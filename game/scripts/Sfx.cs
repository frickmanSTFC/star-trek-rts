using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Game sounds (docs/sound-contract.md): WAVs under res://sounds/&lt;group&gt;/&lt;name&gt;.wav, loaded on first use and
/// cached; a missing file is silence with one warning per path. No autoload: the first call creates a
/// <see cref="SfxHost"/> under the scene tree's root (added deferred, so it is safe from any _Ready), which survives scene
/// changes and holds the players. Buses UI, Effects and Ambience (all to Master) are created with AudioServer if the
/// project has none (GameSettings sets their volumes).
/// <list type="bullet">
/// <item><see cref="Ui"/>: flat sounds on UI. The host runs with ProcessMode Always, so they play while the tree is
/// paused (the pause card). At most one UI sound starts per frame: a later one in the same frame only replaces it when
/// it matters more (hover &lt; click/back/open/close/locked &lt; confirm/place/cancel/error &lt; tutorial), so a click that
/// also opens a card is one sound. Hover is rate-limited and ignores buttons rebuilt under a resting mouse.</item>
/// <item><see cref="At"/>: one-shot AudioStreamPlayer3D on Effects from a small pool under a pausable node (they pause
/// with the game). <see cref="Loop"/>: a looping 3D player under the caller's node that the caller drives (freed with
/// it). Both use <see cref="Configure3D"/>: inverse-square falloff with unit size 150 (about -2 dB at the landing
/// camera's 170, about -31 dB at 900, cut at 1000), no distance low-pass, no doppler.</item>
/// <item><see cref="Select"/>: the selection hum on Effects (flat), falling back to default_building /
/// default_vehicle; a new hum fades the old one out over 0.12 s.</item>
/// <item><see cref="Wire"/>: hover/click/locked sounds for every BaseButton under a root, now and later
/// (SceneTree.NodeAdded, one handler on the host; each button is hooked once, marked with meta).</item>
/// </list>
/// Loop files (name ending in _loop) get LoopMode Forward over the whole file in code, since no .import files are
/// committed.
/// </summary>
public static class Sfx
{
    public const string UiBus = "UI", EffectsBus = "Effects", AmbienceBus = "Ambience";

    /// <summary>Meta on a button: the sound its click plays instead of the default ("back", "confirm", "cancel",
    /// "locked", "none" = no click sound (hover stays), "mute" = no sound at all, …). Set it with <see cref="Tag"/>.</summary>
    public const string SoundMeta = "sfx";

    internal static readonly StringName UiBusName = UiBus, EffectsBusName = EffectsBus;
    internal static readonly StringName WiredMeta = "_sfx_wired", SoundMetaName = SoundMeta;

    private static SfxHost? _host;
    private static readonly Dictionary<string, AudioStream?> Streams = new();
    private static readonly Dictionary<string, string> UiPaths = new();
    private static ulong _lastError;

    // ---------------------------------------------------------------- the API (docs/sound-contract.md)

    /// <summary>A UI sound by name ("click", "hover", …) on the UI bus.</summary>
    public static void Ui(string name, float volumeDb = 0f, float pitch = 1f)
    {
        var host = Host();
        if (host == null || !host.IsInsideTree()) return;
        if (!UiPaths.TryGetValue(name, out var path)) UiPaths[name] = path = "ui/" + name;
        var stream = Stream(path);
        if (stream == null) return;
        host.PlayUi(stream, PriorityOf(name), volumeDb, pitch);
    }

    /// <summary>A UI sound a little later (real time, so it also runs while paused), e.g. the next card after a step's
    /// "done" chime.</summary>
    public static void UiLater(string name, float seconds, float volumeDb = 0f)
    {
        var host = Host();
        host?.Later(name, seconds, volumeDb);
    }

    /// <summary>A one-shot positional sound ("landing/touchdown") on the Effects bus.</summary>
    public static void At(string path, Vector3 position, float volumeDb = 0f, float pitch = 1f)
    {
        var host = Host();
        if (host == null || !host.IsInsideTree()) return;
        var stream = Stream(path);
        if (stream == null) return;
        host.PlayAt(stream, position, volumeDb, pitch);
    }

