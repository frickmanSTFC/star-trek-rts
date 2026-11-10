using Godot;

namespace FinalFrontier;

// Exocomp sounds (owner 2026-11-10: "the exocomps should make some noise"; docs/sound-contract.md "Exocomps").
// Render-only, positional on the Effects bus with the vehicles' falloff (unit size 100, cut at 900).
//
// UpdateDrone calls NoteDrone for every drone once it is placed this frame, Update calls UpdateSounds once after the
// drone loop. Loops come from two pooled banks of players under one node of their own (made once, freed with the view):
// the nearest HoverSlots drones within HearRange of the camera hum (exocomp/hover_loop, a pitch of its own per drone
// ±8 %, a little louder and higher while it flies or hops, softer while its site is paused, fading as it shrinks into
// its dock), and the nearest BeamSlots beams being drawn buzz (exocomp/beam_loop, at the impact point). A slot keeps its
// drone while it is among the nearest and only takes a new one once it has faded out, so nothing is allocated per frame
// and a drone that docks or a site that goes simply fades. One-shots come from a pool of OneShots players: launch at the
// dock as a drone leaves, dock DockLead s before it is home (the clamp click lands as it vanishes), hop when a working
// drone moves to its next spot, lock when a printed piece locks, complete when the shimmer starts on a finished
// building or upgrade, and a chirp from a random working drone of each site every 3–9 s (7–20 s while it is paused).
// Silent while hidden: Heard false (orbit), a drone the fog hides (ConstructionView already hides another player's
// drones out of sight), a site out of sight. The game's pause pauses the players (they sit under the pausable view).
public partial class ConstructionView
{
    private const int HoverSlots = 8;
    private const int BeamSlots = 4;
    private const int OneShots = 6;
    private const float HearRange = 600f;        // a drone further from the camera than this isn't a candidate
    private const float OneShotRange = 600f;
    private const float HopRange = 350f;         // hops are small: only the close ones
    private const float LockGap = 0.08f;         // "finish now" locks a dozen pieces in one frame: one sound
    private const float SoundUnitSize = 100f;    // the vehicles' falloff: about -9 dB at the landing camera's 170
    private const float SoundMaxDistance = 900f;
    private const float HoverFade = 0.25f, BeamFade = 0.1f;
    private const float HoverDb = -3f, BeamDb = -2f;
    private const float LaunchDb = -2f, DockDb = -2f, HopDb = -6f, LockDb = -3f, CompleteDb = 0f, ChirpDb = -3f;
    private const float DockLead = 0.9f;         // dock.wav's clamp click is at ~0.9 s

    private static readonly string[] Chirps = { "exocomp/chirp_a", "exocomp/chirp_b", "exocomp/chirp_c" };

    private struct Candidate
    {
        public int Key;
        public float Dist, Gain, Pitch;
        public Vector3 Pos;
    }

    /// <summary>A few looping players shared by the nearest sources of one kind (drone hums, beams).</summary>
    private sealed class LoopBank
    {
        public required string Path;
        public required float Db, Fade;
        public required AudioStreamPlayer3D[] Players;
        public int[] Key = null!;
        public bool[] Want = null!;
        public float[] Level = null!, Gain = null!, Pitch = null!;
        public Vector3[] Pos = null!;
        public bool[] Taken = null!;
        public Candidate[] Candidates = new Candidate[32];
        public int Count;

        public void Init()
        {
            var n = Players.Length;
            Key = new int[n];
            Want = new bool[n];
            Level = new float[n];
            Gain = new float[n];
            Pitch = new float[n];
            Pos = new Vector3[n];
            Taken = new bool[n];
            for (var i = 0; i < n; i++) Key[i] = -1;
        }

        public void Add(int key, float dist, Vector3 pos, float gain, float pitch)
        {
            if (Count == Candidates.Length) System.Array.Resize(ref Candidates, Count * 2); // rare: only grows
            Candidates[Count++] = new Candidate { Key = key, Dist = dist, Pos = pos, Gain = gain, Pitch = pitch };
        }
    }

    private Node3D? _soundRoot;
    private LoopBank _hover = null!, _beamBank = null!;
    private readonly AudioStreamPlayer3D[] _oneShots = new AudioStreamPlayer3D[OneShots];
    private readonly float[] _oneShotStarted = new float[OneShots];
    private int _nextDroneId;
    private float _lastLockSound = -1f;

    private bool Hearing => _view.Heard;

