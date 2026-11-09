using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Game sounds on the surface (docs/sound-contract.md "Hooks"): the selection hum. A selection the player makes (a click
// on a vehicle, building or core, a base tab / Tab, A for all vehicles) arms it; once a frame the selection's signature
// is compared with the last one, and an armed change plays the hum of what is selected: one building = its id (a colony
// core = colony_hub), one kind of vehicle = that unit id, a base = colony_hub; a mix of vehicle kinds plays nothing.
// Selections the game makes itself (the landing's vehicles, a unit that died) never hum. The other surface sounds are
// one-liners where they happen (place / cancel in PlanetDriver.cs, refusals in PlanetHud.PushEvent, landing sounds in
// DropShipView / LandingView).
public partial class PlanetDriver
{
    private bool _selectionSoundArmed;
    private long _selectionSignature;
    private long _selectionHummed;

    /// <summary>The player just changed the selection on purpose: the next change of it may hum.</summary>
    private void ArmSelectionSound() => _selectionSoundArmed = true;

    /// <summary>Once a frame from _Process (after input): hum when an armed selection change happened.</summary>
    private void UpdateSelectionSound()
    {
        var armed = _selectionSoundArmed;
        _selectionSoundArmed = false;
        var signature = SelectionSignature();
        if (signature != _selectionSignature)
        {
            _selectionSignature = signature;
            if (!armed) _selectionHummed = 0; // changed by the game (Esc, a unit gone, …): the next pick hums again
        }
        if (!armed || signature == _selectionHummed || _inOrbit || Me.Phase != LandingPhase.Landed) return;
        _selectionHummed = signature;
        PlaySelectionHum();
    }

    /// <summary>A hash of what is selected (no allocation: the HashSet enumerator is a struct).</summary>
    private long SelectionSignature()
    {
        long h = 1469598103934665603;
        var n = 0;
        long sum = 0, mix = 0;
        foreach (var id in _view.SelectedUnits)
        {
            n++;
            sum += id;
            mix ^= id * unchecked((long)0x9E3779B97F4A7C15UL);
        }
        h = (h ^ n) * 1099511628211;
        h = (h ^ sum) * 1099511628211;
        h = (h ^ mix) * 1099511628211;
        h = (h ^ _selectedStructure) * 1099511628211;
        h = (h ^ (_selectedTile + 3)) * 1099511628211;
        // The base counts only when it is all that's selected (its panel shows).
        if (n == 0 && _selectedStructure == 0 && _selectedTile < 0) h = (h ^ (_selectedBase + 7919L)) * 1099511628211;
        return h == 0 ? 1 : h;
    }

    private void PlaySelectionHum()
    {
        if (_selectedStructure != 0)
        {
            if (Surface.FindStructure(_selectedStructure) is { } s)
                Sfx.Select(_data.Building(s.DefId).IsCore ? "colony_hub" : s.DefId, vehicle: false);
            return;
        }
        if (_view.SelectedUnits.Count > 0)
        {
            string? kind = null;
            foreach (var id in _view.SelectedUnits)
            {
                if (Surface.FindUnit(id) is not { } u) continue;
                if (kind == null) kind = u.DefId;
                else if (kind != u.DefId) return; // several kinds: no single hum
            }
            if (kind != null) Sfx.Select(kind, vehicle: true);
            return;
        }
        if (_selectedTile < 0 && _selectedBase != 0 && Surface.FindStructure(_selectedBase) != null)
            Sfx.Select("colony_hub", vehicle: false);
    }
}