    /// <summary>A looping positional player under <paramref name="parent"/> that the caller drives (VolumeDb,
    /// PitchScale, Play/Stop); not playing yet; freed with the parent. Null while the file is missing.</summary>
    public static AudioStreamPlayer3D? Loop(Node3D parent, string path)
    {
        Host(); // buses + settings
        var stream = Stream(path);
        if (stream == null || !GodotObject.IsInstanceValid(parent)) return null;
        var player = new AudioStreamPlayer3D { Name = "Sfx_" + path.Replace('/', '_'), Stream = stream };
        Configure3D(player);
        parent.AddChild(player);
        return player;
    }

    /// <summary>The selection hum of a building or vehicle id (falls back to default_building / default_vehicle).</summary>
    public static void Select(string id, bool vehicle)
    {
        var host = Host();
        if (host == null || !host.IsInsideTree()) return;
        var path = "select/" + id;
        var stream = ResourceLoader.Exists(FileOf(path)) || Streams.ContainsKey(path) ? Stream(path) : null;
        stream ??= Stream(vehicle ? "select/default_vehicle" : "select/default_building");
        if (stream == null) return;
        host.PlaySelect(stream);
    }

    /// <summary>A flat sound on another bus that also plays while paused (the settings card's volume previews).</summary>
    public static void Preview(string path, string bus)
    {
        var host = Host();
        if (host == null || !host.IsInsideTree()) return;
        var stream = Stream(path);
        if (stream == null) return;
        host.PlayFlat(stream, bus, 0f, 1f);
    }

    /// <summary>An order was refused (the event feed's "Can't …" lines): "error", at most every 0.4 s.</summary>
    public static void Refused()
    {
        var now = Time.GetTicksMsec();
        if (now - _lastError < 400 && _lastError != 0) return;
        _lastError = now;
        Ui("error");
    }

    /// <summary>Hover/click sounds for every button under <paramref name="root"/>, now and those added later. Safe to call
    /// more than once (a button is hooked once).</summary>
    public static void Wire(Node root)
    {
        var host = Host();
        if (host == null || !GodotObject.IsInstanceValid(root)) return;
        host.AddRoot(root);
        WireTree(root);
    }

    /// <summary>The sound <paramref name="button"/>'s click plays ("back", "confirm", "cancel", "locked", "none", …).</summary>
    public static void Tag(BaseButton? button, string sound)
    {
        if (button != null && GodotObject.IsInstanceValid(button)) button.SetMeta(SoundMetaName, sound);
    }

    // ---------------------------------------------------------------- set-up

