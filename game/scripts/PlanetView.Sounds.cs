using System.Collections.Generic;
using FinalFrontier.Sim.Data;
using FinalFrontier.Sim.Surface;
using Godot;

namespace FinalFrontier;

// Vehicle sounds (docs/sound-contract.md "Ambient sounds"; owner 2026-11-10: "when the vehicles are moving, a slight EV
// type sound, ground noise, etc. … If the wipers are going, add that too"). Render-only, all on the Effects bus.
//
// UpdateRovers calls NoteVehicleSound for every vehicle (after it is placed this frame) and UpdateVehicleSounds once at
// the end. A vehicle's speed is measured from how far its model really moved (so a paused sim, a wait at a bend or a
// parked vehicle is silent). The nearest DriveSlots moving vehicles to the camera (shown: ours, or another player's in
// sight; mobile, not deployed or riding in a drop ship) each get a slot: a drive loop (rovers vehicle/ev_loop, pitch
// 0.8–1.25 by speed and a little by mark; the crawler and the construction rover vehicle/crawler_loop) and the ground
// loop for the SprayKind under it (two players crossfading over GroundFadeSeconds when it changes), plus
// vehicle/flat_loop while it has a flat. Slots ease in and out over FadeSeconds as a vehicle starts, stops or drops out
// of the nearest; a slot only takes a new vehicle once it is silent. The players live under one node of their own
// (made once, never freed until PlanetView goes) and are moved to their vehicle every frame, so nothing is allocated
// per frame and nothing dangles when a vehicle goes. Wipers: each stroke the view animates (RoverLook.Lamps.StrokeBegan)
// plays vehicle/wiper at the cab from a pool of WiperPlayers for vehicles within WiperRange (vehicle/wiper_squeak when
// the wipers are forced on over dry glass). Silent in orbit (Heard) and while the game is paused (the players pause with
// PlanetView).
public partial class PlanetView
{
    /// <summary>Set by the driver every frame: false in orbit (feed or not), where the vehicles go quiet.</summary>
    public bool Heard { get; set; } = true;

    /// <summary>Rain at the view's centre 0..1 (the driver's eased weather): wipers on rained-on glass swish, not squeak.</summary>
    public float RainNow { get; set; }

    private const int DriveSlots = 6;           // vehicles heard driving at once: the nearest moving ones
    private const int WiperPlayers = 4;
    private const float HearRange = 700f;       // a vehicle further from the camera than this isn't a candidate
    private const float WiperRange = 260f;
    private const float FadeSeconds = 0.3f;
    private const float GroundFadeSeconds = 0.35f;
    private const float VehicleUnitSize = 100f; // falloff: about -9 dB at the landing camera's 170, -28 at 500
    private const float VehicleMaxDistance = 900f;
    private const float DriveDb = 0f, GroundDb = -1f, FlatDb = -3f, WiperDb = -2f;

    private sealed class DriveSlot
    {
        public AudioStreamPlayer3D Drive = null!, Flat = null!;
        public readonly AudioStreamPlayer3D[] GroundPlayer = new AudioStreamPlayer3D[2];
        public readonly SprayKind[] Kind = { SprayKind.None, SprayKind.None };
        public readonly float[] GroundGain = new float[2];
        public int Active;          // the ground player with the current kind
        public int Unit = -1;       // the vehicle it follows (-1 = free)
        public bool Want;           // still one of the nearest moving vehicles this frame
        public float Level;         // 0..1, eased over FadeSeconds
        public float FlatGain;
        public bool Heavy;
        public Vector3 Pos;
        public float Speed;         // 0..1 share of its top speed
        public float Pitch;
        public SprayKind Under;     // the ground under it now
        public bool HasFlat;
    }

    private struct HeardVehicle
    {
        public int Unit;
        public float Dist, Speed, Pitch;
        public Vector3 Pos;
        public bool Heavy, Flat;
        public SprayKind Ground;
    }

    private struct Motion
    {
        public Vector3 Last;
        public float Speed;
        public bool Has;
    }

