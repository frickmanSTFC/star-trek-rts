using System;
using Godot;

namespace FinalFrontier;

/// <summary>The renderer's own debug views (Viewport.DebugDraw), as one cycling option.</summary>
public enum DebugRenderMode { Off, Wireframe, Overdraw, Unshaded, Lighting }

/// <summary>
/// The debug overlay's switches (owner 2026-11-11: "we need to add framerate and other debug options to the game that can
/// be turned on/off"). Static, saved in user://settings.cfg [debug] through <see cref="GameSettings"/> (Load / Save /
/// ResetDefaults, like [graphics]). Render-only: nothing here may touch the sim. Works in every build.
/// <para>Off by default: the overlay (F8), every world draw, the renderer view and the FPS cap. The section switches only
/// pick what the overlay shows once it is on; Performance is pre-ticked so a first F8 shows the frame rate.</para>
/// <para>Who reads what: <see cref="DebugOverlay"/> (the panels, the renderer view, F8 / Shift+F8), PlanetDriver.Debug.cs
/// (sim step timing, the world draws, the surface / orbit sections), SettingsPanel's DEBUG tab, <see cref="Apply"/> (the
/// FPS cap, from GameSettings.Apply).</para>
/// </summary>
public static class DebugOptions
{
    private static bool _overlay;
    private static bool _performance = true, _rendering, _memory, _simulation, _cursor, _selection;
    private static bool _hexGrid, _paths, _footprints;
    private static DebugRenderMode _renderMode = DebugRenderMode.Off;
    private static int _fpsCap;

    /// <summary>FPS cap choices (0 = no cap).</summary>
    public static readonly int[] FpsCaps = { 0, 30, 60, 120, 144 };

    // Read user://settings.cfg on first use (GameSettings.Load sets the fields through the setters below; it runs once).
    private static void Ensure() => GameSettings.Load();

    /// <summary>The whole overlay: panels, world draws and the renderer view (F8 flips it, and saves).</summary>
    public static bool Overlay { get { Ensure(); return _overlay; } set { Ensure(); _overlay = value; } }

    // Overlay sections (only matter while the overlay is on).
    public static bool Performance { get { Ensure(); return _performance; } set { Ensure(); _performance = value; } }
    public static bool Rendering { get { Ensure(); return _rendering; } set { Ensure(); _rendering = value; } }
    public static bool Memory { get { Ensure(); return _memory; } set { Ensure(); _memory = value; } }
    public static bool Simulation { get { Ensure(); return _simulation; } set { Ensure(); _simulation = value; } }
    public static bool Cursor { get { Ensure(); return _cursor; } set { Ensure(); _cursor = value; } }
    public static bool Selection { get { Ensure(); return _selection; } set { Ensure(); _selection = value; } }

    // World debug draws on the surface (built only while on with the overlay; freed when off).
    public static bool HexGrid { get { Ensure(); return _hexGrid; } set { Ensure(); _hexGrid = value; } }
    public static bool Paths { get { Ensure(); return _paths; } set { Ensure(); _paths = value; } }
    public static bool Footprints { get { Ensure(); return _footprints; } set { Ensure(); _footprints = value; } }

    /// <summary>The renderer's debug view while the overlay is on (Shift+F8 cycles it).</summary>
    public static DebugRenderMode RenderMode { get { Ensure(); return _renderMode; } set { Ensure(); _renderMode = value; } }

    /// <summary>Most frames a second (0 = no cap); applies whether or not the overlay is on.</summary>
    public static int FpsCap { get { Ensure(); return _fpsCap; } set { Ensure(); _fpsCap = Array.IndexOf(FpsCaps, value) >= 0 ? value : 0; } }

    /// <summary>The effective switches: a section or draw counts only while the overlay is on.</summary>
    public static bool Shows(bool option) => Overlay && option;

    /// <summary>Raised after any switch changes (settings card, F8, Shift+F8, reset). Listeners re-apply what they own.</summary>
    public static event Action? Changed;

    /// <summary>Call after changing any property above.</summary>
    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>The overlay sections in the settings card's order: name, one-line hint, getter, setter.</summary>
    public static readonly (string Name, string Hint, Func<bool> Get, Action<bool> Set)[] Sections =
    {
        ("Performance", "FPS, frame ms avg / max / 1% low, frame graph, GPU, CPU and sim ms", () => Performance, v => Performance = v),
        ("Rendering", "Draw calls, objects, primitives, video memory, graphics mode, GPU", () => Rendering, v => Rendering = v),
        ("Memory", "Static and C# memory, objects, nodes, orphan nodes, collections", () => Memory, v => Memory = v),
        ("Simulation", "Tick, game time, speed, units, buildings, refusals, state hash", () => Simulation, v => Simulation = v),
        ("Cursor & camera", "Camera, the tile under the mouse: terrain, height, fog, comms", () => Cursor, v => Cursor = v),
        ("Selection", "The selected vehicle's or building's state, condition, path, drone", () => Selection, v => Selection = v),
    };

