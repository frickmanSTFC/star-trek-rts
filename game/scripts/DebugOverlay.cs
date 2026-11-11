using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace FinalFrontier;

/// <summary>What the scene running the overlay adds (PlanetDriver.Debug.cs on the surface and in orbit; none on the main
/// menu, which then shows only Performance, Rendering and Memory). Every call is render-side and read-only.</summary>
public interface IDebugHost
{
    /// <summary>Where the overlay's cards may go this frame, in viewport pixels (they stack from the top-left corner and
    /// wrap into further columns to the right).</summary>
    Rect2 DebugArea(Vector2 screen);

    /// <summary>Extra Performance lines (sim step timing, ticks a second, the F7 compare numbers).</summary>
    void DebugPerformance(StringBuilder sb);

    void DebugSimulation(StringBuilder sb);
    void DebugCursor(StringBuilder sb);
    void DebugSelection(StringBuilder sb);

    /// <summary>Every frame while the overlay is on (world draws).</summary>
    void DebugFrame(float delta);

    /// <summary>At 4 Hz: whether cards are shown and their right edge (the host moves what sits there, e.g. the event feed).</summary>
    void DebugPlaced(bool shown, float right);

    /// <summary>The overlay went off (or is leaving the tree): free the world draws, stop timing.</summary>
    void DebugStop();
}

/// <summary>
/// Measuring a viewport's render time is one switch shared by whoever wants it (the F7 readout in
/// PlanetDriver.Graphics.cs and this overlay): on while anyone asks.
/// </summary>
public static class RenderTimer
{
    private static readonly HashSet<string> Wants = new();

    public static void Want(Viewport? viewport, string who, bool on)
    {
        if (viewport == null) return;
        var before = Wants.Count > 0;
        if (on) Wants.Add(who);
        else Wants.Remove(who);
        var now = Wants.Count > 0;
        if (now != before) RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), now);
    }
}

