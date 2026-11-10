using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

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

    // Mute while the window is in the background (GameSettings.InBackground): focus lost or the mouse outside it.
    // Both start true: until the first notification we don't know, and a fresh run usually has the mouse and focus.
    private bool _focused = true, _mouseInside = true;

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut) _focused = false;
        else if (what == NotificationApplicationFocusIn || what == NotificationWMWindowFocusIn) _focused = true;
        else if (what == NotificationWMMouseExit) _mouseInside = false;
        else if (what == NotificationWMMouseEnter) _mouseInside = true;
        else return;
        GameSettings.SetBackground(!_focused || !_mouseInside);
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