    private Node3D? _soundRoot;
    private readonly DriveSlot[] _driveSlots = new DriveSlot[DriveSlots];
    private readonly AudioStreamPlayer3D[] _wiperPlayers = new AudioStreamPlayer3D[WiperPlayers];
    private readonly int[] _wiperUnit = new int[WiperPlayers];
    private HeardVehicle[] _heardVehicles = new HeardVehicle[32];
    private int _heardCount;
    private readonly bool[] _heardTaken = new bool[DriveSlots];
    private readonly Dictionary<int, Motion> _motion = new();

    /// <summary>A vehicle that drives (not a survey team on foot).</summary>
    private static bool MakesDriveSound(SurfaceUnitDef def) => def.CanMine || def.CanFound || def.Mark != null || def.Tyres != null;

    /// <summary>
    /// UpdateRovers, once per vehicle after its model is placed: measure its speed, start a wiper stroke's sound, and
    /// list it when it is a moving vehicle the player can hear. <paramref name="carried"/> = still in or on its drop ship.
    /// </summary>
    private void NoteVehicleSound(SurfaceUnit u, SurfaceUnitDef def, Node3D model, SprayKind ground, bool onPad, bool carried, bool shown,
        RoverLook.Lamps? lamps, float delta)
    {
        var pos = model.Position;
        _motion.TryGetValue(u.Id, out var m);
        if (m.Has && delta > 0f)
        {
            var step = new Vector2(pos.X - m.Last.X, pos.Z - m.Last.Z).Length();
            var now = step > 40f ? 0f : step / delta; // a jump (just made, teleported) isn't speed
            m.Speed = Mathf.Lerp(m.Speed, now, 1f - Mathf.Exp(-delta / 0.15f));
        }
        m.Last = pos;
        m.Has = true;
        _motion[u.Id] = m;
        if (!Heard || !shown) return;
        var dist = pos.DistanceTo(_eye); // the camera (last frame's: UpdateHeadlights sets it after Refresh)

        if (lamps != null && lamps.StrokeBegan && dist < WiperRange)
        {
            // Auto wipers run with washer fluid on wet or dusty ground; forced on over dry glass they squeak.
            var wet = onPad || RainNow > 0.15f || ground is SprayKind.Water or SprayKind.Mud or SprayKind.Snow or SprayKind.FrostGrit
                      || lamps.Wipers != RoverLook.WiperMode.On;
            PlayWiper(u.Id, model, wet);
        }

        if (carried || u.Mode != UnitMode.Mobile || !MakesDriveSound(def) || dist > HearRange) return;
        var top = def.Speed.ToFloat() * HexWidth / Mathf.Max(1, _surface.Map.MinMoveCost);
        var share = top > 0.01f ? m.Speed / top : 0f;
        if (share < 0.04f) return; // parked, waiting or barely creeping

        var heavy = def.CanMine || def.CanFound;
        var jitter = 1f + 0.02f * (((u.Id * 37) % 5 + 5) % 5 - 2) / 2f; // a fixed nudge per vehicle: a convoy isn't one sample
        var s = Mathf.Clamp(share, 0f, 1f);
        float pitch;
        if (heavy) pitch = (0.85f + 0.3f * s) * (def.CanFound ? 1.08f : 1f);
        else pitch = (0.8f + 0.45f * s) * (def.Mark switch { 1 => 1.06f, 3 => 0.93f, _ => 1f });

        if (_heardCount == _heardVehicles.Length) System.Array.Resize(ref _heardVehicles, _heardCount * 2); // rare: only grows
        _heardVehicles[_heardCount++] = new HeardVehicle
        {
            Unit = u.Id, Dist = dist, Pos = pos, Speed = s, Pitch = pitch * jitter, Heavy = heavy,
            Flat = u.FlatTyres != 0, Ground = ground,
        };
    }

    /// <summary>A vehicle went away (UpdateRovers' clean-up): its slot just fades out where it was.</summary>
    private void ForgetVehicleSound(int id) => _motion.Remove(id);