    /// <summary>The host, created on first use (null while there is no scene tree).</summary>
    private static SfxHost? Host()
    {
        if (_host != null && GodotObject.IsInstanceValid(_host) && !_host.IsQueuedForDeletion()) return _host;
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null) return null;
        EnsureBuses();
        GameSettings.ApplyAudio(); // also for a direct F6 run of a scene that never went through the menu
        _host = new SfxHost(tree) { Name = "SfxHost" };
        // Deferred: the root may be busy adding the current scene (we may be inside its _Ready).
        tree.Root.CallDeferred(Node.MethodName.AddChild, _host);
        return _host;
    }

    /// <summary>UI, Effects and Ambience buses (sending to Master), added at the end of the layout if missing.</summary>
    internal static void EnsureBuses()
    {
        Ensure(UiBus);
        Ensure(EffectsBus);
        Ensure(AmbienceBus);

        static void Ensure(string name)
        {
            if (AudioServer.GetBusIndex(name) >= 0) return;
            AudioServer.AddBus();
            var index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }
    }

    /// <summary>Distance falloff for the game's positional sounds (sized to the strategy camera: the landing camera
    /// sits ~170 from the ship).</summary>
    internal static void Configure3D(AudioStreamPlayer3D p)
    {
        p.Bus = EffectsBusName;
        p.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance;
        p.UnitSize = 150f;
        p.MaxDb = 3f;
        p.MaxDistance = 1000f;
        p.AttenuationFilterDb = 0f; // no distance low-pass: rocket noise stays crisp from the camera
        p.PanningStrength = 0.6f;   // a top-down camera: keep the stereo spread gentle
        p.DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled;
    }

    private static string FileOf(string path) => $"res://sounds/{path}.wav";

    /// <summary>The cached stream for "group/name", or null (one warning) when the file isn't there.</summary>
    internal static AudioStream? Stream(string path)
    {
        if (Streams.TryGetValue(path, out var cached)) return cached;
        var file = FileOf(path);
        AudioStream? stream = null;
        try
        {
            if (ResourceLoader.Exists(file)) stream = ResourceLoader.Load(file) as AudioStream;
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"Sfx: {file} didn't load ({e.Message})");
        }
        if (stream == null) GD.PushWarning($"Sfx: no sound at {file} (silent)");
        else if (path.EndsWith("_loop")) MakeLoop(stream);
        Streams[path] = stream;
        return stream;
    }

    /// <summary>Loop the whole file (WAV imports come without loop points unless the .import says so).</summary>
    private static void MakeLoop(AudioStream stream)
    {
        switch (stream)
        {
            case AudioStreamWav wav when wav.LoopMode == AudioStreamWav.LoopModeEnum.Disabled:
            {
                var channels = wav.Stereo ? 2 : 1;
                long frames = wav.Format switch
                {
                    AudioStreamWav.FormatEnum.Format8Bits => wav.Data.Length / channels,
                    AudioStreamWav.FormatEnum.Format16Bits => wav.Data.Length / (2 * channels),
                    _ => (long)System.Math.Round(wav.GetLength() * wav.MixRate), // compressed (IMA ADPCM / QOA)
                };
                if (frames <= 1) return;
                wav.LoopBegin = 0;
                wav.LoopEnd = (int)frames;
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                break;
            }
            case AudioStreamOggVorbis ogg:
                ogg.Loop = true;
                break;
            case AudioStreamMP3 mp3:
                mp3.Loop = true;
                break;
        }
    }

    /// <summary>Which UI sound wins when two start in the same frame.</summary>
    private static int PriorityOf(string name) => name switch
    {
        "hover" => 0,
        "confirm" or "place" or "cancel" or "error" => 2,
        "tutorial" or "tutorial_done" => 3,
        _ => 1,
    };

    // ---------------------------------------------------------------- buttons

    private static void WireTree(Node node)
    {
        if (node is BaseButton b) Hook(b);
        var n = node.GetChildCount();
        for (var i = 0; i < n; i++) WireTree(node.GetChild(i));
    }

    internal static void Hook(BaseButton b)
    {
        if (b.HasMeta(WiredMeta)) return;
        b.SetMeta(WiredMeta, true);
        b.MouseEntered += () =>
        {
            if (GodotObject.IsInstanceValid(b) && !b.Disabled && SoundOf(b) != "mute") _host?.Hover(b);
        };
        b.Pressed += () =>
        {
            if (!GodotObject.IsInstanceValid(b)) return;
            var sound = SoundOf(b);
            if (sound is not ("none" or "mute")) Ui(sound);
        };
        b.GuiInput += e =>
        {
            // A disabled (locked) button still gets its clicks as gui_input, but never Pressed.
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && GodotObject.IsInstanceValid(b) && b.Disabled
                && SoundOf(b) is not ("none" or "mute"))
                Ui("locked");
        };
    }

    /// <summary>The click sound: the button's tag, else "back" for back / close / cancel buttons, else "click".</summary>
    private static string SoundOf(BaseButton b)
    {
        if (b.HasMeta(SoundMetaName)) return b.GetMeta(SoundMetaName).AsString();
        if (b is Button { Text: { Length: > 0 } text })
        {
            if (text.StartsWith('‹') || text == "×" || text.StartsWith("BACK") || text.StartsWith("CLOSE") || text.StartsWith("CANCEL")
                || text.StartsWith("DONE"))
                return "back";
        }
        return "click";
    }
}