    /// <summary>The players, made once under a node of their own (the view is in the tree when Update runs).</summary>
    private void EnsureSounds()
    {
        if (_soundRoot != null) return;
        var root = new Node3D { Name = "ExocompSounds" };
        _soundRoot = root;
        AddChild(root);
        _hover = new LoopBank { Path = "exocomp/hover_loop", Db = HoverDb, Fade = HoverFade, Players = Players("Hover", HoverSlots) };
        _hover.Init();
        _beamBank = new LoopBank { Path = "exocomp/beam_loop", Db = BeamDb, Fade = BeamFade, Players = Players("Beam", BeamSlots) };
        _beamBank.Init();
        var shots = Players("Shot", OneShots);
        for (var i = 0; i < OneShots; i++) _oneShots[i] = shots[i];

        AudioStreamPlayer3D[] Players(string name, int count)
        {
            var list = new AudioStreamPlayer3D[count];
            for (var i = 0; i < count; i++)
            {
                list[i] = Sfx.Player3D($"{name}{i}", SoundUnitSize, SoundMaxDistance);
                root.AddChild(list[i]);
            }
            return list;
        }
    }

    /// <summary>A pitch of its own for each drone, spread evenly over ±8 % (golden-ratio steps: neighbours differ).</summary>
    private static float DronePitch(int id)
    {
        var f = id * 0.618034f;
        return 0.92f + 0.16f * (f - Mathf.Floor(f));
    }

    /// <summary>
    /// UpdateDrone, once per drone after it is placed: list its hum and its beam (when one was drawn for it this frame)
    /// as candidates. <paramref name="size"/> = how far out of its dock it is (shrinks to 0.3 as it docks).
    /// </summary>
    private void NoteDrone(Exocomp d, Vector3 pos, Vector3 velocity, float size, bool beamDrawn)
    {
        EnsureSounds();
        if (!Hearing || !d.Node.Visible) return;
        var dist = pos.DistanceTo(_view.HearingEye);
        if (dist > HearRange) return;
        var moving = Mathf.Clamp(velocity.Length() / 10f, 0f, 1f); // flying out or home, or easing to a new spot
        var paused = d.State == DroneState.Working && d.Site is { Working: false };
        var gain = (paused ? 0.55f : 1f) * (0.78f + 0.3f * moving) * Mathf.Clamp((size - 0.3f) / 0.7f, 0f, 1f);
        _hover.Add(d.Id, dist, pos, gain, d.Pitch * (1f + 0.05f * moving));
        if (beamDrawn) _beamBank.Add(d.Id, d.Aim.DistanceTo(_view.HearingEye), d.Aim, 1f, 0.96f + 0.5f * (d.Pitch - 0.92f));
    }

    /// <summary>Update, once after the drones: hand out the loop slots, drive the players, and the sites' chirps.</summary>
    private void UpdateSounds(float delta)
    {
        EnsureSounds();
        Drive(_hover, delta);
        Drive(_beamBank, delta);
        if (!Hearing)
        {
            foreach (var p in _oneShots) Hush(p);
            return;
        }
        for (var i = 0; i < _sites.Count; i++) Chatter(_sites[i]);
    }

    private static void Drive(LoopBank bank, float delta)
    {
        var count = bank.Count;
        bank.Count = 0;
        var slots = bank.Players.Length;

        // The nearest `slots` candidates first (partial selection sort, in place).
        var k = Mathf.Min(count, slots);
        var c = bank.Candidates;
        for (var i = 0; i < k; i++)
        {
            var best = i;
            for (var j = i + 1; j < count; j++)
                if (c[j].Dist < c[best].Dist) best = j;
            if (best != i) (c[i], c[best]) = (c[best], c[i]);
        }

        // Sources that already have a slot keep it; the others take a silent one.
        for (var s = 0; s < slots; s++) bank.Want[s] = false;
        for (var i = 0; i < k; i++)
        {
            bank.Taken[i] = false;
            for (var s = 0; s < slots; s++)
            {
                if (bank.Key[s] != c[i].Key) continue;
                Fill(bank, s, c[i]);
                bank.Taken[i] = true;
                break;
            }
        }
        for (var i = 0; i < k; i++)
        {
            if (bank.Taken[i]) continue;
            for (var s = 0; s < slots; s++)
            {
                if (bank.Key[s] >= 0) continue;
                bank.Key[s] = c[i].Key;
                bank.Level[s] = 0f;
                Fill(bank, s, c[i]);
                break;
            }
        }

        var stream = Sfx.Load(bank.Path);
        for (var s = 0; s < slots; s++)
        {
            if (bank.Key[s] < 0) continue;
            bank.Level[s] = Mathf.MoveToward(bank.Level[s], bank.Want[s] ? 1f : 0f, delta / bank.Fade);
            var player = bank.Players[s];
            if (bank.Level[s] <= 0f && !bank.Want[s])
            {
                Hush(player); // silent: free the slot for the next source
                bank.Key[s] = -1;
                continue;
            }
            player.GlobalPosition = bank.Pos[s];
            var gain = bank.Level[s] * bank.Gain[s];
            if (stream == null || gain < 0.002f)
            {
                Hush(player);
                continue;
            }
            if (player.Stream != stream) player.Stream = stream;
            player.VolumeDb = bank.Db + Mathf.LinearToDb(gain);
            player.PitchScale = Mathf.Clamp(bank.Pitch[s], 0.5f, 2f);
            if (!player.Playing)
            {
                // Somewhere along the loop, so two drones never phase.
                var length = (float)stream.GetLength();
                player.Play(length > 0.5f ? GD.Randf() * length * 0.9f : 0f);
            }
        }
    }

