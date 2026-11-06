using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Vehicle headlights that really light the world (owner 2026-11-06: "Vehicles headlights should actually work and cast
/// light in the env"). Render-only. Each live vehicle in PlanetView (and each drop ship) gets a <see cref="Rig"/>: a few
/// warm-white <see cref="SpotLight3D"/>s at its lamps, aimed forward and a little down (drop ships: landing lights down and
/// ahead while they come in, a belly flood while the ramp is down), its own copy of the glb's "Lamp" lens material that
/// glows while they are on, a very faint additive light cone in front of the headlights (stronger in fog, rain, snow and
/// dust) and a soft glowing ground pool (a <see cref="Decal"/>) that stands in for the lights when the budget or the
/// distance turns them off. The model factories (BuildingModels) no longer add lights, so the build-menu renders, the
/// placement ghost and construction holograms never get any. PlanetView.Headlights decides when they are on and which
/// of them are real lights (the budget below). All numbers are placeholders.
/// </summary>
public static class VehicleLights
{
    // ---------------------------------------------------------------- budget (PlanetView.Headlights)

    /// <summary>Most vehicle spot lights lit at once (nearest the camera first).</summary>
    public const int MaxSpots = 24;

    /// <summary>Only the lit vehicles this close to the camera, nearest first, get shadows from their main lamp.</summary>
    public const int ShadowRigs = 4;
    public const float ShadowDistance = 260f;

    /// <summary>Beyond this from the camera a vehicle's lights are off and its ground pool shows instead.</summary>
    public const float LightDistance = 650f;

    /// <summary>The ground pool shows out to here.</summary>
    public const float PoolDistance = 2600f;

    /// <summary>Each light also fades itself out (Light3D distance fade) a little before <see cref="LightDistance"/>.</summary>
    public const float FadeBegin = 470f, FadeLength = 160f, ShadowFade = 220f;

    // ---------------------------------------------------------------- when they are on (PlanetView.Headlights)

    /// <summary>Daylight (SkyField / Climate 0..1) below which the lamps come on, and above which they go off again.</summary>
    public const float DarkOn = 0.16f, DarkOff = 0.24f;

    /// <summary>Visibility (0..1) below which they come on whatever the time of day.</summary>
    public const float MurkVisibility = 0.65f;

    /// <summary>Seconds a change of the automatic choice must hold before the lamps follow (no flicker at the threshold);
    /// each vehicle adds up to <see cref="SwitchStagger"/> more, so a convoy doesn't switch in step.</summary>
    public const float SwitchDelay = 1.2f, SwitchStagger = 0.9f;

    /// <summary>Seconds to come fully on / go fully off.</summary>
    public const float OnSeconds = 0.25f, OffSeconds = 0.45f;

    /// <summary>Integrity below which the lamps flicker (RoverLook's tail lamps flash below the same).</summary>
    public const int FlickerBelow = 50;

    public enum Kind { RoverMk1, RoverMk2, RoverMk3, Crawler, Constructor, DropShip }

    /// <summary>What a lamp is for: Head = main beam (the one that may cast shadows, gets the cone), Roof = light bar /
    /// roof spots, Landing = drop ship landing lights (shadows, cone), Belly = drop ship flood while unloading, Work = the
    /// crawler's work lamp at the boom tip (the model's own "WorkLight").</summary>
    public enum Role { Head, Roof, Landing, Belly, Work }

    /// <summary>The V-style per-vehicle setting (L key): automatic (dark, fog, storms, dust), forced on, or off.</summary>
    public enum Mode { Auto, On, Off }

    /// <summary>One lamp: where (model space, front = -Z), its downward pitch (degrees, negative = down), range, cone angle
    /// (SpotAngle, half-angle degrees), energy, and whether it gets the faint light cone.</summary>
    public readonly record struct Lamp(Role Role, Vector3 At, float Pitch, float Range, float Angle, float Energy, bool Cone = false);

    /// <summary>A vehicle type's lamps on its glb and on its primitive stand-in, and where the stand-in's lenses go.
    /// <paramref name="LensOff"/> = the lens glow left when off (share of the material's own energy).</summary>
    public sealed record Spec(Lamp[] Lamps, Lamp[] Primitive, Vector3[] PrimitiveLenses, float LensOff, Vector2 Pool);

    private static readonly Color Warm = new(1f, 0.93f, 0.8f);

    /// <summary>The one table of placeholder numbers. Positions come from the Blender scripts (Blender (x, y, z) → Godot
    /// (x, z, -y)); a rover glb's Headlamp±1 and Spot0..n nodes override the Head / Roof positions when present.</summary>
    public static readonly Dictionary<Kind, Spec> Specs = new()
    {
        // build_vehicles.py rover_mk1: round headlamps (±0.78, 1.12, -2.055), two roof spots (±0.8, 2.42, -1.5).
        [Kind.RoverMk1] = new Spec(
            new[] { new Lamp(Role.Head, new(0, 1.12f, -2.3f), -8f, 28f, 36f, 5f, true), new Lamp(Role.Roof, new(0, 2.42f, -1.75f), -14f, 34f, 26f, 3.5f) },
            new[] { new Lamp(Role.Head, new(0, 1.05f, -2.15f), -8f, 26f, 36f, 5f, true), new Lamp(Role.Roof, new(0, 2.3f, -1.75f), -14f, 32f, 26f, 3.2f) },
            new[] { new Vector3(-0.75f, 1.05f, -1.92f), new Vector3(0.75f, 1.05f, -1.92f) }, 0.05f, new(7f, 16f)),
        // rover_mk2 (Kalty): headlamps (±0.85, 1.2, -2.34), light bar with four spots at (x, 2.46, -1.12).
        [Kind.RoverMk2] = new Spec(
            new[] { new Lamp(Role.Head, new(0, 1.2f, -2.6f), -8f, 30f, 36f, 5f, true), new Lamp(Role.Roof, new(0, 2.46f, -1.38f), -16f, 36f, 28f, 3.8f) },
            new[] { new Lamp(Role.Head, new(0, 1.05f, -2.15f), -8f, 26f, 36f, 5f, true), new Lamp(Role.Roof, new(0, 2.3f, -1.75f), -14f, 32f, 26f, 3.2f) },
            new[] { new Vector3(-0.75f, 1.05f, -1.92f), new Vector3(0.75f, 1.05f, -1.92f) }, 0.05f, new(7.5f, 17f)),
        // rover_mk3: slim LED headlamps (±0.78, 1.55, -2.63), light bar spots at (x, 2.77, -1.06).
        [Kind.RoverMk3] = new Spec(
            new[] { new Lamp(Role.Head, new(0, 1.55f, -2.9f), -8f, 32f, 38f, 5.5f, true), new Lamp(Role.Roof, new(0, 2.77f, -1.3f), -16f, 38f, 28f, 4f) },
            new[] { new Lamp(Role.Head, new(0, 1.05f, -2.15f), -8f, 26f, 36f, 5f, true), new Lamp(Role.Roof, new(0, 2.3f, -1.75f), -14f, 32f, 26f, 3.2f) },
            new[] { new Vector3(-0.75f, 1.05f, -1.92f), new Vector3(0.75f, 1.05f, -1.92f) }, 0.05f, new(8f, 18f)),
        // build_miner.py: headlamps (±0.95, 1.5, -2.85), cab roof spots (±0.78, 3.12, -2.37); the boom-tip work lamp is the
        // model's own "WorkLight" (BuildingModels.AddCrawlerEffects). Deployed, the head and roof lamps tip down onto the dig.
        [Kind.Crawler] = new Spec(
            new[] { new Lamp(Role.Head, new(0, 1.5f, -3.1f), -9f, 30f, 40f, 5.5f, true), new Lamp(Role.Roof, new(0, 3.12f, -2.6f), -20f, 34f, 34f, 4f), new Lamp(Role.Work, default, 0f, 0f, 0f, 3.5f) },
            new[] { new Lamp(Role.Head, new(0, 1.4f, -2.95f), -9f, 28f, 40f, 5.5f, true), new Lamp(Role.Roof, new(0, 3.2f, -2.3f), -20f, 32f, 34f, 4f), new Lamp(Role.Work, default, 0f, 0f, 0f, 3.5f) },
            new[] { new Vector3(-0.9f, 1.4f, -2.72f), new Vector3(0.9f, 1.4f, -2.72f) }, 0.05f, new(9f, 18f)),
        // build_constructor.py: headlamps (±0.85, 1.24, -3.62), light bar with five spots at (x, 2.81, -2.54).
        [Kind.Constructor] = new Spec(
            new[] { new Lamp(Role.Head, new(0, 1.24f, -3.9f), -8f, 30f, 38f, 5.5f, true), new Lamp(Role.Roof, new(0, 2.81f, -2.8f), -15f, 38f, 30f, 4f) },
            new[] { new Lamp(Role.Head, new(0, 1.1f, -3.6f), -8f, 28f, 38f, 5.5f, true), new Lamp(Role.Roof, new(0, 2.9f, -3.2f), -15f, 36f, 30f, 4f) },
            new[] { new Vector3(-0.85f, 1.1f, -3.42f), new Vector3(0.85f, 1.1f, -3.42f) }, 0.05f, new(8f, 18f)),
        // build_dropship.py: nose landing lamps (±0.85, 2.25, -6.62); belly (BELLY 0.85) under the bay. The rest of the
        // Lamp material (strobe) keeps a little glow when they are off.
        [Kind.DropShip] = new Spec(
            new[] { new Lamp(Role.Landing, new(0, 2.25f, -6.9f), -50f, 95f, 28f, 10f, true), new Lamp(Role.Belly, new(0, 0.7f, 1.2f), -90f, 16f, 70f, 4f) },
            new[] { new Lamp(Role.Landing, new(0, 2.25f, -6.9f), -50f, 95f, 28f, 10f, true), new Lamp(Role.Belly, new(0, 0.7f, 1.2f), -90f, 16f, 70f, 4f) },
            System.Array.Empty<Vector3>(), 0.3f, Vector2.Zero),
    };

    /// <summary>The kind for a ground vehicle type (miner → crawler, founder → construction rover, else the rover mark).</summary>
    public static Kind KindOf(FinalFrontier.Sim.Data.SurfaceUnitDef def) =>
        def.CanMine ? Kind.Crawler : def.CanFound ? Kind.Constructor : def.Mark switch { 1 => Kind.RoverMk1, 3 => Kind.RoverMk3, _ => Kind.RoverMk2 };

    /// <summary>Meta set on our lights and lens materials: PlanetView.Sky's night boost leaves them alone (their energy is
    /// already the night value; boosting it too would stack).</summary>
    public static readonly StringName NoBoostMeta = "vehicle_light";

    // ---------------------------------------------------------------- shared resources

    private static ArrayMesh? _coneMesh;
    private static StandardMaterial3D? _coneMaterial;
    private static Texture2D? _poolTexture;
    private static StandardMaterial3D? _lensStandIn;

    /// <summary>A unit cone along -Z (apex radius 0.06 at z 0, radius 1 at z -1), vertex alpha fading from the lamp out.</summary>
    private static ArrayMesh ConeMesh()
    {
        if (_coneMesh != null) return _coneMesh;
        const int segments = 18;
        float[] ringZ = { 0f, -0.3f, -1f };
        float[] ringR = { 0.06f, 0.33f, 1f };
        float[] ringA = { 0.85f, 0.45f, 0f };
        var vertices = new Vector3[segments * ringZ.Length];
        var colours = new Color[vertices.Length];
        for (var r = 0; r < ringZ.Length; r++)
            for (var s = 0; s < segments; s++)
            {
                var a = Mathf.Tau * s / segments;
                vertices[r * segments + s] = new Vector3(Mathf.Cos(a) * ringR[r], Mathf.Sin(a) * ringR[r], ringZ[r]);
                colours[r * segments + s] = new Color(1f, 1f, 1f, ringA[r]);
            }
        var indices = new int[(ringZ.Length - 1) * segments * 6];
        var k = 0;
        for (var r = 0; r < ringZ.Length - 1; r++)
            for (var s = 0; s < segments; s++)
            {
                int a = r * segments + s, b = r * segments + (s + 1) % segments, c = a + segments, d = b + segments;
                indices[k++] = a; indices[k++] = c; indices[k++] = b;
                indices[k++] = b; indices[k++] = c; indices[k++] = d;
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        _coneMesh = new ArrayMesh();
        _coneMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return _coneMesh;
    }

    /// <summary>Additive, unshaded, two-sided, soft where it meets the ground, gone beyond ~420 from the camera. Each cone
    /// sets its strength through GeometryInstance3D.Transparency.</summary>
    private static StandardMaterial3D ConeMaterial() => _coneMaterial ??= new StandardMaterial3D
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        VertexColorUseAsAlbedo = true,
        AlbedoColor = new Color(1f, 0.9f, 0.72f, 0.16f),
        ProximityFadeEnabled = true,
        ProximityFadeDistance = 1.8f,
        DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha,
        DistanceFadeMinDistance = 420f, // min > max: fades out from 160 to 420
        DistanceFadeMaxDistance = 160f,
    };

    /// <summary>Soft radial blob for the ground pool (white centre to black edge).</summary>
    private static Texture2D PoolTexture()
    {
        if (_poolTexture != null) return _poolTexture;
        var gradient = new Gradient();
        gradient.SetColor(0, Colors.White);
        gradient.SetColor(1, Colors.Black);
        gradient.AddPoint(0.45f, new Color(0.35f, 0.35f, 0.35f));
        _poolTexture = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 64,
            Height = 64,
        };
        return _poolTexture;
    }

    // ---------------------------------------------------------------- one vehicle's lights

    /// <summary>
    /// One vehicle's lamps. Built before the model enters the tree (so the night boost and the surface feed see the
    /// finished nodes). PlanetView.Headlights writes its state (<see cref="Demand"/>, damage, mode…) and calls
    /// <see cref="Apply"/> once a frame with what the budget allows. No per-frame allocation.
    /// </summary>
    public sealed class Rig
    {
        public readonly Node3D Model;
        public readonly Kind Kind;
        public readonly int Seed;
        private readonly Lamp[] _lamps;
        private readonly Light3D?[] _lights;
        private readonly MeshInstance3D?[] _cones;
        private readonly float[] _pitch;
        private readonly bool[] _shadow;
        private readonly BaseMaterial3D? _lens;
        private readonly float _lensBase, _lensOff;
        private readonly Decal? _pool;
        private float _lensShown = -1f;
        private bool _poolShown;

        /// <summary>Per lamp, 0..1, set by the owner each frame (drop ship landing/belly; crawler work lamp while
        /// deployed). Head/roof lamps default to 1.</summary>
        public readonly float[] Demand;

        public Mode Mode;
        public int UnitId;

        // Written by PlanetView.Headlights.
        public float Level, Murk;
        public bool AutoOn, Damaged, Dead, Hidden, Fresh = true;
        public float AutoHeld;
        public double NextWeather;
        public string Reason = "daylight";
        public readonly float Stagger;

        /// <summary>Extra downward tilt for the head and roof lamps (degrees; the deployed crawler looks at its dig).</summary>
        public float Tilt;

        private Rig(Node3D model, Kind kind, int seed)
        {
            Model = model;
            Kind = kind;
            Seed = seed;
            Stagger = SwitchStagger * Mathf.PosMod(seed * 0.618034f, 1f);
            var spec = Specs[kind];
            var lens = ShareLens(model);
            var glb = lens != null;
            _lamps = (Lamp[])(glb ? spec.Lamps : spec.Primitive).Clone();
            if (glb) FromMarkers(model, _lamps);
            _lights = new Light3D?[_lamps.Length];
            _cones = new MeshInstance3D?[_lamps.Length];
            _pitch = new float[_lamps.Length];
            _shadow = new bool[_lamps.Length];
            Demand = new float[_lamps.Length];
            for (var i = 0; i < _lamps.Length; i++)
            {
                var lamp = _lamps[i];
                Demand[i] = lamp.Role is Role.Head or Role.Roof ? 1f : 0f;
                if (lamp.Role == Role.Work)
                {
                    // The crawler's own boom-tip lamp (an OmniLight3D from AddCrawlerEffects), now ours to switch.
                    if (model.GetNodeOrNull<Light3D>("WorkLight") is { } work)
                    {
                        work.SetMeta(NoBoostMeta, true);
                        work.Visible = false;
                        _lights[i] = work;
                    }
                    continue;
                }
                var spot = new SpotLight3D
                {
                    Name = $"Lamp{lamp.Role}{i}",
                    LightColor = Warm,
                    LightEnergy = 0f,
                    SpotRange = lamp.Range,
                    SpotAngle = lamp.Angle,
                    SpotAngleAttenuation = 0.9f,
                    SpotAttenuation = 1f,
                    ShadowEnabled = false,
                    ShadowBias = 0.06f,
                    ShadowNormalBias = 1.2f,
                    DistanceFadeEnabled = true,
                    DistanceFadeBegin = FadeBegin,
                    DistanceFadeLength = FadeLength,
                    DistanceFadeShadow = ShadowFade,
                    Position = lamp.At,
                    RotationDegrees = new Vector3(lamp.Pitch, 0, 0),
                    Visible = false,
                };
                spot.SetMeta(NoBoostMeta, true);
                _pitch[i] = lamp.Pitch;
                if (lamp.Cone)
                {
                    var length = lamp.Range * 0.55f;
                    var radius = length * Mathf.Tan(Mathf.DegToRad(lamp.Angle)) * 0.8f;
                    var cone = new MeshInstance3D
                    {
                        Name = "LightCone",
                        Mesh = ConeMesh(),
                        MaterialOverride = ConeMaterial(),
                        Scale = new Vector3(radius, radius, length),
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                        Transparency = 1f,
                        Visible = false,
                    };
                    spot.AddChild(cone);
                    _cones[i] = cone;
                }
                model.AddChild(spot);
                _lights[i] = spot;
            }

            // Lenses: the glb's own Lamp surfaces, or small glowing boxes on a primitive stand-in.
            if (lens == null && spec.PrimitiveLenses.Length > 0)
            {
                _lensStandIn ??= new StandardMaterial3D { AlbedoColor = new Color(1f, 0.95f, 0.82f), EmissionEnabled = true, Emission = new Color(1f, 0.95f, 0.82f), EmissionEnergyMultiplier = 4f };
                lens = (BaseMaterial3D)_lensStandIn.Duplicate();
                foreach (var at in spec.PrimitiveLenses)
                    model.AddChild(new MeshInstance3D
                    {
                        Name = "HeadlampLens",
                        Mesh = new BoxMesh { Size = new Vector3(0.36f, 0.16f, 0.05f) },
                        MaterialOverride = lens,
                        Position = at,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    });
            }
            if (lens != null)
            {
                lens.SetMeta(NoBoostMeta, true);
                _lens = lens;
                _lensBase = lens.EmissionEnergyMultiplier > 0f ? lens.EmissionEnergyMultiplier : 4f;
                lens.EmissionEnergyMultiplier = _lensBase * spec.LensOff;
            }
            _lensOff = spec.LensOff;

            // Far stand-in: a soft warm glow projected on the ground ahead.
            if (spec.Pool != Vector2.Zero)
            {
                var front = 0f;
                foreach (var lamp in _lamps)
                    if (lamp.Role == Role.Head) front = lamp.At.Z;
                _pool = new Decal
                {
                    Name = "LightPool",
                    Size = new Vector3(spec.Pool.X, 6f, spec.Pool.Y),
                    Position = new Vector3(0, 1f, front - spec.Pool.Y * 0.56f), // starts just ahead of the nose, not on it
                    TextureEmission = PoolTexture(),
                    EmissionEnergy = 0f,
                    Modulate = Warm,
                    UpperFade = 0.3f,
                    LowerFade = 0.3f,
                    DistanceFadeEnabled = true,
                    DistanceFadeBegin = PoolDistance - 400f,
                    DistanceFadeLength = 400f,
                    Visible = false,
                };
                model.AddChild(_pool);
            }
        }

        /// <summary>Build a vehicle's lamps (before the model enters the tree, after RoverLook's grime overlay so the
        /// cones and lenses don't get dirt).</summary>
        public static Rig Attach(Node3D model, Kind kind, int seed) => new(model, kind, seed);

        /// <summary>What lamp <paramref name="i"/> (the order of <see cref="Demand"/>) is for.</summary>
        public Role RoleOf(int i) => _lamps[i].Role;

        /// <summary>Lights (spots and the work lamp) this rig would light now.</summary>
        public int SpotsWanted
        {
            get
            {
                var n = 0;
                for (var i = 0; i < _lamps.Length; i++)
                    if (_lights[i] != null && Demand[i] > 0.01f) n++;
                return n;
            }
        }

        /// <summary>
        /// Show this frame's state: <paramref name="lit"/> = its lights are within the budget; <paramref name="shadow"/> =
        /// its main lamp casts shadows; <paramref name="pool"/> = draw the ground pool instead. Only touches properties
        /// that change.
        /// </summary>
        public void Apply(bool lit, bool shadow, bool pool, float time)
        {
            var flicker = Damaged ? Flicker(time) : 1f;
            var level = Level * flicker;
            var strongest = 0f;
            for (var i = 0; i < _lamps.Length; i++)
            {
                var lamp = _lamps[i];
                var demand = Demand[i];
                strongest = Mathf.Max(strongest, demand);
                var on = lit && demand > 0.01f && level > 0.002f;
                if (_lights[i] is not { } light) continue;
                if (light.Visible != on) light.Visible = on;
                if (on)
                {
                    light.LightEnergy = lamp.Energy * level * demand;
                    var castShadow = shadow && lamp.Role is Role.Head or Role.Landing;
                    if (_shadow[i] != castShadow)
                    {
                        _shadow[i] = castShadow;
                        light.ShadowEnabled = castShadow;
                    }
                    if (lamp.Role is Role.Head or Role.Roof)
                    {
                        var pitch = lamp.Pitch - Tilt * (lamp.Role == Role.Roof ? 1f : 0.7f);
                        if (Mathf.Abs(pitch - _pitch[i]) > 0.05f)
                        {
                            _pitch[i] = pitch;
                            light.RotationDegrees = new Vector3(pitch, 0, 0);
                        }
                    }
                }
                if (_cones[i] is { } cone)
                {
                    // Barely there in clear air; the beam shows in fog, rain, snow and dust.
                    var strength = on ? Mathf.Clamp(level * demand * (0.25f + 0.75f * Murk), 0f, 1f) : 0f;
                    var show = strength > 0.02f;
                    if (cone.Visible != show) cone.Visible = show;
                    if (show) cone.Transparency = 1f - strength;
                }
            }

            if (_lens != null)
            {
                var glow = _lensBase * Mathf.Lerp(_lensOff, 1f, Mathf.Clamp(level * strongest, 0f, 1f));
                if (Mathf.Abs(glow - _lensShown) > 0.01f)
                {
                    _lensShown = glow;
                    _lens.EmissionEnergyMultiplier = glow;
                }
            }

            if (_pool != null)
            {
                var showPool = pool && level > 0.05f;
                if (_poolShown != showPool)
                {
                    _poolShown = showPool;
                    _pool.Visible = showPool;
                }
                if (showPool) _pool.EmissionEnergy = 1.4f * level;
            }
        }

        /// <summary>A failing lamp: mostly steady with a faint shimmer, dropping out now and then.</summary>
        private float Flicker(float t)
        {
            var s = Seed * 1.37f;
            var dips = Mathf.Sin(t * 13.7f + s) + Mathf.Sin(t * 4.1f + s * 2.3f);
            return dips > 1.3f ? 0.1f : 0.86f + 0.14f * Mathf.Sin(t * 47f + s);
        }

        /// <summary>One shared copy of the glb's "Lamp" material for every lamp surface on this model (null if it has none,
        /// i.e. a primitive stand-in).</summary>
        private static BaseMaterial3D? ShareLens(Node model)
        {
            BaseMaterial3D? copy = null;
            void Visit(Node node)
            {
                if (node is MeshInstance3D mi)
                {
                    if (mi.MaterialOverride is BaseMaterial3D over && over.ResourceName == "Lamp")
                        mi.MaterialOverride = copy ??= (BaseMaterial3D)over.Duplicate();
                    else if (mi.Mesh != null)
                        for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                            if (mi.GetActiveMaterial(i) is BaseMaterial3D m && m.ResourceName == "Lamp")
                                mi.SetSurfaceOverrideMaterial(i, copy ??= (BaseMaterial3D)m.Duplicate());
                }
                foreach (var child in node.GetChildren()) Visit(child);
            }
            Visit(model);
            return copy;
        }

        /// <summary>Rover glbs keep their lamp parts as nodes: put the Head lamp just ahead of the Headlamp±1 pair and the
        /// Roof lamp just ahead of the Spot0..n row (model space), when they are there.</summary>
        private static void FromMarkers(Node3D model, Lamp[] lamps)
        {
            var head = Average(model, "Headlamp-1", "Headlamp1");
            Vector3? roof = null;
            var sum = Vector3.Zero;
            var count = 0;
            for (var k = 0; k < 8 && model.FindChild($"Spot{k}", true, false) is Node3D spot; k++)
            {
                sum += ModelSpace(spot, model);
                count++;
            }
            if (count > 0) roof = sum / count;
            for (var i = 0; i < lamps.Length; i++)
            {
                if (lamps[i].Role == Role.Head && head is { } h) lamps[i] = lamps[i] with { At = h + new Vector3(0, 0, -0.25f) };
                else if (lamps[i].Role == Role.Roof && roof is { } r) lamps[i] = lamps[i] with { At = r + new Vector3(0, 0.04f, -0.2f) };
            }
        }

        private static Vector3? Average(Node3D model, string a, string b) =>
            model.FindChild(a, true, false) is Node3D na && model.FindChild(b, true, false) is Node3D nb
                ? (ModelSpace(na, model) + ModelSpace(nb, model)) / 2f
                : null;

        /// <summary>A node's origin in an ancestor's space, from the local transforms (works before it's in the tree).</summary>
        private static Vector3 ModelSpace(Node3D node, Node3D ancestor)
        {
            var t = node.Transform;
            for (var p = node.GetParent(); p != null && p != ancestor; p = p.GetParent())
                if (p is Node3D p3) t = p3.Transform * t;
            return t.Origin;
        }
    }
}