/// <summary>
/// The players behind <see cref="Sfx"/>: lives under the scene tree's root for the whole run (ProcessMode Always). UI
/// players are direct children (they play while paused); the positional pool and the selection hums sit under a
/// pausable child, so they pause with the game. Also the one SceneTree.NodeAdded handler that hooks new buttons under
/// the roots given to <see cref="Sfx.Wire"/> (roots are dropped once freed; unsubscribed when the host leaves the tree).
/// </summary>
public partial class SfxHost : Node
{
    private const int UiPlayers = 6, AtPlayers = 10;
    private const ulong HoverEveryMs = 70;

    private readonly AudioStreamPlayer[] _ui = new AudioStreamPlayer[UiPlayers];
    private readonly AudioStreamPlayer3D[] _at = new AudioStreamPlayer3D[AtPlayers];
    private readonly AudioStreamPlayer[] _select = new AudioStreamPlayer[2];
    private readonly Tween?[] _selectFade = new Tween?[2];
    private readonly AudioStreamPlayer _preview;
    private int _uiNext, _atNext, _selectNext;
    private readonly List<Node> _roots = new();
    private SceneTree? _tree;

    private ulong _frame = ulong.MaxValue;
    private int _framePriority;
    private AudioStreamPlayer? _framePlayer;
    private ulong _lastHover;
    private Vector2 _hoverMouse = new(-1e6f, -1e6f);

    private readonly List<(string Name, ulong AtMs, float Db)> _later = new();

    public SfxHost(SceneTree tree)
    {
        ProcessMode = ProcessModeEnum.Always;
        for (var i = 0; i < UiPlayers; i++)
        {
            _ui[i] = new AudioStreamPlayer { Name = $"Ui{i}", Bus = Sfx.UiBusName };
            AddChild(_ui[i]);
        }
        _preview = new AudioStreamPlayer { Name = "Preview", Bus = Sfx.UiBusName };
        AddChild(_preview);
        var effects = new Node { Name = "Effects", ProcessMode = ProcessModeEnum.Pausable };
        AddChild(effects);
        for (var i = 0; i < AtPlayers; i++)
        {
            _at[i] = new AudioStreamPlayer3D { Name = $"At{i}" };
            Sfx.Configure3D(_at[i]);
            effects.AddChild(_at[i]);
        }
        for (var i = 0; i < 2; i++)
        {
            _select[i] = new AudioStreamPlayer { Name = $"Select{i}", Bus = Sfx.EffectsBusName };
            effects.AddChild(_select[i]);
        }
        _tree = tree;
        tree.NodeAdded += OnNodeAdded; // now, not on entering the tree: buttons added in this frame's _Ready count too
    }

    // Parameterless constructor for Godot's script machinery; such an instance plays nothing.
    public SfxHost()
    {
        _preview = new AudioStreamPlayer();
    }

    public override void _ExitTree()
    {
        if (_tree != null && GodotObject.IsInstanceValid(_tree)) _tree.NodeAdded -= OnNodeAdded;
        _tree = null;
    }

    public override void _EnterTree()
    {
        // Back in the tree after leaving it (never in practice): subscribe again.
        if (_tree == null && GetTree() is { } tree)
        {
            _tree = tree;
            tree.NodeAdded += OnNodeAdded;
        }
    }

    public override void _Process(double delta)
    {
        if (_later.Count == 0) return;
        var now = Time.GetTicksMsec();
        for (var i = _later.Count - 1; i >= 0; i--)
        {
            if (_later[i].AtMs > now) continue;
            var (name, _, db) = _later[i];
            _later.RemoveAt(i);
            Sfx.Ui(name, db);
        }
    }

    internal void Later(string name, float seconds, float db) =>
        _later.Add((name, Time.GetTicksMsec() + (ulong)Mathf.Max(0f, seconds * 1000f), db));

