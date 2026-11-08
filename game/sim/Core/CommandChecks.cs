using System;
using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Core;

/// <summary>
/// The shape checks every command passes before any handler sees it (multiplayer safety: a peer can send anything).
/// Malformed = something no honest client makes: a missing list, string or list item, an enum value that isn't defined,
/// an absurdly long list or string. Whether the ids, tiles and amounts make sense in this game is up to each handler,
/// which refuses (changes nothing) instead of throwing. See <see cref="Simulation.RefusedCommands"/>.
/// </summary>
public static class CommandChecks
{
    /// <summary>Most ids one command may list (a selection of vehicles or ships).</summary>
    public const int MaxIds = 1024;

    /// <summary>Longest id string (building, unit, tech, faction, payload).</summary>
    public const int MaxIdLength = 128;

    /// <summary>Most edits or files one data command may carry.</summary>
    public const int MaxDataItems = 4096;

    /// <summary>Largest coordinate a star-system move order may target (keeps ship maths far from Fix64 overflow).</summary>
    public static readonly Fix64 MaxCoordinate = Fix64.FromInt(1_000_000);

    /// <summary>Why this command is malformed, or null if its shape is fine.</summary>
    public static string? Malformed(Command? command)
    {
        switch (command)
        {
            case null: return "no command";
            case MoveCommand c:
                if (Ids(c.ShipIds) is { } bad) return bad;
                return Fix64.Abs(c.Target.X) <= MaxCoordinate && Fix64.Abs(c.Target.Y) <= MaxCoordinate ? null : "target out of range";
            case MoveUnitsCommand c: return Ids(c.UnitIds);
            case PackUpCommand c: return Ids(c.UnitIds);
            case RepairCommand c: return Ids(c.UnitIds);
            case ChangeTyreCommand c: return Ids(c.UnitIds);
            case PlaceBuildingCommand c: return Id(c.BuildingId);
            case BuildUnitCommand c: return Id(c.UnitId);
            case ShuttleCommand c: return Id(c.UnitId);
            case BeamCommand c: return Id(c.Payload);
            case ResearchCommand c: return Id(c.TechId);
            case SetCourseCommand c: return Id(c.BodyId);
            case SystemSatelliteCommand c: return Id(c.BodyId);
            case DebugUnitCommand c: return Enum.IsDefined(c.Field) ? null : "unknown field";
            case DebugSpawnUnitCommand c: return Id(c.UnitId);
            case DebugStructureCommand c: return Enum.IsDefined(c.Field) ? null : "unknown field";
            case DebugPlaceBuildingCommand c: return Id(c.BuildingId);
            case DebugBaseCommand c: return Enum.IsDefined(c.Action) ? null : "unknown action";
            case DebugStockCommand c:
                if (c.Values == null) return "no values";
                return c.Values.Count <= MaxIds ? null : "too many values";
            case DebugFlagCommand c: return Enum.IsDefined(c.Flag) ? null : "unknown flag";
            case DebugTechCommand c: return Id(c.TechId);
            case DebugAddPlayerCommand c: return Id(c.FactionId);
            case DebugDataCommand c:
                if (c.Edits == null) return "no edits";
                if (c.Edits.Count > MaxDataItems) return "too many edits";
                foreach (var e in c.Edits)
                    if (e?.File == null || e.Path == null || e.Value == null) return "a missing edit";
                return null;
            case DebugReloadDataCommand c:
                if (c.Files == null) return "no files";
                if (c.Files.Count > MaxDataItems) return "too many files";
                foreach (var f in c.Files)
                    if (f?.File == null || f.Text == null) return "a missing file";
                return null;
            default:
                return null; // ints only (ids, tiles, rows): the handlers check them
        }
    }

    private static string? Ids(IReadOnlyList<int>? ids)
    {
        if (ids == null) return "no ids";
        return ids.Count <= MaxIds ? null : "too many ids";
    }

    private static string? Id(string? id)
    {
        if (id == null) return "no id";
        return id.Length <= MaxIdLength ? null : "id too long";
    }
}

/// <summary>
/// A stable hash contribution for each command (for the scheduled-command queue in <see cref="Simulation.StateHash"/>):
/// a fixed type tag and then every field in declaration order. Never record GetHashCode (strings hash differently in every
/// process). A new command type needs its own tag and case here (test CommandHashCoversEveryCommandType checks).
/// </summary>
public static class CommandHash
{
    /// <summary>The tag of a command type this switch doesn't know (it is then hashed by its type name).</summary>
    public const int UnknownTag = 0;

    /// <summary>The type tag of a command (UnknownTag for a type with no case below).</summary>
    public static int Tag(Command command) => command switch
    {
        MoveCommand => 1,
        LandColonyCommand => 2,
        MoveUnitsCommand => 3,
        PlaceBuildingCommand => 4,
        CancelBuildingCommand => 5,
        UpgradeCommand => 6,
        MineCommand => 7,
        PackUpCommand => 8,
        BuildUnitCommand => 9,
        CancelUnitCommand => 10,
        OutpostDropCommand => 11,
        ShuttleCommand => 12,
        BeamCommand => 13,
        SatelliteCommand => 14,
        ResearchCommand => 15,
        MaintainCommand => 16,
        RepairCommand => 17,
        ChangeTyreCommand => 18,
        SalvageCommand => 19,
        FoundOutpostCommand => 20,
        PauseCommand => 21,
        SetCourseCommand => 22,
        LongRangeScanCommand => 23,
        SystemSatelliteCommand => 24,
        // Test panel.
        DebugUnitCommand => 101,
        DebugMoveUnitCommand => 102,
        DebugRemoveUnitCommand => 103,
        DebugSpawnUnitCommand => 104,
        DebugStructureCommand => 105,
        DebugRemoveStructureCommand => 106,
        DebugPlaceBuildingCommand => 107,
        DebugBaseCommand => 108,
        DebugStockCommand => 109,
        DebugFlagCommand => 110,
        DebugTechCommand => 111,
        DebugSurveyAllCommand => 112,
        DebugDepositCommand => 113,
        DebugAddPlayerCommand => 114,
        DebugDataCommand => 115,
        DebugReloadDataCommand => 116,
        _ => UnknownTag,
    };

