using FinalFrontier.Sim.Core;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Surface;

// Pausing a building (owner's open item: "the Alloy Foundry eats duranium as soon as it's built"). Any building that
// does work can be switched off and on again: converters (refinery, foundry), mines, the Vehicle Bay, labs, reactors and
// anything else with an output, upkeep or power draw. A paused building is Complete but idle: status Paused, it uses no
// input, makes nothing, pays no upkeep, wears not, and neither uses nor supplies power (so pausing a reactor saves its
// deuterium, and pausing a foundry frees its power for the rest of the base). Colony cores can't be paused (they are the
// base) and plain storage has nothing to pause. SurfaceStructure.Paused is in the state hash.
public sealed partial class PlanetSurface
{
    /// <summary>Has something to switch off: works (mine, converter, bay, lab), makes or burns something, or draws or
    /// supplies power. Not a colony core, not a building that only stores.</summary>
    public bool CanPause(string defId)
    {
        var def = _data.Building(defId);
        if (def.IsCore) return false;
        return def.IsMine || def.IsConverter || def.IsFactory || def.IsLab || def.OutputPerMinute.Count > 0 ||
               def.UpkeepPerMinute.Count > 0 || def.PowerUse > Fix64.Zero || def.PowerSupply > Fix64.Zero || def.Fabricator != null;
    }

    /// <summary>Why this building can't be paused (pause = true) or resumed (false) now, or null. Unfinished buildings
    /// can be paused ahead (they start paused when they are done).</summary>
    public string? PauseProblem(int player, int structureId, bool pause)
    {
        var s = FindStructure(structureId);
        if (s == null || s.Owner != player) return "not yours";
        if (!CanPause(s.DefId)) return _data.Building(s.DefId).IsCore ? "a colony core can't be paused" : "nothing to pause";
        if (s.Paused == pause) return pause ? "already paused" : "not paused";
        return null;
    }

    /// <summary>Switches the building off or on; a command PauseProblem refuses changes nothing and is counted as refused.</summary>
    private bool ApplyPause(Simulation sim, PauseCommand command)
    {
        if (PauseProblem(command.Player, command.StructureId, command.Pause) is { } problem) return sim.Refuse(command, problem);
        FindStructure(command.StructureId)!.Paused = command.Pause; // Economy sets the status in the same step
        return true;
    }
}
