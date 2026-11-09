using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Civilizations phase 2, surface view side (docs/civilizations-phase2-contract.md "View"; render-only): ruins on the
// colony's own world (PlanetSurface.Ruins / RuinAt) in the hex tooltip ("Ruins of the <species> · N layers, M dug") and
// on the minimap (Minimap.cs), the Excavation Site's building panel rows (layer progress DigProgress, DigProblem, the
// lock countdown, the last find), its finds list (PlanetHud.ShowFinds), the sick-crew chip and base-panel line
// (CrewSickSeconds), the "carrying a pathogen" row on vehicles (the sim's Carrier / CarrierSeconds, read by reflection
// since they land later), the orbit findings' ruins hint (the sim's RuinRegionHint, by reflection), the placement text
// for an Excavation Site off a ruin tile, and the event lines (a diff once a second: each new find, a defence system
// waking, a dig exhausted, a base's crew falling sick (or the sickness spreading to another base) and recovering).
public partial class PlanetDriver
{
    public const string ExcavationSite = "excavation_site";

    private double _digPollAt = -1;
    private bool _digBaseline;
    private int _findsSeen;
    private readonly Dictionary<int, int> _sickSeen = new();
    private readonly Dictionary<int, (bool Locked, bool Exhausted)> _ruinSeen = new();
    private readonly HashSet<int> _carriersSeen = new();

    private static bool IsExcavation(SurfaceStructure s) => s.DefId == ExcavationSite;

    private static string TheSpecies(string species) =>
        species.StartsWith("the ", System.StringComparison.OrdinalIgnoreCase) ? species : $"the {species}";

    private int SecondsUntil(long tick) => (int)System.Math.Max(0, (tick - _sim.Tick + Simulation.TickRate - 1) / Simulation.TickRate);

    private static string Clock2(int seconds) => $"{System.Math.Max(0, seconds) / 60}:{System.Math.Max(0, seconds) % 60:00}";

    /// <summary>A ruin the player has seen (its tile isn't unexplored fog).</summary>
    private bool RuinSeen(SurfaceRuin r) => _view.FogAt(r.Tile) != PlanetView.Fog.Unexplored;

    // ---------------------------------------------------------------- tooltip, placement

    /// <summary>"\nRuins of the X · 5 layers, 2 dug" (+ locked / exhausted) for a seen ruin tile, else "".</summary>
    private string RuinTooltip(int tile)
    {
        if (Surface.RuinAt(tile) is not { } r || !RuinSeen(r)) return "";
        var sb = new StringBuilder();
        sb.Append($"\nRuins of {TheSpecies(r.Species)} · {r.Layers} layer{(r.Layers == 1 ? "" : "s")}, {r.LayersDug} dug");
        if (r.Exhausted) sb.Append(" · exhausted");
        else if (r.LockedUntilTick > _sim.Tick) sb.Append($" · locked by a defence system {Clock2(SecondsUntil(r.LockedUntilTick))}");
        if (Surface.StructureAt(tile) is not { } s || !IsExcavation(s)) sb.Append("\nAn Excavation Site on this hex digs it layer by layer (finds: research, resources, dangers)");
        return sb.ToString();
    }