    internal static void Add(ref StateHasher h, Command command)
    {
        var tag = Tag(command);
        h.Add(tag);
        h.Add(command.Player);
        switch (command)
        {
            case MoveCommand c: Ids(ref h, c.ShipIds); h.Add(c.Target); break;
            case LandColonyCommand c: h.Add(c.Tile); break;
            case MoveUnitsCommand c: Ids(ref h, c.UnitIds); h.Add(c.Tile); break;
            case PlaceBuildingCommand c: Text(ref h, c.BuildingId); h.Add(c.Tile); h.Add(c.Facing); break;
            case CancelBuildingCommand c: h.Add(c.StructureId); break;
            case UpgradeCommand c: h.Add(c.StructureId); break;
            case MineCommand c: h.Add(c.UnitId); h.Add(c.Tile); break;
            case PackUpCommand c: Ids(ref h, c.UnitIds); break;
            case BuildUnitCommand c: h.Add(c.StructureId); Text(ref h, c.UnitId); break;
            case CancelUnitCommand c: h.Add(c.StructureId); break;
            case OutpostDropCommand c: h.Add(c.Tile); break;
            case ShuttleCommand c: h.Add(c.Tile); Text(ref h, c.UnitId); break;
            case BeamCommand c: h.Add(c.Tile); Text(ref h, c.Payload); break;
            case SatelliteCommand c: h.Add(c.Row); break;
            case ResearchCommand c: Text(ref h, c.TechId); break;
            case MaintainCommand c: h.Add(c.StructureId); break;
            case RepairCommand c: Ids(ref h, c.UnitIds); break;
            case ChangeTyreCommand c: Ids(ref h, c.UnitIds); break;
            case SalvageCommand c: h.Add(c.StructureId); break;
            case FoundOutpostCommand c: h.Add(c.UnitId); h.Add(c.Tile); break;
            case PauseCommand c: h.Add(c.StructureId); h.Add(c.Pause); break;
            case SetCourseCommand c: h.Add(c.ShipId); Text(ref h, c.BodyId); break;
            case LongRangeScanCommand c: h.Add(c.ShipId); break;
            case SystemSatelliteCommand c: Text(ref h, c.BodyId); break;
            case DebugUnitCommand c: h.Add(c.UnitId); h.Add((int)c.Field); h.Add(c.Value); break;
            case DebugMoveUnitCommand c: h.Add(c.UnitId); h.Add(c.Tile); break;
            case DebugRemoveUnitCommand c: h.Add(c.UnitId); break;
            case DebugSpawnUnitCommand c: h.Add(c.Owner); Text(ref h, c.UnitId); h.Add(c.Tile); break;
            case DebugStructureCommand c: h.Add(c.StructureId); h.Add((int)c.Field); h.Add(c.Value); break;
            case DebugRemoveStructureCommand c: h.Add(c.StructureId); break;
            case DebugPlaceBuildingCommand c: h.Add(c.Owner); Text(ref h, c.BuildingId); h.Add(c.Tile); h.Add(c.Facing); break;
            case DebugBaseCommand c: h.Add(c.BaseId); h.Add((int)c.Action); h.Add(c.Value); break;
            case DebugStockCommand c:
                h.Add(c.Target);
                h.Add(c.Values?.Count ?? -1);
                if (c.Values != null)
                    foreach (var v in c.Values)
                    {
                        h.Add(v.Resource);
                        h.Add(v.Amount);
                    }
                break;
            case DebugFlagCommand c: h.Add(c.Target); h.Add((int)c.Flag); h.Add(c.On); break;
            case DebugTechCommand c: h.Add(c.Target); Text(ref h, c.TechId); h.Add(c.Known); h.Add(c.Progress); break;
            case DebugSurveyAllCommand c: h.Add(c.Target); break;
            case DebugDepositCommand c: h.Add(c.Tile); h.Add(c.Amount); break;
            case DebugAddPlayerCommand c: Text(ref h, c.FactionId); break;
            case DebugDataCommand c:
                h.Add(c.Edits?.Count ?? -1);
                if (c.Edits != null)
                    foreach (var e in c.Edits)
                    {
                        h.Add(e != null);
                        if (e == null) continue;
                        Text(ref h, e.File);
                        Text(ref h, e.Path);
                        Text(ref h, e.Value);
                    }
                break;
            case DebugReloadDataCommand c:
                h.Add(c.Files?.Count ?? -1);
                if (c.Files != null)
                    foreach (var f in c.Files)
                    {
                        h.Add(f != null);
                        if (f == null) continue;
                        Text(ref h, f.File);
                        Text(ref h, f.Text);
                    }
                break;
            default:
                Text(ref h, command.GetType().FullName); // stable across processes, unlike GetHashCode
                break;
        }
    }

    private static void Ids(ref StateHasher h, IReadOnlyList<int>? ids)
    {
        h.Add(ids?.Count ?? -1);
        if (ids == null) return;
        foreach (var id in ids) h.Add(id);
    }

    /// <summary>A string, or a marker for a missing one (a malformed command can sit in the queue until it is refused).</summary>
    private static void Text(ref StateHasher h, string? s)
    {
        h.Add(s != null);
        if (s != null) h.Add(s);
    }
}
