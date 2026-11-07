using FinalFrontier.Sim.Core;

namespace FinalFrontier.Sim.Surface;

// Fog of war, per player, in the sim (it used to live only in PlanetView, so the view could draw rival bases the
// player had never seen, e.g. from orbit).
//
// In sight now (IsVisible) is computed from the state, never stored: within StructureSight tiles of any of the
// player's structures (any state, as before), within SightOf(unit) = its survey radius now (damage, weather) +
// UnitSightBonus of any of their vehicles, and DescentSight round the landing tile while the colony pod falls.
// A player in orbit has nothing on the ground, so sees nothing (the orbital scan's terrain is a view matter).
//
// Memory is state (SurfacePlayer.Seen, SeenStructures; both hashed), updated at the end of a step by UpdateSight:
// once a second (tick % TickRate == 0) every tile in sight is marked seen and every other player's structure standing
// on one is remembered from then on; in between, a vehicle that drives into a new tile marks its sight there at once
// (Lanes.cs, with the survey) and the falling pod's site is marked every tick. Once a second because a vehicle's sight
// reads the weather on its tile (Climate.Effects), which is too dear for every vehicle on every tick (it slowed the
// balance runner by a fifth); weather and new buildings change sight slowly, so memory trails sight by under a second.
// Explored (IsExplored) = seen at some point, or surveyed (rovers, landing, satellites, the test panel's survey all).
// Nothing here changes any other rule: AI, placement and landing checks don't read it.
public sealed partial class PlanetSurface
{
    /// <summary>How far a player's buildings see, in tiles (any state: a queued building's site counts, as in the old view fog).</summary>
    public const int StructureSight = 3;

    /// <summary>A vehicle sees this many tiles beyond its survey radius now.</summary>
    public const int UnitSightBonus = 1;

    /// <summary>How far round its landing tile a player sees while the colony pod is coming down.</summary>
    public const int DescentSight = 2;

    /// <summary>How far a vehicle sees now: <see cref="SurveyRadiusOf"/> (damaged sensors, weather) + <see cref="UnitSightBonus"/>.</summary>
    public int SightOf(SurfaceUnit u) => SurveyRadiusOf(u) + UnitSightBonus;

    /// <summary>True if the tile is in sight of one of the player's buildings or vehicles now.</summary>
    public bool IsVisible(int player, int tile)
    {
        if (!IsPlayer(player) || !OnMap(tile)) return false;
        var grid = Map.Grid;
        var p = _players[player];
        if (p.Phase == LandingPhase.Descending && p.LandingTile >= 0 && grid.Distance(p.LandingTile, tile) <= DescentSight) return true;
        foreach (var s in _structures)
            if (s.Owner == player && grid.Distance(s.Tile, tile) <= StructureSight) return true;
        foreach (var u in _units)
            if (u.Owner == player && grid.Distance(u.Tile, tile) <= SightOf(u)) return true;
        return false;
    }

    /// <summary>True if the player has had the tile in sight at some point, or surveyed it.</summary>
    public bool IsExplored(int player, int tile) =>
        IsPlayer(player) && OnMap(tile) && (_players[player].Seen[tile] || _players[player].Surveyed[tile]);

    /// <summary>Every tile the player has in sight now (a new array, one entry per tile; all false for a bad player).</summary>
    public bool[] VisibleTiles(int player)
    {
        var visible = new bool[Map.Grid.Count];
        MarkVisible(player, visible);
        return visible;
    }

    /// <summary>The player sees this structure now: their own, or on a tile in sight.</summary>
    public bool CanSee(int player, SurfaceStructure s) => s.Owner == player || IsVisible(player, s.Tile);

    /// <summary>The player sees this vehicle now: their own, or on a tile in sight.</summary>
    public bool CanSee(int player, SurfaceUnit u) => u.Owner == player || IsVisible(player, u.Tile);

    /// <summary>The player has seen this other player's structure at some point (it stays on their map from then on).</summary>
    public bool Remembers(int player, SurfaceStructure s) =>
        IsPlayer(player) && _players[player]._seenStructures.BinarySearch(s.Id) >= 0;

    /// <summary>The player knows this structure is there: their own, in sight now, or remembered.</summary>
    public bool Knows(int player, SurfaceStructure s) => s.Owner == player || Remembers(player, s) || IsVisible(player, s.Tile);

    /// <summary>Sets every tile the player has in sight now to true in <paramref name="visible"/> (and in
    /// <paramref name="also"/> if given); leaves the others as they are.</summary>
    private void MarkVisible(int player, bool[] visible, bool[]? also = null)
    {
        if (!IsPlayer(player)) return;
        var p = _players[player];
        if (p.Phase == LandingPhase.Descending && p.LandingTile >= 0) MarkWithin(visible, also, p.LandingTile, DescentSight);
        foreach (var s in _structures)
            if (s.Owner == player) MarkWithin(visible, also, s.Tile, StructureSight);
        foreach (var u in _units)
            if (u.Owner == player) MarkWithin(visible, also, u.Tile, SightOf(u));
    }

    /// <summary><see cref="HexGrid.Within"/> without the list: marks every tile within radius of centre.</summary>
    private void MarkWithin(bool[] marks, bool[]? also, int centre, int radius)
    {
        if (radius < 0 || !OnMap(centre)) return;
        var g = Map.Grid;
        var r0 = g.Row(centre);
        var c0 = g.Column(centre);
        var rowEnd = System.Math.Min(g.Height - 1, r0 + radius);
        var colEnd = System.Math.Min(g.Width - 1, c0 + radius + 1);
        for (var r = System.Math.Max(0, r0 - radius); r <= rowEnd; r++)
            for (var c = System.Math.Max(0, c0 - radius - 1); c <= colEnd; c++)
            {
                var i = g.Index(c, r);
                if (marks[i] || g.Distance(centre, i) > radius) continue;
                marks[i] = true;
                if (also != null) also[i] = true;
            }
    }

    /// <summary>Scratch for <see cref="UpdateSight"/> (cleared before each use; never read outside it, not state).</summary>
    private bool[] _sightScratch = System.Array.Empty<bool>();

    /// <summary>A vehicle that just drove into a new tile marks what it sees there (radius = its survey radius now + the bonus).</summary>
    private void MarkSightAt(SurfaceUnit u, int surveyRadius) =>
        MarkWithin(_players[u.Owner].Seen, null, u.Tile, surveyRadius + UnitSightBonus);

    /// <summary>End of each step: the falling pods' sites every tick; once a second, everything every player has in sight
    /// now goes into their memory (seen tiles, other players' structures).</summary>
    private void UpdateSight(long tick)
    {
        for (var i = 0; i < _players.Count; i++)
        {
            var p = _players[i];
            if (p.Phase == LandingPhase.Descending && p.LandingTile >= 0) MarkWithin(p.Seen, null, p.LandingTile, DescentSight);
        }
        if (tick % Simulation.TickRate != 0) return;
        if (_sightScratch.Length != Map.Grid.Count) _sightScratch = new bool[Map.Grid.Count];
        for (var i = 0; i < _players.Count; i++)
        {
            var p = _players[i];
            System.Array.Clear(_sightScratch);
            MarkVisible(i, _sightScratch, p.Seen);
            foreach (var s in _structures)
            {
                if (s.Owner == i || !_sightScratch[s.Tile]) continue;
                var at = p._seenStructures.BinarySearch(s.Id);
                if (at < 0) p._seenStructures.Insert(~at, s.Id);
            }
        }
    }
}