    /// <summary>The world draws in the settings card's order.</summary>
    public static readonly (string Name, string Hint, Func<bool> Get, Action<bool> Set)[] Draws =
    {
        ("Hex grid", "Tile edges over the terrain (surface)", () => HexGrid, v => HexGrid = v),
        ("Vehicle paths", "Each vehicle's route points and remaining tiles as lines", () => Paths, v => Paths = v),
        ("Footprints", "Building / deployed-vehicle footprints (solid) and lane rings (dim)", () => Footprints, v => Footprints = v),
    };

    /// <summary>Overlay label of a renderer view.</summary>
    public static string RenderModeName(DebugRenderMode mode) => mode switch
    {
        DebugRenderMode.Wireframe => "WIREFRAME",
        DebugRenderMode.Overdraw => "OVERDRAW",
        DebugRenderMode.Unshaded => "UNSHADED",
        DebugRenderMode.Lighting => "LIGHTING",
        _ => "OFF",
    };

    public static Viewport.DebugDrawEnum DebugDrawOf(DebugRenderMode mode) => mode switch
    {
        DebugRenderMode.Wireframe => Viewport.DebugDrawEnum.Wireframe,
        DebugRenderMode.Overdraw => Viewport.DebugDrawEnum.Overdraw,
        DebugRenderMode.Unshaded => Viewport.DebugDrawEnum.Unshaded,
        DebugRenderMode.Lighting => Viewport.DebugDrawEnum.Lighting,
        _ => Viewport.DebugDrawEnum.Disabled,
    };

    /// <summary>The FPS cap to the engine (cheap; GameSettings.Apply and the overlay call it).</summary>
    public static void Apply()
    {
        var cap = FpsCap;
        if (Engine.MaxFps != cap) Engine.MaxFps = cap;
    }

    internal static void Load(ConfigFile cfg)
    {
        _overlay = cfg.GetValue("debug", "overlay", _overlay).AsBool();
        _performance = cfg.GetValue("debug", "performance", _performance).AsBool();
        _rendering = cfg.GetValue("debug", "rendering", _rendering).AsBool();
        _memory = cfg.GetValue("debug", "memory", _memory).AsBool();
        _simulation = cfg.GetValue("debug", "simulation", _simulation).AsBool();
        _cursor = cfg.GetValue("debug", "cursor", _cursor).AsBool();
        _selection = cfg.GetValue("debug", "selection", _selection).AsBool();
        _hexGrid = cfg.GetValue("debug", "hex_grid", _hexGrid).AsBool();
        _paths = cfg.GetValue("debug", "paths", _paths).AsBool();
        _footprints = cfg.GetValue("debug", "footprints", _footprints).AsBool();
        var mode = cfg.GetValue("debug", "render_mode", (int)_renderMode).AsInt32();
        _renderMode = mode is >= 0 and <= (int)DebugRenderMode.Lighting ? (DebugRenderMode)mode : DebugRenderMode.Off;
        var cap = cfg.GetValue("debug", "fps_cap", _fpsCap).AsInt32();
        _fpsCap = Array.IndexOf(FpsCaps, cap) >= 0 ? cap : 0;
    }

    internal static void Save(ConfigFile cfg)
    {
        cfg.SetValue("debug", "overlay", _overlay);
        cfg.SetValue("debug", "performance", _performance);
        cfg.SetValue("debug", "rendering", _rendering);
        cfg.SetValue("debug", "memory", _memory);
        cfg.SetValue("debug", "simulation", _simulation);
        cfg.SetValue("debug", "cursor", _cursor);
        cfg.SetValue("debug", "selection", _selection);
        cfg.SetValue("debug", "hex_grid", _hexGrid);
        cfg.SetValue("debug", "paths", _paths);
        cfg.SetValue("debug", "footprints", _footprints);
        cfg.SetValue("debug", "render_mode", (int)_renderMode);
        cfg.SetValue("debug", "fps_cap", _fpsCap);
    }

    /// <summary>Defaults (GameSettings.ResetDefaults): everything off; Performance pre-ticked for when the overlay is on.</summary>
    public static void ResetDefaults()
    {
        _overlay = false;
        _performance = true;
        _rendering = _memory = _simulation = _cursor = _selection = false;
        _hexGrid = _paths = _footprints = false;
        _renderMode = DebugRenderMode.Off;
        _fpsCap = 0;
    }
}