/// <summary>
/// The debug overlay (owner 2026-11-11: "we need to add framerate and other debug options to the game that can be turned
/// on/off"; DebugOptions holds the switches, Settings › Debug sets them). F8 toggles it (saved), Shift+F8 cycles the
/// renderer's debug view. Cards in the HUD's look, top-left under the top bar (the host says where), mouse-transparent,
/// one per section: Performance (FPS, frame ms avg / max / 1% low, a 120-frame graph with 16.7 / 33.3 ms lines, GPU and
/// CPU ms, plus the host's sim step lines), Rendering, Memory, and the host's Simulation, Cursor & camera, Selection.
/// Text updates at 4 Hz, the graph every frame without allocating; nothing runs while it is off (SetProcess(false)).
/// Render-only: never touches the sim. Its own CanvasLayer 7 (with the F7 readout; under the pause card, 8), ProcessMode
/// Always so it keeps counting while the tree is paused.
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    /// <summary>Set before it enters the tree (null on the main menu).</summary>
    public IDebugHost? Host { get; set; }

    private const int History = 600; // frames kept for the 1% low (~10 s at 60 FPS)
    private const int GraphFrames = 120;
    private const double TextInterval = 0.25;

    private readonly float[] _frames = new float[History]; // frame ms, ring
    private readonly float[] _sorted = new float[History];
    private int _frameAt, _frameCount;
    private float _windowSum, _windowMax, _gpuSum, _cpuSum;
    private int _windowFrames, _gpuFrames;
    private double _textAt = -1, _now;
    private bool _on, _measuring;
    private DebugRenderMode _appliedMode = DebugRenderMode.Off;
    private Vector2 _builtSize;
    private float _s = 1f;

    private readonly StringBuilder _sb = new();
    private Control _root = null!;
    private VFlowContainer _flow = null!;
    private Label _header = null!;
    private DebugFrameGraph _graph = null!;
    private Card _perf = null!, _render = null!, _memory = null!, _sim = null!, _cursor = null!, _selection = null!;

    private sealed class Card
    {
        public PanelContainer Panel = null!;
        public Label Title = null!, Body = null!;
    }

    private static SystemFont? _mono;

    /// <summary>A monospace face for the numbers (falls back through common system fonts).</summary>
    public static Font Mono => _mono ??= new SystemFont
    {
        FontNames = new[] { "JetBrains Mono", "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "Liberation Mono", "Menlo", "Courier New", "monospace" },
    };

    public override void _Ready()
    {
        Layer = 7;
        ProcessMode = ProcessModeEnum.Always;
        _root = new Control { Name = "DebugRoot", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        Build();
        DebugOptions.Changed += OnOptionsChanged;
        DebugOptions.Apply(); // the FPS cap also on a direct F6 run of a scene
        Refresh();
    }

    public override void _ExitTree()
    {
        DebugOptions.Changed -= OnOptionsChanged;
        if (_on) TurnOff();
    }

    private void OnOptionsChanged()
    {
        if (!IsInstanceValid(this) || !IsInsideTree()) return;
        Refresh();
    }

    /// <summary>F8: the whole overlay on / off (saved). Shift+F8: the next renderer view (turns the overlay on).</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.F8 } key) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit) return; // typing in a test-panel field
        if (key.ShiftPressed)
        {
            DebugOptions.RenderMode = (DebugRenderMode)(((int)DebugOptions.RenderMode + 1) % ((int)DebugRenderMode.Lighting + 1));
            DebugOptions.Overlay = true;
        }
        else DebugOptions.Overlay = !DebugOptions.Overlay;
        GameSettings.Save();
        DebugOptions.NotifyChanged();
        GetViewport().SetInputAsHandled();
    }

    // ---------------------------------------------------------------- state

    /// <summary>Applies the options: visibility, which cards, measuring, the renderer view. Cheap; on Changed and _Ready.</summary>
    private void Refresh()
    {
        var on = DebugOptions.Overlay;
        if (on && !_on)
        {
            _on = true;
            _frameCount = _frameAt = 0;
            _windowSum = _windowMax = _gpuSum = _cpuSum = 0f;
            _windowFrames = _gpuFrames = 0;
            _textAt = -1;
        }
        else if (!on && _on) TurnOff();
        Visible = on;
        SetProcess(on);
        if (!on) return;

        _perf.Panel.Visible = DebugOptions.Performance;
        _render.Panel.Visible = DebugOptions.Rendering;
        _memory.Panel.Visible = DebugOptions.Memory;
        _sim.Panel.Visible = Host != null && DebugOptions.Simulation;
        _cursor.Panel.Visible = Host != null && DebugOptions.Cursor;
        _selection.Panel.Visible = Host != null && DebugOptions.Selection;

        var measure = DebugOptions.Performance || DebugOptions.Rendering;
        if (measure != _measuring)
        {
            _measuring = measure;
            RenderTimer.Want(GetViewport(), "debug_overlay", measure);
        }
        ApplyRenderMode(DebugOptions.RenderMode);
        _textAt = -1; // fill the text on the next frame
    }

    private void TurnOff()
    {
        _on = false;
        if (_measuring)
        {
            _measuring = false;
            RenderTimer.Want(GetViewport(), "debug_overlay", false);
        }
        ApplyRenderMode(DebugRenderMode.Off);
        Host?.DebugPlaced(false, 0f);
        Host?.DebugStop();
    }

    private void ApplyRenderMode(DebugRenderMode mode)
    {
        if (mode == _appliedMode) return;
        _appliedMode = mode;
        var viewport = GetViewport();
        if (viewport != null) viewport.DebugDraw = DebugOptions.DebugDrawOf(mode);
    }

    // ---------------------------------------------------------------- per frame

    public override void _Process(double delta)
    {
        var ms = (float)(delta * 1000.0);
        _now += delta;
        _frames[_frameAt] = ms;
        _frameAt = (_frameAt + 1) % History;
        if (_frameCount < History) _frameCount++;
        _windowSum += ms;
        _windowMax = Mathf.Max(_windowMax, ms);
        _windowFrames++;
        if (_measuring)
        {
            var rid = GetViewport().GetViewportRid();
            var gpu = (float)RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
            if (gpu > 0f)
            {
                _gpuSum += gpu;
                _cpuSum += (float)(RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid) + RenderingServer.GetFrameSetupTimeCpu());
                _gpuFrames++;
            }
        }
        if (_perf.Panel.Visible) _graph.QueueRedraw();
        Host?.DebugFrame((float)delta);

        if (_textAt >= 0 && _now - _textAt < TextInterval) return;
        _textAt = _now;
        UpdateText();
        Place();
    }

    /// <summary>The cards' corner and the wrap height; rebuilt for a new UI scale.</summary>
    private void Place()
    {
        var screen = GetViewport().GetVisibleRect().Size;
        if (screen != _builtSize)
        {
            _builtSize = screen;
            var s = Mathf.Clamp(HudKit.ScaleFor(screen), 0.85f, 1.6f);
            if (!Mathf.IsEqualApprox(s, _s))
            {
                _s = s;
                Restyle();
            }
        }
        var area = Host?.DebugArea(screen) ?? new Rect2(12, 12, screen.X * 0.6f, screen.Y - 24);
        _flow.Position = area.Position;
        _flow.Size = new Vector2(Mathf.Max(200f, area.Size.X), Mathf.Max(120f, area.Size.Y));
        var right = area.Position.X;
        var any = false;
        foreach (var child in _flow.GetChildren())
            if (child is Control { Visible: true } c)
            {
                any = true;
                right = Mathf.Max(right, area.Position.X + c.Position.X + c.Size.X);
            }
        Host?.DebugPlaced(any, right);
    }

    private void UpdateText()
    {
        var sb = _sb;
        var mode = DebugOptions.RenderMode;
        var shown = _perf.Panel.Visible || _render.Panel.Visible || _memory.Panel.Visible || _sim.Panel.Visible || _cursor.Panel.Visible || _selection.Panel.Visible;
        _header.Text = shown
            ? $"DEBUG  ·  F8 hide  ·  Shift+F8 view {DebugOptions.RenderModeName(mode)}"
            : "DEBUG  ·  F8 hide  ·  no sections on: Settings › Debug";

        if (_perf.Panel.Visible)
        {
            sb.Clear();
            var avg = _windowFrames > 0 ? _windowSum / _windowFrames : 0f;
            var low = OnePercentLow();
            sb.Append($"{Engine.GetFramesPerSecond(),4:0} FPS   frame {avg,5:0.0} ms  max {_windowMax,5:0.0}\n");
            sb.Append(low > 0f ? $"1% low {1000f / low,4:0} FPS ({low:0.0} ms, last {_frameCount} frames)\n" : "1% low  –\n");
            if (_gpuFrames > 0) sb.Append($"GPU {_gpuSum / _gpuFrames,5:0.00} ms   render CPU {_cpuSum / _gpuFrames,5:0.00} ms\n");
            else sb.Append("GPU  – (measuring…)\n");
            var process = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
            var physics = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
            sb.Append($"process {process,5:0.00} ms   physics {physics,5:0.00} ms\n");
            Host?.DebugPerformance(sb);
            sb.Append(Engine.MaxFps > 0 ? $"cap {Engine.MaxFps} FPS" : "no FPS cap");
            sb.Append(DisplayServer.WindowGetVsyncMode() == DisplayServer.VSyncMode.Disabled ? " · VSync off" : " · VSync on (caps FPS; GPU ms is the real cost)");
            SetBody(_perf, sb);
            _windowSum = _windowMax = _gpuSum = _cpuSum = 0f;
            _windowFrames = _gpuFrames = 0;
        }
        else
        {
            _windowSum = _windowMax = _gpuSum = _cpuSum = 0f;
            _windowFrames = _gpuFrames = 0;
        }

        if (_render.Panel.Visible)
        {
            sb.Clear();
            var calls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            var objects = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
            var prims = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
            sb.Append($"draw calls {calls,7:N0}   objects {objects,7:N0}\n");
            sb.Append($"primitives {Count(prims)}\n");
            sb.Append($"video {Mb(Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed))}  textures {Mb(Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed))}  buffers {Mb(Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed))}\n");
            var compiles = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsCanvas) + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsMesh)
                + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSurface) + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw)
                + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSpecialization);
            sb.Append($"pipeline compiles {compiles:N0} (each one is a stutter)\n");
            sb.Append($"graphics {GraphicsQuality.ModeName}{(GraphicsQuality.CompareClassic ? " (F7 compare)" : "")} · {RenderingServer.GetCurrentRenderingMethod()}\n");
            sb.Append("on: ").Append(FeaturesOn()).Append('\n');
            var vp = GetViewport();
            var size = vp.GetVisibleRect().Size;
            sb.Append($"{size.X:0}×{size.Y:0} · MSAA {vp.Msaa3D} · {vp.Scaling3DMode} ×{vp.Scaling3DScale:0.##}{(vp.UseTaa ? " · TAA" : "")}\n");
            sb.Append($"view {DebugOptions.RenderModeName(mode)} (Shift+F8)\n");
            sb.Append(Adapter);
            SetBody(_render, sb);
        }

        if (_memory.Panel.Visible)
        {
            sb.Clear();
            var stat = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
            var statMax = Performance.GetMonitor(Performance.Monitor.MemoryStaticMax);
            sb.Append(stat > 0 ? $"static {Mb(stat)} (peak {Mb(statMax)})\n" : "static  – (release build)\n");
            sb.Append($"C# heap {Mb(GC.GetTotalMemory(false))} · GC {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}\n");
            sb.Append($"objects {Performance.GetMonitor(Performance.Monitor.ObjectCount),8:N0}   resources {Performance.GetMonitor(Performance.Monitor.ObjectResourceCount),6:N0}\n");
            var orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
            sb.Append($"nodes   {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),8:N0}   orphan nodes {orphans,4:N0}{(orphans > 0 ? " (leak?)" : "")}");
            SetBody(_memory, sb);
        }

        if (Host == null) return;
        if (_sim.Panel.Visible) Fill(_sim, Host.DebugSimulation);
        if (_cursor.Panel.Visible) Fill(_cursor, Host.DebugCursor);
        if (_selection.Panel.Visible) Fill(_selection, Host.DebugSelection);
    }

    private void Fill(Card card, Action<StringBuilder> fill)
    {
        _sb.Clear();
        fill(_sb);
        SetBody(card, _sb);
    }

    private static void SetBody(Card card, StringBuilder sb)
    {
        // Trailing newlines and \r (AppendLine on Windows shows blank lines in Labels) out.
        while (sb.Length > 0 && (sb[^1] == '\n' || sb[^1] == '\r')) sb.Length--;
        sb.Replace("\r", "");
        var text = sb.ToString();
        if (card.Body.Text != text) card.Body.Text = text;
    }

    /// <summary>The 99th-percentile frame time over the history (ms), 0 before 30 frames.</summary>
    private float OnePercentLow()
    {
        if (_frameCount < 30) return 0f;
        Array.Copy(_frames, _sorted, _frameCount);
        Array.Sort(_sorted, 0, _frameCount);
        return _sorted[Mathf.Clamp((int)(_frameCount * 0.99f), 0, _frameCount - 1)];
    }

    private static string FeaturesOn()
    {
        var parts = new List<string>(8);
        if (GraphicsQuality.Gi) parts.Add("GI");
        if (GraphicsQuality.Fog) parts.Add("vol.fog");
        if (GraphicsQuality.Grading) parts.Add("grading");
        if (GraphicsQuality.Shadows) parts.Add("shadows");
        if (GraphicsQuality.Terrain) parts.Add("terrain");
        if (GraphicsQuality.Decals) parts.Add("decals");
        if (GraphicsQuality.Effects) parts.Add("effects");
        if (GraphicsQuality.AntiAlias) parts.Add("AA");
        return parts.Count == 0 ? "none (the Classic look)" : string.Join(" ", parts);
    }

    private static string? _adapter;

    private static string Adapter => _adapter ??=
        $"{RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterType()}, {RenderingServer.GetVideoAdapterApiVersion()})";

    private static string Mb(double bytes) => bytes >= 1024.0 * 1024 * 1024 ? $"{bytes / (1024.0 * 1024 * 1024):0.00} GB" : $"{bytes / (1024.0 * 1024):0} MB";

    private static string Count(double n) => n >= 1e6 ? $"{n / 1e6:0.00} M" : n >= 1e4 ? $"{n / 1e3:0.0} k" : $"{n:0}";

    // ---------------------------------------------------------------- building

    private void Build()
    {
        _flow = new VFlowContainer { Name = "Cards", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_flow);

        var head = HudKit.PanelBox(new Color(HudKit.Panel, 0.8f), HudKit.Line, 4, 8, 3);
        _header = HudKit.Label("DEBUG", 12, HudKit.Accent, condensed: true, bold: true);
        head.AddChild(_header);
        _flow.AddChild(head);

        _perf = MakeCard("PERFORMANCE");
        _graph = new DebugFrameGraph { Source = this, MouseFilter = Control.MouseFilterEnum.Ignore };
        ((VBoxContainer)_perf.Body.GetParent()).AddChild(_graph);
        _render = MakeCard("RENDERING");
        _memory = MakeCard("MEMORY");
        _sim = MakeCard("SIMULATION");
        _cursor = MakeCard("CURSOR & CAMERA");
        _selection = MakeCard("SELECTION");
        Restyle();
    }

    private Card MakeCard(string title)
    {
        var card = new Card { Panel = HudKit.PanelBox(new Color(HudKit.Panel, 0.8f), HudKit.Line, 5, 9, 6) };
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        card.Title = HudKit.Label(title, 12, HudKit.Accent, condensed: true, bold: true);
        card.Body = HudKit.Label("…", 12, HudKit.Ink);
        card.Body.AddThemeFontOverride("font", Mono);
        box.AddChild(card.Title);
        box.AddChild(card.Body);
        card.Panel.AddChild(box);
        card.Panel.Visible = false;
        _flow.AddChild(card.Panel);
        return card;
    }

    /// <summary>Font sizes, paddings and the graph's size for the UI scale.</summary>
    private void Restyle()
    {
        var s = _s;
        _flow.AddThemeConstantOverride("h_separation", Mathf.RoundToInt(6 * s));
        _flow.AddThemeConstantOverride("v_separation", Mathf.RoundToInt(5 * s));
        _header.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * s));
        foreach (var card in new[] { _perf, _render, _memory, _sim, _cursor, _selection })
        {
            card.Title.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * s));
            card.Body.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(11 * s));
        }
        _graph.CustomMinimumSize = new Vector2(GraphFrames * 2.4f * s, 48 * s);
    }

    /// <summary>For the graph: the frame-time ring, the next write position and how many frames it holds.</summary>
    internal float[] Frames => _frames;
    internal int FrameAt => _frameAt;
    internal int FrameCount => _frameCount;
    internal const int HistoryFrames = History;
    internal const int GraphLength = GraphFrames;
}