    /// <summary>UpdateRovers, once at the end: give the nearest moving vehicles their slots and drive every player.</summary>
    private void UpdateVehicleSounds(float delta)
    {
        EnsureVehicleSounds();
        var count = _heardCount;
        _heardCount = 0;

        // The nearest DriveSlots of this frame's candidates first (partial selection sort, in place).
        var k = Mathf.Min(count, DriveSlots);
        for (var i = 0; i < k; i++)
        {
            var best = i;
            for (var j = i + 1; j < count; j++)
                if (_heardVehicles[j].Dist < _heardVehicles[best].Dist) best = j;
            if (best != i) (_heardVehicles[i], _heardVehicles[best]) = (_heardVehicles[best], _heardVehicles[i]);
        }

        // Vehicles that already have a slot keep it; the others take a silent one.
        foreach (var slot in _driveSlots) slot.Want = false;
        for (var i = 0; i < k; i++)
        {
            _heardTaken[i] = false;
            foreach (var slot in _driveSlots)
            {
                if (slot.Unit != _heardVehicles[i].Unit) continue;
                Fill(slot, _heardVehicles[i]);
                _heardTaken[i] = true;
                break;
            }
        }
        for (var i = 0; i < k; i++)
        {
            if (_heardTaken[i]) continue;
            foreach (var slot in _driveSlots)
            {
                if (slot.Unit >= 0) continue;
                slot.Unit = _heardVehicles[i].Unit;
                slot.Level = 0f;
                slot.FlatGain = 0f;
                Fill(slot, _heardVehicles[i]);
                break;
            }
        }

        foreach (var slot in _driveSlots) DriveSlotSound(slot, delta);

        // Wiper strokes follow their vehicle while they play.
        for (var i = 0; i < WiperPlayers; i++)
        {
            var p = _wiperPlayers[i];
            if (!p.Playing || !_rovers.TryGetValue(_wiperUnit[i], out var model)) continue;
            p.Position = CabOf(model);
        }
    }

    private static void Fill(DriveSlot slot, in HeardVehicle h)
    {
        slot.Want = true;
        slot.Pos = h.Pos;
        slot.Speed = h.Speed;
        slot.Pitch = h.Pitch;
        slot.Heavy = h.Heavy;
        slot.Under = h.Ground;
        slot.HasFlat = h.Flat;
    }

    private void DriveSlotSound(DriveSlot slot, float delta)
    {
        if (slot.Unit < 0) return;
        slot.Level = Mathf.MoveToward(slot.Level, slot.Want ? 1f : 0f, delta / FadeSeconds);
        if (slot.Level <= 0f && !slot.Want)
        {
            // Silent: free the slot for the next vehicle.
            Hush(slot.Drive);
            Hush(slot.Flat);
            for (var j = 0; j < 2; j++)
            {
                Hush(slot.GroundPlayer[j]);
                slot.GroundGain[j] = 0f;
                slot.Kind[j] = SprayKind.None;
            }
            slot.Unit = -1;
            return;
        }

        var at = slot.Pos + new Vector3(0, 1.2f, 0);
        var s = slot.Speed;

        // Drive loop.
        var drive = Sfx.Load(slot.Heavy ? "vehicle/crawler_loop" : "vehicle/ev_loop");
        slot.Drive.Position = at;
        Run(slot.Drive, drive, DriveDb, slot.Level * (0.5f + 0.5f * s), slot.Pitch);

        // Ground loop, crossfading to the kind under it.
        if (slot.Under != slot.Kind[slot.Active])
        {
            var other = 1 - slot.Active;
            if (slot.Kind[other] != slot.Under)
            {
                slot.GroundPlayer[other].Stop();
                slot.GroundPlayer[other].Stream = GroundStream(slot.Under);
                slot.Kind[other] = slot.Under;
                slot.GroundGain[other] = 0f;
            }
            slot.Active = other;
        }
        for (var j = 0; j < 2; j++)
        {
            var on = j == slot.Active && slot.Kind[j] != SprayKind.None;
            slot.GroundGain[j] = Mathf.MoveToward(slot.GroundGain[j], on ? 1f : 0f, delta / GroundFadeSeconds);
            var player = slot.GroundPlayer[j];
            player.Position = slot.Pos + new Vector3(0, 0.4f, 0);
            Run(player, player.Stream, GroundDb, slot.GroundGain[j] * slot.Level * (0.35f + 0.65f * s), 0.85f + 0.3f * s);
        }

        // A flat tyre's thump, faster with speed.
        slot.FlatGain = Mathf.MoveToward(slot.FlatGain, slot.HasFlat ? 1f : 0f, delta / FadeSeconds);
        slot.Flat.Position = slot.Pos + new Vector3(0, 0.4f, 0);
        Run(slot.Flat, slot.FlatGain > 0f ? Sfx.Load("vehicle/flat_loop") : null, FlatDb, slot.FlatGain * slot.Level, 0.6f + 0.8f * s);
    }

