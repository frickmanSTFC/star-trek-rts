using System.Collections.Generic;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Render-only: a finished Drone Pad's animation (BuildingModels.Drone.cs), driven by its drones' states
/// (PlanetSurface.DronesOf / DroneOf), after UpdateExcavations. Per bay (Bay0..): the ring lights (PadLight{k}) are dim
/// white when empty, amber breathing while the docked drone charges, steady green once it is full, a fast blue blink
/// while one launches or lands there, dim cyan while its drone is out, slow red when it was lost; the charger arm
/// (Charger{k}) swings over a docked drone (its head "ChargeGlow" lit while charging) and away otherwise. The roll Door
/// opens for 3 s when a drone is loaded from stock (the docked count goes up) and while one lands. The Status board:
/// green all charged, amber charging, cyan a drone out, red one lost, flickering when the pad is broken. The Beacon
/// blinks every 1.5 s, twice as fast while a drone is out; the Dish turns to follow the nearest of the pad's flying
/// drones, else scans slowly. Another player's pad moves only while in sight; parts looked up once per model.
/// </summary>
public partial class PlanetView
{
    private static readonly Color PadEmpty = new(0.85f, 0.9f, 1f), PadAmber = new(1f, 0.62f, 0.15f), PadGreen = new(0.3f, 1f, 0.45f),
        PadBlue = new(0.35f, 0.65f, 1f), PadCyan = new(0.45f, 0.95f, 1f), PadRed = new(1f, 0.18f, 0.1f);

    private sealed class PadVisual
    {
        public required Node3D Model;
        public int Facing;
        public Node3D?[] Bays = System.Array.Empty<Node3D?>();
        public Node3D?[] Chargers = System.Array.Empty<Node3D?>();
        public Basis[] ChargerRest = System.Array.Empty<Basis>();
        public float[] Swing = System.Array.Empty<float>();
        public List<(BaseMaterial3D Material, float Energy)>[] Lights = System.Array.Empty<List<(BaseMaterial3D, float)>>();
        public List<(BaseMaterial3D Material, float Energy)>[] Heads = System.Array.Empty<List<(BaseMaterial3D, float)>>();
        public readonly List<(BaseMaterial3D Material, float Energy)> Status = new(), Beacon = new();
        public Node3D? Door, Dish;
        public Basis DishRest;
        public float Open, OpenUntil = -1f, DishYaw;
        public int Docked = -1;
        // per frame, per bay: what is there
        public DroneState[] State = System.Array.Empty<DroneState>();
        public float[] Battery = System.Array.Empty<float>();
    }

    private readonly Dictionary<int, PadVisual> _pads = new();
    private readonly HashSet<int> _padsAlive = new();
    private readonly List<int> _padsGone = new();

    private static bool IsDronePad(SurfaceStructure s) => s.DefId == "drone_pad";

    private void UpdateDronePads(float delta)
    {
        _padsAlive.Clear();
        foreach (var s in _surface.Structures)
        {
            if (!IsDronePad(s) || s.State != StructureState.Complete || !_structures.TryGetValue(s.Id, out var sv)) continue;
            _padsAlive.Add(s.Id);
            var bays = Mathf.Max(2, _surface.DroneBays(s));
            if (!_pads.TryGetValue(s.Id, out var p) || p.Model != sv.Model || p.Bays.Length != bays) _pads[s.Id] = p = PadParts(sv.Model, bays);
            p.Facing = s.Facing;
            if (!ShowsLive(s.Owner, s.Tile)) continue; // a rival's pad keeps its last look until it's in sight
            AnimatePad(s, p, delta);
        }
        if (_pads.Count == _padsAlive.Count) return;
        _padsGone.Clear();
        foreach (var id in _pads.Keys)
            if (!_padsAlive.Contains(id)) _padsGone.Add(id);
        foreach (var id in _padsGone) _pads.Remove(id);
    }

    private static PadVisual PadParts(Node3D model, int bays)
    {
        var p = new PadVisual
        {
            Model = model,
            Bays = new Node3D?[bays],
            Chargers = new Node3D?[bays],
            ChargerRest = new Basis[bays],
            Swing = new float[bays],
            Lights = new List<(BaseMaterial3D, float)>[bays],
            Heads = new List<(BaseMaterial3D, float)>[bays],
            State = new DroneState[bays],
            Battery = new float[bays],
            Door = Part(model, "Door"),
            Dish = Part(model, "Dish"),
        };
        if (p.Dish != null) p.DishRest = p.Dish.Basis;
        for (var k = 0; k < bays; k++)
        {
            p.Bays[k] = Part(model, $"Bay{k}");
            p.Chargers[k] = Part(model, $"Charger{k}");
            if (p.Chargers[k] is { } c) p.ChargerRest[k] = c.Basis;
            p.Lights[k] = new List<(BaseMaterial3D, float)>();
            p.Heads[k] = new List<(BaseMaterial3D, float)>();
            Lamps(model, $"PadLight{k}", p.Lights[k]);
            if (p.Chargers[k] is { } charger)
                foreach (var m in MaterialsOf(charger))
                    if (m.ResourceName == "ChargeGlow")
                    {
                        m.EmissionEnabled = true;
                        p.Heads[k].Add((m, 2.5f));
                    }
        }
        foreach (var m in MaterialsOf(model))
        {
            if (m.ResourceName == "Status" && !p.Status.Exists(x => x.Material == m)) p.Status.Add((m, m.EmissionEnergyMultiplier > 0.05f ? m.EmissionEnergyMultiplier : 3f));
        }
        Lamps(model, "Beacon", p.Beacon);
        return p;
    }