    private static void Fill(LoopBank bank, int s, in Candidate c)
    {
        bank.Want[s] = true;
        bank.Pos[s] = c.Pos;
        bank.Gain[s] = c.Gain;
        bank.Pitch[s] = c.Pitch;
    }

    private static void Hush(AudioStreamPlayer3D player)
    {
        if (player.Playing) player.Stop();
    }

    /// <summary>A one-shot at a world point (exocomp/…). <paramref name="important"/> sounds take the oldest player when
    /// all are busy; the rest are dropped then.</summary>
    private void PlayAt(string path, Vector3 at, float db, float pitch, bool important)
    {
        if (!Hearing || at.DistanceTo(_view.HearingEye) > OneShotRange) return;
        EnsureSounds();
        var stream = Sfx.Load(path);
        if (stream == null) return;
        var index = -1;
        for (var i = 0; i < OneShots; i++)
            if (!_oneShots[i].Playing)
            {
                index = i;
                break;
            }
        if (index < 0)
        {
            if (!important) return;
            index = 0;
            for (var i = 1; i < OneShots; i++)
                if (_oneShotStarted[i] < _oneShotStarted[index]) index = i;
        }
        var p = _oneShots[index];
        p.Stop();
        p.Stream = stream;
        p.GlobalPosition = at;
        p.VolumeDb = db;
        p.PitchScale = pitch;
        p.Play();
        _oneShotStarted[index] = _time;
    }

    /// <summary>A drone was just launched: it pops off its dock (heard only when its site can be seen).</summary>
    private void SoundLaunch(Exocomp d, Site site)
    {
        if (!site.InSight) return;
        PlayAt("exocomp/launch", d.From, LaunchDb, d.Pitch, false);
    }

    /// <summary>A working drone hops to its next spot.</summary>
    private void SoundHop(Exocomp d, Vector3 pos)
    {
        if (!d.Node.Visible || pos.DistanceTo(_view.HearingEye) > HopRange) return;
        PlayAt("exocomp/hop", pos, HopDb, d.Pitch * (0.97f + 0.06f * _rng.Randf()), false);
    }

    /// <summary>A drone is DockLead s from home: the falling whoosh, the clamp click as it vanishes into the dock.</summary>
    private void SoundDock(Exocomp d)
    {
        if (!d.Node.Visible) return;
        PlayAt("exocomp/dock", d.To, DockDb, d.Pitch, false);
    }

    /// <summary>A printed piece locks (its flash starts).</summary>
    private void SoundLock(Site site, Piece p)
    {
        if (!site.InSight || !site.Model.Visible || _time - _lastLockSound < LockGap) return;
        _lastLockSound = _time;
        var c = p.Box.GetCenter();
        PlayAt("exocomp/lock", new Vector3(c.X, Mathf.Lerp(p.Box.Position.Y, p.Box.End.Y, 0.7f), c.Z), LockDb, 0.95f + 0.1f * _rng.Randf(), true);
    }

    /// <summary>The shimmer starts on a finished building or upgrade (it sweeps up over ShimmerSeconds = the chime's rise).</summary>
    private void SoundComplete(Site site, Node3D model)
    {
        if (!site.InSight || !GodotObject.IsInstanceValid(model) || !model.Visible) return;
        PlayAt("exocomp/complete", site.Centre + new Vector3(0, (site.Top - site.Floor) * 0.5f, 0), CompleteDb, 1f, true);
    }

    /// <summary>Now and then one of a site's working drones chirps (rarer while the site is paused).</summary>
    private void Chatter(Site site)
    {
        if (_time < site.NextChirp) return;
        site.NextChirp = _time + (site.Working ? 3f + 6f * _rng.Randf() : 7f + 13f * _rng.Randf());
        if (!site.InSight) return;
        var count = 0;
        foreach (var d in _drones)
            if (d.Site == site && d.State == DroneState.Working && d.Node.Visible) count++;
        if (count == 0) return;
        var pick = (int)(_rng.Randi() % (uint)count);
        foreach (var d in _drones)
        {
            if (d.Site != site || d.State != DroneState.Working || !d.Node.Visible) continue;
            if (pick-- > 0) continue;
            var path = Chirps[(int)(_rng.Randi() % (uint)Chirps.Length)];
            PlayAt(path, d.Node.GlobalPosition, ChirpDb - 2f * _rng.Randf(), d.Pitch * (0.95f + 0.1f * _rng.Randf()), false);
            break;
        }
    }
}