    // ---------------------------------------------------------------- playing

    internal void PlayUi(AudioStream stream, int priority, float db, float pitch)
    {
        var frame = Engine.GetProcessFrames();
        if (frame == _frame)
        {
            if (priority <= _framePriority) return; // one UI sound per frame: the more important one
            if (_framePlayer != null && _framePlayer.Playing) _framePlayer.Stop();
        }
        var p = Next(_ui, ref _uiNext);
        p.Stream = stream;
        p.VolumeDb = db;
        p.PitchScale = pitch;
        p.Play();
        _frame = frame;
        _framePriority = priority;
        _framePlayer = p;
    }

    internal void PlayFlat(AudioStream stream, string bus, float db, float pitch)
    {
        _preview.Stream = stream;
        _preview.Bus = bus;
        _preview.VolumeDb = db;
        _preview.PitchScale = pitch;
        _preview.Play();
    }

    internal void PlayAt(AudioStream stream, Vector3 position, float db, float pitch)
    {
        AudioStreamPlayer3D? p = null;
        for (var i = 0; i < AtPlayers; i++)
        {
            var c = _at[(_atNext + i) % AtPlayers];
            if (c.Playing) continue;
            p = c;
            break;
        }
        p ??= _at[_atNext]; // all busy: the oldest-started one
        _atNext = (System.Array.IndexOf(_at, p) + 1) % AtPlayers;
        p.Stream = stream;
        p.GlobalPosition = position;
        p.VolumeDb = db;
        p.PitchScale = pitch;
        p.Play();
    }

    internal void PlaySelect(AudioStream stream)
    {
        var old = _select[_selectNext];
        if (old.Playing)
        {
            // Duck the hum that is still playing: a quick fade, then stop.
            _selectFade[_selectNext]?.Kill();
            var fade = old.CreateTween();
            fade.TweenProperty(old, "volume_db", -40f, 0.12);
            fade.TweenCallback(Callable.From(old.Stop));
            _selectFade[_selectNext] = fade;
        }
        _selectNext = 1 - _selectNext;
        var p = _select[_selectNext];
        _selectFade[_selectNext]?.Kill();
        _selectFade[_selectNext] = null;
        p.Stream = stream;
        p.VolumeDb = 0f;
        p.Play();
    }

    internal void Hover(Control c)
    {
        var now = Time.GetTicksMsec();
        var mouse = c.GetViewport()?.GetMousePosition() ?? Vector2.Zero;
        // A button rebuilt under a mouse that hasn't moved fires MouseEntered again: not a new hover.
        if (mouse.DistanceSquaredTo(_hoverMouse) < 1f) return;
        if (now - _lastHover < HoverEveryMs) return;
        _lastHover = now;
        _hoverMouse = mouse;
        Sfx.Ui("hover");
    }

    private static T Next<T>(T[] pool, ref int next) where T : AudioStreamPlayer
    {
        for (var i = 0; i < pool.Length; i++)
        {
            var c = pool[(next + i) % pool.Length];
            if (c.Playing) continue;
            next = (next + i + 1) % pool.Length;
            return c;
        }
        var oldest = pool[next];
        next = (next + 1) % pool.Length;
        return oldest;
    }

    // ---------------------------------------------------------------- buttons added later

    internal void AddRoot(Node root)
    {
        _roots.RemoveAll(r => !GodotObject.IsInstanceValid(r));
        if (!_roots.Contains(root)) _roots.Add(root);
    }

    private void OnNodeAdded(Node node)
    {
        if (node is not BaseButton b || b.HasMeta(Sfx.WiredMeta)) return;
        for (var i = _roots.Count - 1; i >= 0; i--)
        {
            var root = _roots[i];
            if (!GodotObject.IsInstanceValid(root))
            {
                _roots.RemoveAt(i);
                continue;
            }
            if (root == b || root.IsAncestorOf(b))
            {
                Sfx.Hook(b);
                return;
            }
        }
    }
}