    private void AnimatePad(SurfaceStructure s, PadVisual p, float delta)
    {
        for (var k = 0; k < p.State.Length; k++)
        {
            p.State[k] = DroneState.None;
            p.Battery[k] = 0f;
        }
        var docked = 0;
        var anyOut = false;
        var anyLost = false;
        var landing = false;
        Vector3? nearestFlying = null;
        var nearestDist = float.MaxValue;
        var drones = _surface.DronesOf(s.Id);
        for (var i = 0; i < drones.Count; i++)
        {
            var d = drones[i];
            var info = _surface.DroneOf(d);
            if (info.Bay < 0 || info.Bay >= p.State.Length) continue;
            p.State[info.Bay] = info.State;
            p.Battery[info.Bay] = Mathf.Clamp(info.Battery.ToFloat(), 0f, 1f);
            if (info.State == DroneState.Docked) docked++;
            if (info.State is DroneState.Flying or DroneState.Loitering or DroneState.Returning) anyOut = true;
            if (info.State == DroneState.Lost) anyLost = true;
            if (info.State == DroneState.Landing) landing = true;
            if (info.State is DroneState.Flying or DroneState.Loitering or DroneState.Returning or DroneState.Launching
                && _scoutDrones.TryGetValue(d.Id, out var dv) && dv.Placed)
            {
                var dist = dv.Pos.DistanceSquaredTo(p.Model.Position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestFlying = dv.Pos;
                }
            }
        }

        // the door: a drone loaded from stock, or one coming in to land
        if (p.Docked >= 0 && docked > p.Docked && !landing) p.OpenUntil = _time + 3f;
        p.Docked = docked;
        var open = landing || _time < p.OpenUntil ? 1f : 0f;
        p.Open = Mathf.MoveToward(p.Open, open, delta / 0.9f);
        if (p.Door != null) p.Door.Scale = new Vector3(1f, Mathf.Lerp(1f, 0.08f, Mathf.SmoothStep(0f, 1f, p.Open)), 1f);

        var charging = false;
        var broken = s.Broken;
        for (var k = 0; k < p.State.Length; k++)
        {
            var st = p.State[k];
            var full = p.Battery[k] >= 0.98f;
            if (st == DroneState.Docked && !full) charging = true;
            Color colour;
            float level;
            switch (st)
            {
                case DroneState.Docked when full:
                    colour = PadGreen;
                    level = 1f;
                    break;
                case DroneState.Docked:
                    colour = PadAmber;
                    level = 0.55f + 0.45f * Mathf.Sin(_time * 2.2f + k);
                    break;
                case DroneState.Launching or DroneState.Landing:
                    colour = PadBlue;
                    level = Mathf.PosMod(_time * 4f + k * 0.5f, 1f) < 0.5f ? 1.4f : 0.2f;
                    break;
                case DroneState.Flying or DroneState.Loitering or DroneState.Returning:
                    colour = PadCyan;
                    level = 0.4f;
                    break;
                case DroneState.Lost:
                    colour = PadRed;
                    level = Mathf.PosMod(_time * 0.8f, 1f) < 0.5f ? 1f : 0.15f;
                    break;
                default:
                    colour = PadEmpty;
                    level = 0.25f;
                    break;
            }
            if (broken) level *= Mathf.Sin(_time * 17f + k) > 0.3f ? 0.6f : 0.1f;
            foreach (var (m, e) in p.Lights[k])
            {
                m.AlbedoColor = colour;
                m.Emission = colour;
                m.EmissionEnergyMultiplier = e * level;
            }

            // charger arm: over the drone while docked, swung away otherwise
            p.Swing[k] = Mathf.MoveToward(p.Swing[k], st == DroneState.Docked ? 0f : 1f, delta / 1.2f);
            if (p.Chargers[k] is { } c)
            {
                var side = k % 2 == 0 ? -1f : 1f;
                c.Basis = p.ChargerRest[k] * new Basis(Vector3.Up, side * Mathf.DegToRad(75f) * Mathf.SmoothStep(0f, 1f, p.Swing[k]));
            }
            var head = st == DroneState.Docked && !full && !broken ? 0.6f + 0.4f * Mathf.Sin(_time * 6f + k) : 0.05f;
            foreach (var (m, e) in p.Heads[k]) m.EmissionEnergyMultiplier = e * head;
        }

        var status = anyLost ? PadRed : anyOut ? PadCyan : charging ? PadAmber : PadGreen;
        var statusLevel = broken ? (Mathf.PosMod(_time * 2f, 1f) < 0.5f ? 1f : 0.1f) : 1f;
        if (broken) status = PadRed;
        foreach (var (m, e) in p.Status)
        {
            m.AlbedoColor = status;
            m.Emission = status;
            m.EmissionEnergyMultiplier = e * statusLevel;
        }
        var period = anyOut ? 0.75f : 1.5f;
        var blink = Mathf.PosMod(_time + s.Id * 0.37f, period) < 0.3f * period ? 1f : 0.1f;
        foreach (var (m, e) in p.Beacon) m.EmissionEnergyMultiplier = e * blink;

        if (p.Dish != null)
        {
            float want;
            if (nearestFlying is { } at && p.Dish.GetParent() is Node3D parent && parent.IsInsideTree())
            {
                var local = parent.GlobalTransform.AffineInverse() * at - p.Dish.Position;
                want = Mathf.Atan2(-local.X, -local.Z); // the rest dish faces -Z
                p.DishYaw = Mathf.LerpAngle(p.DishYaw, want, Mathf.Min(1f, delta * 2f));
            }
            else p.DishYaw += delta * 0.25f;
            p.Dish.Basis = new Basis(Vector3.Up, p.DishYaw) * p.DishRest;
        }
    }
}