    /// <summary>The text for a placement problem the view has no case for yet: a ruin rule the sim adds (its own text if
    /// it exposes one, e.g. a static "…RuinProblem" string), else a plain "not available".</summary>
    private static string OtherReason(PlacementProblem p)
    {
        var name = p.ToString();
        if (!name.Contains("Ruin")) return "not available";
        foreach (var m in typeof(PlanetSurface).GetMembers(BindingFlags.Public | BindingFlags.Static))
        {
            if (!m.Name.Contains("Ruin") || !m.Name.Contains("Problem")) continue;
            var value = m switch
            {
                FieldInfo f when f.FieldType == typeof(string) => f.GetValue(null) as string,
                PropertyInfo pr when pr.PropertyType == typeof(string) && pr.GetIndexParameters().Length == 0 => pr.GetValue(null) as string,
                _ => null,
            };
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return "an Excavation Site must go on a ruin site";
    }

    // ---------------------------------------------------------------- building panel

    /// <summary>The Excavation Site's rows, status and dig progress; a crewed building's sick-crew note. Called from
    /// ShowStructure before its buttons.</summary>
    private void ExcavationPanel(SurfaceStructure s, bool complete, List<(string, string)> rows, ref string status, ref Color statusColour,
        ref float progress, ref string progressLabel, ref string progressText)
    {
        var sick = Surface.CrewSickSeconds(s.BaseId);
        if (sick > 0 && _data.Building(s.DefId).Crewed && complete) status += $"  ·  crew sick ({Clock2(sick)})";
        if (!IsExcavation(s)) return;
        var ruin = Surface.RuinAt(s.Tile);
        var at = 0;
        if (ruin == null)
        {
            rows.Insert(at, ("Site", "no ruins under it"));
            return;
        }
        rows.Insert(at++, ("Site", ruin.Exhausted ? $"Ruins of {TheSpecies(ruin.Species)} · all {ruin.Layers} layers dug"
            : $"Ruins of {TheSpecies(ruin.Species)} · layer {ruin.LayersDug + 1} of {ruin.Layers}"));
        var finds = Surface.Finds.Where(f => f.Tile == s.Tile && (f.Player == LocalPlayer || f.Player < 0)).ToList();
        rows.Insert(at++, ("Finds", finds.Count == 0 ? "nothing yet (each layer turns something up)" : $"{finds.Count} so far · last: {finds[^1].Name} ({finds[^1].Effect})"));
        var locked = ruin.LockedUntilTick > _sim.Tick;
        if (locked) rows.Insert(at++, ("Lock", $"defence system: dig locked {Clock2(SecondsUntil(ruin.LockedUntilTick))}"));
        if (sick > 0) rows.Insert(at, ("Crew", $"sick for {Clock2(sick)}: digs and works slower"));
        if (!complete) return;
        var problem = Surface.DigProblem(s);
        if (problem != null)
        {
            status = $"Dig stopped: {problem}";
            statusColour = locked ? new Color(1f, 0.42f, 0.36f) : new Color(1f, 0.7f, 0.42f);
        }
        else if (!ruin.Exhausted && s.Status is StructureStatus.Working or StructureStatus.LowPower) status = $"Digging layer {ruin.LayersDug + 1} of {ruin.Layers}";
        if (progress >= 0f || ruin.Exhausted) return;
        progress = Mathf.Clamp(Surface.DigProgress(s).ToFloat(), 0f, 1f);
        progressLabel = locked ? "Locked" : $"Layer {ruin.LayersDug + 1} of {ruin.Layers}";
        progressText = locked ? $"defence system: {Clock2(SecondsUntil(ruin.LockedUntilTick))} left" : $"{Mathf.RoundToInt(progress * 100)}%  ·  each layer rolls a find";
    }

    private static Color FindColour(string kind) => kind switch
    {
        "artifact" => HelmScreen.Lavender,
        "data_core" => HelmScreen.Cyan,
        "tech_fragment" => HelmScreen.Green,
        "resources" => PlanetHud.Accent,
        "defence" => HelmScreen.Red,
        "pathogen" => new Color(0.75f, 0.9f, 0.35f),
        _ => HelmScreen.Peach,
    };

    private string FindTime(long tick)
    {
        var s = tick / Simulation.TickRate;
        return $"{s / 60:00}:{s % 60:00}";
    }

    // ---------------------------------------------------------------- per frame (surface HUD)

    /// <summary>Every frame on the surface (UpdateHud, after ShowTools): the finds list, the sick-crew chip, the events.</summary>
    private void UpdateExcavation()
    {
        var selected = _selectedStructure != 0 ? Surface.FindStructure(_selectedStructure) : null;
        if (!_inOrbit && selected != null && selected.Owner == LocalPlayer && IsExcavation(selected) && Surface.RuinAt(selected.Tile) is { } ruin)
        {
            var rows = Surface.Finds.Where(f => f.Tile == selected.Tile && (f.Player == LocalPlayer || f.Player < 0)).Reverse().Take(6)
                .Select(f => new FindRow(f.Kind, f.Name, f.Effect, FindTime(f.Tick), FindColour(f.Kind))).ToList();
            _hud.ShowFinds($"Finds · ruins of {TheSpecies(ruin.Species)} · {ruin.LayersDug} of {ruin.Layers} layers", rows);
        }
        else _hud.ShowFinds(null, null);

        var sick = _selectedBase != 0 ? Surface.CrewSickSeconds(_selectedBase) : 0;
        _hud.ShowSickCrew(sick > 0 && Me.Phase == LandingPhase.Landed ? $"CREW SICK {Clock2(sick)}" : null,
            $"A pathogen from the dig: {BaseName(_selectedBase)}'s crew work slower (repairs, construction, crewed buildings) for {Clock2(sick)}.\n" +
            "Vehicles from a sick base can carry it to another. Xenomedicine research shortens it.");

        if (_digPollAt >= 0 && _clock - _digPollAt < 1.0) return;
        _digPollAt = _clock;
        ExcavationEvents();
    }

    /// <summary>"Crew sick: 4:12 left · …" for the base panel, or "".</summary>
    private string SickLine(int baseId)
    {
        var sick = Surface.CrewSickSeconds(baseId);
        return sick > 0 ? $"Crew sick for {Clock2(sick)}: repairs, construction and crewed buildings work slower\n" : "";
    }

    /// <summary>Once a second: new finds, defence wake, exhausted digs, sickness falling and lifting, carriers.</summary>
    private void ExcavationEvents()
    {
        var lines = new List<(string, Color)>();
        var finds = Surface.Finds;
        if (_findsSeen > finds.Count) _findsSeen = 0;
        var infectedByFind = new HashSet<int>();
        var defenceAt = new HashSet<int>();
        for (var i = _findsSeen; i < finds.Count; i++)
        {
            var f = finds[i];
            if (!_digBaseline || (f.Player >= 0 && f.Player != LocalPlayer)) continue;
            var ruin = Surface.RuinAt(f.Tile);
            var where = ruin != null ? $"the ruins of {TheSpecies(ruin.Species)}" : "the dig";
            var site = Surface.StructureAt(f.Tile);
            switch (f.Kind)
            {
                case "defence":
                    defenceAt.Add(f.Tile);
                    lines.Add(($"Defence system woke at {where}: {f.Name} · {f.Effect}", HelmScreen.Red));
                    break;
                case "pathogen":
                    if (site != null) infectedByFind.Add(site.BaseId);
                    lines.Add(($"Pathogen released at {where}: {f.Name} · {f.Effect}", FindColour(f.Kind)));
                    break;
                default:
                    lines.Add(($"Dig at {where}: {f.Name} · {f.Effect}", FindColour(f.Kind)));
                    break;
            }
        }
        _findsSeen = finds.Count;

        foreach (var r in Surface.Ruins)
        {
            var now = (Locked: r.LockedUntilTick > _sim.Tick, r.Exhausted);
            var had = _ruinSeen.TryGetValue(r.Tile, out var was);
            _ruinSeen[r.Tile] = now;
            if (!_digBaseline || !had || !RuinSeen(r)) continue;
            if (now.Locked && !was.Locked && !defenceAt.Contains(r.Tile))
                lines.Add(($"Ruins of {TheSpecies(r.Species)}: a defence system locked the dig for {Clock2(SecondsUntil(r.LockedUntilTick))}", HelmScreen.Red));
            else if (!now.Locked && was.Locked && !now.Exhausted) lines.Add(($"Ruins of {TheSpecies(r.Species)}: the defence system is quiet again, digging resumes", HelmScreen.Green));
            if (now.Exhausted && !was.Exhausted) lines.Add(($"Ruins of {TheSpecies(r.Species)}: all {r.Layers} layers dug, the site is exhausted", HelmScreen.Lavender));
        }

        var sickBefore = _sickSeen.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToHashSet();
        foreach (var b in Surface.Bases(LocalPlayer))
        {
            var sick = Surface.CrewSickSeconds(b.Id);
            var had = _sickSeen.TryGetValue(b.Id, out var before);
            _sickSeen[b.Id] = sick;
            if (!_digBaseline || !had) continue;
            if (sick > 0 && before <= 0)
                lines.Add((infectedByFind.Contains(b.Id) || sickBefore.Count == 0
                    ? $"{BaseName(b.Id)}'s crew are sick: slower work for {Clock2(sick)}"
                    : $"The pathogen spread to {BaseName(b.Id)} (a vehicle carried it): its crew are sick for {Clock2(sick)}", new Color(0.75f, 0.9f, 0.35f)));
            else if (sick <= 0 && before > 0) lines.Add(($"{BaseName(b.Id)}'s crew have recovered", HelmScreen.Green));
        }

        foreach (var u in Surface.Units)
        {
            if (u.Owner != LocalPlayer) continue;
            var carrier = CarrierSecondsOf(Surface, u) != 0;
            var had = _carriersSeen.Contains(u.Id);
            if (carrier) _carriersSeen.Add(u.Id);
            else _carriersSeen.Remove(u.Id);
            if (_digBaseline && carrier && !had) lines.Add(($"{UnitName(u)} picked up the pathogen: it can carry it to another base", new Color(0.75f, 0.9f, 0.35f)));
        }
        _hud.Minimap.Carriers = _carriersSeen;

        _digBaseline = true;
        foreach (var (text, colour) in lines) ColonyReport(text, colour);
    }

    // ---------------------------------------------------------------- pathogen carriers (sim API by reflection)

    private static readonly MethodInfo? CarrierMethod = typeof(PlanetSurface).GetMethod("Carrier", new[] { typeof(SurfaceUnit) });
    private static readonly MethodInfo? CarrierSecondsMethod = typeof(PlanetSurface).GetMethod("CarrierSeconds", new[] { typeof(SurfaceUnit) });

    /// <summary>Seconds a vehicle carries a pathogen for (the sim's CarrierSeconds), -1 = a carrier for an unknown time
    /// (only Carrier exists), 0 = not a carrier (or the sim doesn't have the API yet).</summary>
    public static int CarrierSecondsOf(PlanetSurface surface, SurfaceUnit u)
    {
        try
        {
            if (CarrierSecondsMethod != null && Seconds(CarrierSecondsMethod.Invoke(surface, new object[] { u })) is var n && n > 0) return n;
            if (CarrierMethod == null) return 0;
            return CarrierMethod.Invoke(surface, new object[] { u }) switch
            {
                null => 0,
                bool b => b ? -1 : 0,
                int i => i > 0 ? -1 : 0,
                long l => l > 0 ? -1 : 0,
                _ => -1,
            };
        }
        catch (System.Exception)
        {
            return 0;
        }
    }

    private static int Seconds(object? v) => v switch
    {
        int i => i,
        long l => (int)l,
        Fix64 f => Mathf.CeilToInt(f.ToFloat()),
        float f => Mathf.CeilToInt(f),
        double d => Mathf.CeilToInt((float)d),
        _ => 0,
    };

    /// <summary>"carrying a pathogen · 3:10" for a carrier, else null.</summary>
    private string? CarrierText(SurfaceUnit u)
    {
        var n = CarrierSecondsOf(Surface, u);
        return n == 0 ? null : n > 0 ? $"carrying a pathogen · {Clock2(n)} · keep it away from other bases" : "carrying a pathogen · keep it away from other bases";
    }

    // ---------------------------------------------------------------- ruins seen from orbit (sim API by reflection)

    private static readonly MemberInfo? RuinHintMember =
        typeof(PlanetSurface).GetMembers(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name.Contains("RuinRegion") && m is MethodInfo or PropertyInfo)
        ?? typeof(SurfaceMap).GetMembers(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name.Contains("RuinRegion") && m is MethodInfo or PropertyInfo);

    private List<int>? _ruinHints;
    private double _ruinHintsAt = -1;

    /// <summary>RuinHintRegions, worked out at most once a second.</summary>
    private List<int> RuinHints()
    {
        if (_ruinHints != null && _clock - _ruinHintsAt < 1.0) return _ruinHints;
        _ruinHintsAt = _clock;
        return _ruinHints = RuinHintRegions();
    }

    /// <summary>The regions the orbital scan flags for artificial structures (ruins), from whatever shape the sim's
    /// RuinRegionHint has: a region index (−1 none), a bool, a list of regions, or a per-region test (int → bool / string).
    /// Region −1 in the list = "somewhere on the map".</summary>
    private List<int> RuinHintRegions()
    {
        var list = new List<int>();
        if (RuinHintMember == null) return list;
        object target = RuinHintMember.DeclaringType == typeof(SurfaceMap) ? Surface.Map : Surface;
        try
        {
            object? value = null;
            switch (RuinHintMember)
            {
                case PropertyInfo p when p.GetIndexParameters().Length == 0:
                    value = p.GetValue(target);
                    break;
                case MethodInfo m when m.GetParameters().Length == 0:
                    value = m.Invoke(target, null);
                    break;
                case MethodInfo m when m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(int):
                    var player = m.GetParameters()[0].Name?.Contains("player") == true;
                    if (player)
                    {
                        value = m.Invoke(target, new object[] { LocalPlayer });
                        break;
                    }
                    for (var r = 0; r < Surface.Map.RegionCount; r++)
                        if (m.Invoke(target, new object[] { r }) is { } v && v is not false && !(v is string s && s.Length == 0) && !(v is int i && i < 0)) list.Add(r);
                    return list;
            }
            switch (value)
            {
                case null: break;
                case bool b: if (b) list.Add(-1); break;
                case int i: if (i >= 0) list.Add(i); break;
                case string s: if (s.Length > 0) list.Add(-1); break;
                case IEnumerable e:
                    foreach (var x in e)
                        if (x is int i2 && i2 >= 0) list.Add(i2);
                    break;
            }
        }
        catch (System.Exception)
        {
        }
        return list;
    }

    /// <summary>The orbit findings' line for the ruins hint, or null.</summary>
    private string? RuinFinding()
    {
        var regions = RuinHints();
        if (regions.Count == 0) return null;
        var places = regions.Where(r => r >= 0).Select(RegionPlace).Distinct().ToList();
        return places.Count == 0 ? "Artificial structures detected: possible ruins of a dead civilization"
            : $"Artificial structures detected · {string.Join(", ", places)}: possible ruins of a dead civilization";
    }

    /// <summary>The orbit tile readout's line when the tile's region is flagged, else "".</summary>
    private string RuinRegionText(int tile)
    {
        var regions = RuinHints();
        return regions.Contains(Surface.Map.Region(tile)) ? "\nArtificial structures in this region: possible ruins" : "";
    }
}