/// <summary>The overlay's last 120 frame times as bars (0–50 ms), with 16.7 ms (60 FPS) and 33.3 ms (30 FPS) lines. Draws
/// straight from the overlay's ring buffer: no allocation per frame.</summary>
public partial class DebugFrameGraph : Control
{
    private const float TopMs = 50f;
    private static readonly Color Back = new(0f, 0f, 0f, 0.35f);
    private static readonly Color Ok = new(0.45f, 0.85f, 0.55f, 0.9f);
    private static readonly Color Slow = new(1f, 0.75f, 0.35f, 0.95f);
    private static readonly Color Bad = new(1f, 0.38f, 0.32f, 1f);
    private static readonly Color Rule60 = new(0.45f, 0.85f, 0.55f, 0.55f);
    private static readonly Color Rule30 = new(1f, 0.45f, 0.35f, 0.55f);

    /// <summary>Set before it enters the tree.</summary>
    public DebugOverlay? Source { get; set; }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), Back);
        if (Source is not { } owner) return;
        var ring = owner.Frames;
        var history = DebugOverlay.HistoryFrames;
        var frames = DebugOverlay.GraphLength;
        var newest = owner.FrameAt;
        var count = Mathf.Min(frames, owner.FrameCount);
        var bar = size.X / frames;
        for (var i = 0; i < count; i++)
        {
            // i = 0 is the newest frame (right edge).
            var ms = ring[((newest - 1 - i) % history + history) % history];
            var h = Mathf.Clamp(ms / TopMs, 0f, 1f) * size.Y;
            var colour = ms > 33.4f ? Bad : ms > 16.8f ? Slow : Ok;
            var x = size.X - (i + 1) * bar;
            DrawRect(new Rect2(x, size.Y - h, Mathf.Max(1f, bar - 0.5f), h), colour);
        }
        var y60 = size.Y * (1f - 16.7f / TopMs);
        var y30 = size.Y * (1f - 33.3f / TopMs);
        DrawLine(new Vector2(0, y60), new Vector2(size.X, y60), Rule60, 1f);
        DrawLine(new Vector2(0, y30), new Vector2(size.X, y30), Rule30, 1f);
        var font = ThemeDB.FallbackFont;
        var fs = Mathf.Max(8, Mathf.RoundToInt(size.Y * 0.2f));
        DrawString(font, new Vector2(2, y60 - 2), "16.7", HorizontalAlignment.Left, -1, fs, Rule60);
        DrawString(font, new Vector2(2, y30 - 2), "33.3", HorizontalAlignment.Left, -1, fs, Rule30);
    }
}