    /// <summary>Play <paramref name="stream"/> on a looping player at a linear gain (stopped when silent or missing).</summary>
    private static void Run(AudioStreamPlayer3D player, AudioStream? stream, float db, float gain, float pitch)
    {
        if (stream == null || gain < 0.002f)
        {
            Hush(player);
            return;
        }
        if (player.Stream != stream) player.Stream = stream;
        player.VolumeDb = db + Mathf.LinearToDb(gain);
        player.PitchScale = Mathf.Clamp(pitch, 0.3f, 3f);
        if (!player.Playing)
        {
            // Somewhere along the loop, so two vehicles never phase.
            var length = (float)stream.GetLength();
            player.Play(length > 0.5f ? GD.Randf() * length * 0.9f : 0f);
        }
    }

    private static void Hush(AudioStreamPlayer3D player)
    {
        if (player.Playing) player.Stop();
    }

    private static AudioStream? GroundStream(SprayKind kind) => kind switch
    {
        SprayKind.Dirt => Sfx.Load("ground/dirt_loop"),
        SprayKind.Grit => Sfx.Load("ground/gravel_loop"),
        SprayKind.Dust => Sfx.Load("ground/sand_loop"),
        SprayKind.RustDust => Sfx.Load("ground/rust_loop"),
        SprayKind.Mud => Sfx.Load("ground/mud_loop"),
        SprayKind.Snow => Sfx.Load("ground/snow_loop"),
        SprayKind.Water => Sfx.Load("ground/water_loop"),
        SprayKind.FrostGrit => Sfx.Load("ground/frost_loop"),
        _ => null,
    };

    /// <summary>Where a vehicle's wipers are: up at the front of the cab (models face -Z).</summary>
    private static Vector3 CabOf(Node3D model) => model.Position + model.Basis * new Vector3(0f, 2.2f, -1.6f);

    private void PlayWiper(int unit, Node3D model, bool wet)
    {
        EnsureVehicleSounds();
        AudioStreamPlayer3D? free = null;
        var index = 0;
        for (var i = 0; i < WiperPlayers; i++)
        {
            if (_wiperPlayers[i].Playing) continue;
            free = _wiperPlayers[i];
            index = i;
            break;
        }
        if (free == null) return; // four strokes already sound nearby: this one is lost in them
        var stream = Sfx.Load(wet ? "vehicle/wiper" : "vehicle/wiper_squeak");
        if (stream == null) return;
        free.Stream = stream;
        free.Position = CabOf(model);
        free.VolumeDb = WiperDb;
        free.PitchScale = 1f + 0.015f * (((unit * 13) % 5 + 5) % 5 - 2); // in time with the blades: barely any pitch change
        free.Play();
        _wiperUnit[index] = unit;
    }

    /// <summary>The players, made once under a node of their own (PlanetView is in the tree when Refresh runs).</summary>
    private void EnsureVehicleSounds()
    {
        if (_soundRoot != null) return;
        var root = new Node3D { Name = "VehicleSounds" };
        _soundRoot = root;
        AddChild(root);
        for (var i = 0; i < DriveSlots; i++)
        {
            var slot = new DriveSlot
            {
                Drive = Player($"Drive{i}"),
                Flat = Player($"Flat{i}"),
            };
            slot.GroundPlayer[0] = Player($"Ground{i}a");
            slot.GroundPlayer[1] = Player($"Ground{i}b");
            _driveSlots[i] = slot;
        }
        for (var i = 0; i < WiperPlayers; i++)
        {
            _wiperPlayers[i] = Player($"Wiper{i}");
            _wiperUnit[i] = -1;
        }

        AudioStreamPlayer3D Player(string name)
        {
            var p = Sfx.Player3D(name, VehicleUnitSize, VehicleMaxDistance);
            root.AddChild(p);
            return p;
        }
    }
}
