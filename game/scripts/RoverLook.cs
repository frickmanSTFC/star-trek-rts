using System.Collections.Generic;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Per-vehicle looks that the model file can't carry (render-only): the vehicle's own name and hull number painted on
/// its blank decal spots, and dirt, mud and snow that build up from the ground it drives on and wash off on the repair
/// pad.
/// </summary>
public static class RoverLook
{
    /// <summary>Decal paint: the dark navy of the hull numbers in the Blender renders.</summary>
    private static readonly Color DecalInk = new(0.12f, 0.15f, 0.3f);

    private const int DecalFontSize = 96;

    /// <summary>
    /// Paint the name and number on the model's decal markers. Each marker (an empty from the glb) carries the decal's
    /// placement, with its scale = (max width, 1, text height) in world units and the text normal on local +Y.
    /// Models without markers (the primitive fallbacks) are left alone; the rovers' and the mining crawler's glbs have them.
    /// </summary>
    public static void Paint(Node3D model, string? name, string? number)
    {
        foreach (var (marker, text) in new[] { ("NameL", name), ("NameR", name), ("NameRear", name), ("NumberL", number), ("NumberR", number) })
        {
            if (string.IsNullOrEmpty(text) || model.FindChild(marker, true, false) is not Node3D spot) continue;
            var maxWidth = Mathf.Abs(spot.Scale.X);
            var height = Mathf.Abs(spot.Scale.Z);
            spot.Scale = Vector3.One;
            if (height <= 0.001f) continue;
            var label = new Label3D
            {
                Name = "Decal",
                Text = text,
                FontSize = DecalFontSize,
                PixelSize = height / DecalFontSize,
                OutlineSize = 0,
                Modulate = DecalInk,
                Shaded = true,
                DoubleSided = false,
                AlphaCut = Label3D.AlphaCutMode.Discard,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
                // Label text faces its +Z; the marker's text normal is its +Y. Sit a hair off the paint.
                RotationDegrees = new Vector3(-90f, 0f, 0f),
                Position = new Vector3(0f, 0.012f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            var font = label.Font ?? ThemeDB.FallbackFont;
            if (font != null && maxWidth > 0.001f)
            {
                var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, DecalFontSize).X * label.PixelSize;
                if (width > maxWidth) label.PixelSize *= maxWidth / width;
            }
            spot.AddChild(label);
        }
    }

    public enum WiperMode { Auto, On, Off }

    /// <summary>The fan the wipers clear, in model space: the first and last pivot, the in-glass axes (Along = the
    /// blade's parked direction, Up = up the glass), the blade length and the sweep in radians.</summary>
    public readonly record struct WiperArea(Vector3 First, Vector3 Last, Vector3 Along, Vector3 Up, float Length, float Sweep);

    /// <summary>
    /// A vehicle's working lamps and wipers. The amber roof beacon ("Beacon" mesh) double-flashes while it drives and
    /// lights the ground around it with a small amber light; the red tail lamps (material "TailLamp") glow steadily and
    /// flash when the vehicle is broken down or badly damaged. Wipers (pivots "Wiper0", "Wiper1", … from the glb) sweep
    /// on wet, muddy, snowy or dusty ground and while the vehicle is washed, then park at the bottom of the glass.
    /// Each vehicle gets its own copies of the lamp materials, and its own phase so a convoy doesn't blink in step.
    /// </summary>
    public sealed class Lamps
    {
        private readonly BaseMaterial3D? _beacon;
        private readonly OmniLight3D? _beaconLight;
        private readonly BaseMaterial3D? _tail;
        private readonly float _tailEnergy;
        private readonly List<(Node3D Pivot, Basis Parked)> _wipers = new();
        private float _time, _wipe;

        /// <summary>Wiper sweep, degrees from the parked position up across the glass.</summary>
        private const float WipeDegrees = 100f;

        /// <summary>Seconds for one wiper stroke up and back (the vehicle/wiper sound is cut to it: tools/gen_sounds.py
        /// WIPE_T).</summary>
        public const float WipeSeconds = 1.3f;

        private Lamps(Node3D model, int seed)
        {
            _time = seed * 0.37f;
            if (model.FindChild("Beacon", true, false) is MeshInstance3D beacon)
            {
                _beacon = OwnCopy(beacon, "Amber") ?? OwnCopy(beacon, null);
                _beaconLight = new OmniLight3D
                {
                    LightColor = new Color(1f, 0.55f, 0.15f),
                    OmniRange = 7f,
                    LightEnergy = 0f,
                    ShadowEnabled = false,
                    Position = new Vector3(0, 0.25f, 0),
                };
                beacon.AddChild(_beaconLight);
            }
            _tail = ShareTail(model);
            _tailEnergy = _tail?.EmissionEnergyMultiplier ?? 1f;
            for (var k = 0; model.FindChild($"Wiper{k}", true, false) is Node3D pivot; k++)
                _wipers.Add((pivot, pivot.Basis));
            if (_wipers.Count > 0)
            {
                // The wiped fan, in the model's own space, for the grime shader to keep clean.
                var first = ModelSpace(_wipers[0].Pivot, model);
                var last = ModelSpace(_wipers[^1].Pivot, model);
                var length = model.FindChild("Wiper0Blade", true, false) is MeshInstance3D blade ? blade.GetAabb().Size.X : 0.6f;
                // Blender's local X (along the bottom edge) stays X; its local Y (up the glass) is local -Z here.
                Area = new WiperArea(first.Origin, last.Origin, first.Basis.X.Normalized(), -first.Basis.Z.Normalized(), length,
                    Mathf.DegToRad(WipeDegrees));
            }
        }

        /// <summary>A node's transform relative to an ancestor, from the local transforms (works before it's in the tree).</summary>
        private static Transform3D ModelSpace(Node3D node, Node3D ancestor)
        {
            var t = node.Transform;
            for (var p = node.GetParent(); p != null && p != ancestor; p = p.GetParent())
                if (p is Node3D p3) t = p3.Transform * t;
            return t;
        }

        /// <summary>Where the wipers sweep (null without wipers).</summary>
        public WiperArea? Area { get; }

        /// <summary>How clean the wiped fan is: up to 1 a moment after the wipers start, back to 0 over ~45 s without them.</summary>
        public float Wiped { get; private set; }

        public static Lamps Attach(Node3D model, int seed) => new(model, seed);

        public bool HasWipers => _wipers.Count > 0;

        /// <summary>True for the one frame a wiper stroke starts (the sound plays then: PlanetView.Sounds.cs).</summary>
        public bool StrokeBegan { get; private set; }

        /// <summary>The V key's setting: automatic (by the ground), or forced on or off.</summary>
        public WiperMode Wipers { get; set; }

        /// <summary>Advance one frame.</summary>
        public void Update(bool moving, bool alarm, bool wipe, float delta)
        {
            _time += delta;

            // Beacon: two quick flashes every 1.2 s while driving, dark when parked.
            if (_beacon != null)
            {
                var t = _time % 1.2f;
                var flash = moving && (t < 0.12f || (t > 0.24f && t < 0.36f)) ? 1f : 0f;
                _beacon.EmissionEnergyMultiplier = moving ? 0.6f + flash * 7f : 0.25f;
                if (_beaconLight != null) _beaconLight.LightEnergy = flash * 1.6f;
            }

            // Tail lamps: steady, or a hard red flash twice a second when something is wrong.
            if (_tail != null)
                _tail.EmissionEnergyMultiplier = alarm ? ((_time * 2f) % 1f < 0.5f ? _tailEnergy * 2.5f : 0.15f) : _tailEnergy;

            // Wipers: keep sweeping while needed; when not, finish the stroke and park.
            StrokeBegan = false;
            if (_wipers.Count == 0) return;
            wipe = Wipers switch { WiperMode.On => true, WiperMode.Off => false, _ => wipe };
            var sweeping = wipe || _wipe % 1f > 0.001f;
            if (sweeping)
            {
                var before = _wipe;
                _wipe += delta / WipeSeconds;
                var crossed = Mathf.FloorToInt(_wipe) > Mathf.FloorToInt(before);
                if (!wipe && crossed) _wipe = Mathf.Floor(_wipe); // parked
                else if (crossed || before % 1f <= 0.001f) StrokeBegan = delta > 0f; // a new stroke from the rest
            }
            Wiped = sweeping ? Mathf.Min(1f, Wiped + delta / WipeSeconds) : Mathf.Max(0f, Wiped - delta / 45f);
            var angle = Mathf.DegToRad(WipeDegrees) * (0.5f - 0.5f * Mathf.Cos(_wipe * Mathf.Tau));
            for (var i = 0; i < _wipers.Count; i++)
            {
                var (pivot, parked) = _wipers[i];
                // The pivot's Blender local Z (into the glass) is local +Y here; a positive turn lifts the blade from
                // along the bottom edge up across the glass.
                pivot.Basis = parked * new Basis(Vector3.Up, angle);
            }
        }

        /// <summary>Give this mesh its own copy of the material named <paramref name="name"/> (any material when null)
        /// and return it, or null if the mesh has no such material.</summary>
        private static BaseMaterial3D? OwnCopy(MeshInstance3D mi, string? name)
        {
            if (mi.MaterialOverride is BaseMaterial3D over && (name == null || over.ResourceName == name))
            {
                var copy = (BaseMaterial3D)over.Duplicate();
                mi.MaterialOverride = copy;
                return copy;
            }
            if (mi.Mesh == null) return null;
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                if (mi.GetActiveMaterial(i) is BaseMaterial3D m && (name == null || m.ResourceName == name))
                {
                    var copy = (BaseMaterial3D)m.Duplicate();
                    mi.SetSurfaceOverrideMaterial(i, copy);
                    return copy;
                }
            return null;
        }

        /// <summary>One shared copy of the tail-lamp material for every tail lamp on this model.</summary>
        private static BaseMaterial3D? ShareTail(Node model)
        {
            BaseMaterial3D? copy = null;
            void Visit(Node node)
            {
                if (node is MeshInstance3D mi)
                {
                    if (mi.MaterialOverride is BaseMaterial3D over && over.ResourceName == "TailLamp")
                        mi.MaterialOverride = copy ??= (BaseMaterial3D)over.Duplicate();
                    else if (mi.Mesh != null)
                        for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                            if (mi.GetActiveMaterial(i) is BaseMaterial3D m && m.ResourceName == "TailLamp")
                                mi.SetSurfaceOverrideMaterial(i, copy ??= (BaseMaterial3D)m.Duplicate());
                }
                foreach (var child in node.GetChildren()) Visit(child);
            }
            Visit(model);
            return copy;
        }
    }

    /// <summary>
    /// Dirt on one vehicle: an overlay material (shaders/grime.gdshader) on every mesh of the model, fed three amounts
    /// (dust, mud, snow; 0 clean to 1 caked) that grow while the vehicle drives on matching ground, slowly fade
    /// otherwise (snow melts, dust shakes off a little) and wash off quickly while it is parked on its repair pad.
    /// </summary>
    public sealed class Grime
    {
        private static Shader? _shader;

        private readonly Node3D _model;
        private readonly ShaderMaterial _material;
        private readonly List<Label3D> _decals = new();
        private float _dust, _mud, _snow;
        private Color _dustColour = DustOf(SprayKind.Dust);

        /// <summary>Seconds of driving to go from clean to fully caked.</summary>
        private const float DustSeconds = 50f, MudSeconds = 40f, SnowSeconds = 25f;

        /// <summary>Seconds for the repair pad's wash to clean a fully caked vehicle.</summary>
        private const float WashSeconds = 5f;

        private Grime(Node3D model, ShaderMaterial material)
        {
            _model = model;
            _material = material;
        }

        public float Dust => _dust;
        public float Mud => _mud;
        public float Snow => _snow;

        /// <param name="bodyHeight">Model-space height of the roof, for the "caked low, thin on top" gradient.</param>
        public static Grime Attach(Node3D model, float bodyHeight)
        {
            _shader ??= GD.Load<Shader>("res://shaders/grime.gdshader");
            var material = new ShaderMaterial { Shader = _shader };
            material.SetShaderParameter("body_height", bodyHeight);
            var grime = new Grime(model, material);
            grime.Cover(model);
            grime.Push();
            return grime;
        }

        private void Cover(Node node)
        {
            switch (node)
            {
                case CpuParticles3D: return; // the wheel spray
                case MeshInstance3D mi when mi.Name != "Selected": mi.MaterialOverlay = _material; break;
                case Label3D label when label.Name == "Decal": _decals.Add(label); break;
            }
            foreach (var child in node.GetChildren()) Cover(child);
        }

        /// <summary>Advance one frame. <paramref name="kind"/> is the ground under the vehicle.</summary>
        /// <summary>Tell the shader where the wipers sweep, so the fan they clear stays clean.</summary>
        public void SetWipers(WiperArea area)
        {
            _material.SetShaderParameter("wiper0", area.First);
            _material.SetShaderParameter("wiper1", area.Last);
            _material.SetShaderParameter("wiper_x", area.Along);
            _material.SetShaderParameter("wiper_y", area.Up);
            _material.SetShaderParameter("wiper_len", area.Length);
            _material.SetShaderParameter("wiper_sweep", area.Sweep);
        }

        /// <summary>How clean the wiped fan is (from <see cref="Lamps.Wiped"/>).</summary>
        public float Wiped { get; set; }

        /// <summary>Rover damage, 0..1 (scratches, then scorch; PlanetView.RoverDamage sets it from integrity).</summary>
        public float Damage { get; set; }

        /// <summary>Clean colour of the painted names and numbers (navy; light on a dark finish, CosmeticLook.Ink).</summary>
        public Color Ink { get; set; } = DecalInk;

        /// <summary>Test panel: set the dirt directly (each 0..1); it then builds up and washes off as usual.</summary>
        public void Set(float dust, float mud, float snow)
        {
            _dust = Mathf.Clamp(dust, 0f, 1f);
            _mud = Mathf.Clamp(mud, 0f, 1f);
            _snow = Mathf.Clamp(snow, 0f, 1f);
            Push();
        }

        public void Update(SprayKind kind, bool moving, bool washing, float delta)
        {
            if (washing)
            {
                var w = delta / WashSeconds;
                _dust = Mathf.Max(0f, _dust - w);
                _mud = Mathf.Max(0f, _mud - w);
                _snow = Mathf.Max(0f, _snow - w);
            }
            else
            {
                var drive = moving ? delta : 0f;
                switch (kind)
                {
                    case SprayKind.Dust:
                        _dust += drive / DustSeconds;
                        break;
                    case SprayKind.Grit:
                        _dust += drive / DustSeconds * 0.7f;
                        break;
                    case SprayKind.Dirt:
                        _dust += drive / DustSeconds * 0.5f;
                        _mud += drive / MudSeconds * 0.25f;
                        break;
                    case SprayKind.Mud:
                        _mud += drive / MudSeconds;
                        break;
                    case SprayKind.Water:
                        _mud += drive / MudSeconds * 0.6f;
                        _dust -= drive / DustSeconds * 2f; // splashing rinses the dust off
                        break;
                    case SprayKind.Snow:
                        _snow += drive / SnowSeconds;
                        _dust -= drive / DustSeconds;
                        break;
                    case SprayKind.RustDust:
                        _dust += drive / DustSeconds;
                        break;
                    case SprayKind.FrostGrit:
                        _snow += drive / SnowSeconds * 0.35f;
                        _dust += drive / DustSeconds * 0.3f;
                        break;
                }
                if (kind is not (SprayKind.Snow or SprayKind.FrostGrit)) _snow -= delta / 90f; // melts off away from the ice
                _dust -= delta / 600f;                             // a little shakes off over time
                if (moving && kind is SprayKind.Dust or SprayKind.Grit or SprayKind.Dirt or SprayKind.RustDust)
                    _dustColour = _dustColour.Lerp(DustOf(kind), Mathf.Min(1f, delta / 20f));
                _dust = Mathf.Clamp(_dust, 0f, 1f);
                _mud = Mathf.Clamp(_mud, 0f, 1f);
                _snow = Mathf.Clamp(_snow, 0f, 1f);
            }
            Push();
        }

        private void Push()
        {
            // The shader works in the model's own space so the dirt rides along with the vehicle.
            _material.SetShaderParameter("to_model", _model.IsInsideTree() ? _model.GlobalTransform.AffineInverse() : Transform3D.Identity);
            _material.SetShaderParameter("dust", _dust);
            _material.SetShaderParameter("mud", _mud);
            _material.SetShaderParameter("snow", _snow);
            _material.SetShaderParameter("wiped", Wiped);
            _material.SetShaderParameter("damage", Damage);
            _material.SetShaderParameter("dust_colour", _dustColour);
            // The painted names sit on the paint, so they get grubby too.
            var dirt = Mathf.Max(_dust * 0.6f, Mathf.Max(_mud * 0.8f, _snow * 0.9f));
            var tint = _snow > _mud && _snow > _dust ? new Color(0.92f, 0.94f, 0.97f) : _mud > _dust ? new Color(0.22f, 0.16f, 0.1f) : _dustColour;
            foreach (var d in _decals) d.Modulate = Ink.Lerp(tint, dirt * 0.7f);
        }

        private static Color DustOf(SprayKind kind) => kind switch
        {
            SprayKind.Grit => new Color(0.55f, 0.52f, 0.47f),
            SprayKind.Dirt => new Color(0.5f, 0.41f, 0.3f),
            SprayKind.RustDust => new Color(0.6f, 0.4f, 0.27f),
            _ => new Color(0.74f, 0.6f, 0.41f),
        };
    }
}
