using System.Collections.Generic;
using System.Linq;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

// The campaign's ship pick (docs/ship-pick-contract.md, owner 2026-11-16: "the player will decide which ship they are
// going to use ... if they pick a ship that is meant to colonize the planet, they get the colonize mission; any other ship
// gets a different mission and won't go to a planet or body"). CONTRACT STUB: the sim agent replaces ShipChoices with the
// parsed data/missions/ship_choice.json (same record, same order) and adds checks; the menu and the driver code against this.

/// <summary>One pickable ship: Id "tereshkova"; Name "Tereshkova"; ClassLabel "Long-range survey vessel"; Role
/// "survey" | "courier" | "patrol" | "diplomacy" | "colony"; Colonizes (lands and plays the colony chain); Chain (the mission
/// chain id it turns on); Crew text; LengthM; Speed (travel-time divisor, 1 = the system's normal speed); Card (res:// path of
/// the card art); Blurb; MissionTitle / MissionSummary (the pick screen's "Your mission" lines); StartBonus (resource → amount
/// added to the landing stock, colonizers only); StartUnits (unit ids that land with the colony, colonizers only).</summary>
public sealed record ShipChoiceDef(string Id, string Name, string ClassLabel, string Role, bool Colonizes, string Chain,
    string Crew, int LengthM, Fix64 Speed, string Card, string Blurb, string MissionTitle, string MissionSummary,
    IReadOnlyDictionary<string, int> StartBonus, IReadOnlyList<string> StartUnits);

public sealed partial class GameData
{
    /// <summary>The ships the campaign start offers, in pick-screen order (empty when the data set has none).</summary>
    public IReadOnlyList<ShipChoiceDef> ShipChoices => _shipChoices ??= StubShipChoices();
    private IReadOnlyList<ShipChoiceDef>? _shipChoices;

    /// <summary>The ship with this id, or null.</summary>
    public ShipChoiceDef? ShipChoice(string? id) => id == null ? null : ShipChoices.FirstOrDefault(s => s.Id == id);

    private static IReadOnlyList<ShipChoiceDef> StubShipChoices()
    {
        static ShipChoiceDef S(string id, string name, string cls, string role, bool col, string chain, string crew, int len, int speedPct, string title, string summary, string blurb) =>
            new(id, name, cls, role, col, chain, crew, len, Fix64.FromInt(speedPct) / Fix64.FromInt(100), $"res://textures/ships/{id}.jpg", blurb, title, summary,
                new Dictionary<string, int>(), new List<string>());
        return new List<ShipChoiceDef>
        {
            S("horizon", "Horizon", "Colony transport", "colony", true, "first_light", "120 crew, 4,000 colonists in cryo", 820, 80, "First Light", "Land the colony and build the first outpost.", "Six drop ships land the colony."),
            S("cochrane", "Cochrane", "Deep-space tender", "colony", true, "first_light", "210 crew", 480, 90, "First Light", "Land the colony and build the first outpost.", "Four drop ships land the colony."),
            S("tereshkova", "Tereshkova", "Long-range survey vessel", "survey", false, "long_survey", "40 crew", 310, 110, "The Long Survey", "Chart the system and chase a faint signal to its edge.", "Never lands."),
            S("armstrong", "Armstrong", "Fast courier", "courier", false, "priority_run", "6 crew", 140, 200, "Priority Run", "Timed runs between bodies.", "Never lands."),
            S("bastion", "Bastion", "System patrol cutter", "patrol", false, "watch_station", "85 crew", 260, 120, "Watch Station", "Patrol, inspect, rescue, escort.", "Never lands."),
            S("meridian", "Meridian", "Diplomatic and science flagship", "diplomacy", false, "envoy", "350 crew", 560, 90, "Envoy", "Find the system's civilization and make first contact.", "Never lands."),
        };
    }
}
