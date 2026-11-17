using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>Where one of the local player's vehicles is drawn right now (PlanetView.PingTargets).</summary>
public struct PingTarget
{
    public int Id;

    /// <summary>The model's position (a flying drone: in the air).</summary>
    public Vector3 At;

    /// <summary>The ground under it (the same height as At for anything on its wheels).</summary>
    public float GroundY;

    /// <summary>Height of the model's top above At.</summary>
    public float Top;
}

// The vehicle ping (owner 2026-11-17: "We need a way to find vehicles on the surface, there are too many rocks and things
// to find them. Maybe a "Ping" to show where they are."). The view's part is only this: where each of the local player's
// vehicles is drawn. The beacons, rings, names and edge arrows are drawn in 2D over the scene (PingOverlay.cs), so trees,
// rocks, buildings and the fog never hide them; the driver's part is PlanetDriver.Ping.cs.
public partial class PlanetView
{
    /// <summary>Fills <paramref name="into"/> with every vehicle of the local player that is on the map: rovers, crawlers
    /// (folded or deployed), construction rovers, survey teams and scout drones (docked, flying or coming down). Vehicles
    /// still riding in a drop ship's bay are left out. Never another player's.</summary>
    public void PingTargets(List<PingTarget> into)
    {
        into.Clear();
        foreach (var u in _surface.Units)
        {
            if (u.Owner != _localPlayer || _aboard.Contains(u.Id)) continue;
            Node3D? model;
            Vector3 at;
            float ground;
            if (_scoutDrones.TryGetValue(u.Id, out var drone))
            {
                if (!drone.Placed) continue;
                model = drone.Holder;
                at = drone.Pos;
                ground = Mathf.Min(at.Y, GroundAt(at));
            }
            else
            {
                if (!_rovers.TryGetValue(u.Id, out model)) continue;
                at = model.Position;
                ground = at.Y;
            }
            into.Add(new PingTarget { Id = u.Id, At = at, GroundY = ground, Top = MarkerHeight(model) });
        }
    }

    /// <summary>True while this vehicle rides in its drop ship's bay (the vehicle list says so).</summary>
    public bool RidingDropShip(int unitId) => _aboard.Contains(unitId);
}
