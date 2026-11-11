using System;
using Godot;

namespace FinalFrontier;

// Visual pass G2 (docs/visual-pass-contract.md): high-detail ground near the camera, and the glue that switches the
// pass's surface features with GraphicsQuality (Terrain here and on the vehicles' grime, Decals in
// PlanetView.Decals.cs, Effects in PlanetView.Effects.cs). Render-only.
//
// Terrain: game/textures/terrain/<kind>_{albedo,normal,rough}.jpg (blender/bake_terrain_textures.py; behind a
// .gdignore, read as plain files like textures/planet) go into two Texture2DArrays, loaded once per run on the first
// switch-on: albedo + roughness in alpha (sRGB colour), and normals (renormalised mipmaps). Each layer's linear mean
// colour goes to the shader so it uses the texture only as variation round the tile colour. det_kind_tex = one texel
// per tile holding its layer (by the PlanetLook role; class L gets tundra / lichen / basalt). With the flag off
// det_on = 0, the shader's old path, untouched.
public partial class PlanetView
{
    /// <summary>The detail layers, in the shader's order (terrain.gdshader).</summary>
    private static readonly string[] DetailKinds = { "grass", "forest_floor", "dirt", "sand", "rock", "snow", "mud", "tundra", "lichen", "basalt" };

    private const int LayerGrass = 0, LayerForest = 1, LayerDirt = 2, LayerSand = 3, LayerRock = 4, LayerSnow = 5,
        LayerTundra = 7, LayerLichen = 8, LayerBasalt = 9;

    // Shared by every PlanetView of the run (a reload builds a new view; the textures stay).
    private static Texture2DArray? _detailAlbedo, _detailNormal;
    private static Vector3[]? _detailMean;
    private static bool _detailTried, _detailMissing;

    private ShaderMaterial? _terrainMaterial;
    private MeshInstance3D? _terrainMesh;
    private ImageTexture? _detailKindTexture;
    private bool _visualPassWired;
    private bool _terrainDetailOn;

    /// <summary>Once, at the end of <see cref="Build"/>: find the terrain, wire GraphicsQuality.Changed and apply the
    /// current switches (Terrain, Decals, Effects).</summary>
    private void SetupVisualPass()
    {
        // The terrain is the first fog material's mesh (BuildTerrain makes it first).
        if (_fogMaterials.Count > 0) _terrainMaterial = _fogMaterials[0];
        foreach (var child in GetChildren())
            if (child is MeshInstance3D mi && mi.MaterialOverride == _terrainMaterial)
            {
                _terrainMesh = mi;
                break;
            }
        if (_terrainMesh != null) _terrainMesh.Layers |= DecalGroundLayer; // PlanetView.Decals.cs: what the ground decals land on
        if (!_visualPassWired)
        {
            _visualPassWired = true;
            GraphicsQuality.Changed += OnGraphicsChanged;
            TreeExiting += () => GraphicsQuality.Changed -= OnGraphicsChanged;
            TreeEntered += () =>
            {
                GraphicsQuality.Changed -= OnGraphicsChanged; // never twice
                GraphicsQuality.Changed += OnGraphicsChanged;
                OnGraphicsChanged();
            };
        }
        OnGraphicsChanged();
    }

    /// <summary>GraphicsQuality changed (settings, preset, the F7 compare): re-apply everything this pass owns.</summary>
    private void OnGraphicsChanged()
    {
        if (!IsInstanceValid(this) || _surface == null) return;
        ApplyTerrainDetail(GraphicsQuality.Terrain);
        ApplyGrimeDetail(GraphicsQuality.Terrain);
        ApplyDecals(GraphicsQuality.Decals);   // PlanetView.Decals.cs
        ApplyEffects(GraphicsQuality.Effects); // PlanetView.Effects.cs
    }

    private void ApplyGrimeDetail(bool on)
    {
        RoverLook.Grime.Detail = on;
        foreach (var grime in _grime.Values) grime.SetDetail(on);
    }

    private void ApplyTerrainDetail(bool on)
    {
        if (_terrainMaterial == null) return;
        if (on && !EnsureDetailTextures()) on = false;
        if (on == _terrainDetailOn) return;
        _terrainDetailOn = on;
        if (on)
        {
            _detailKindTexture ??= DetailKindTexture();
            _terrainMaterial.SetShaderParameter("det_albedo", _detailAlbedo!);
            _terrainMaterial.SetShaderParameter("det_normal", _detailNormal!);
            _terrainMaterial.SetShaderParameter("det_mean", _detailMean!);
            _terrainMaterial.SetShaderParameter("det_kind_tex", _detailKindTexture);
        }
        _terrainMaterial.SetShaderParameter("det_on", on ? 1f : 0f);
        _detailRockCount = 0; // UpdateVisualPass (re)does the scatter rocks
        ApplyRockDetail();
    }

    private int _detailRockCount;
    private static Shader? _rockShader;

    /// <summary>The scatter rocks (rock.gdshader materials among the fog materials, made lazily by the scatter) get the
    /// rock/basalt layer while terrain detail is on; ice keeps its own look.</summary>
    private void ApplyRockDetail()
    {
        if (_detailRockCount == _fogMaterials.Count) return;
        _rockShader ??= GD.Load<Shader>("res://shaders/rock.gdshader");
        var layer = Look.ClassId == "class_l" ? LayerBasalt : LayerRock;
        for (var i = _detailRockCount; i < _fogMaterials.Count; i++)
        {
            var m = _fogMaterials[i];
            if (m.Shader != _rockShader) continue;
            var rough = m.GetShaderParameter("roughness"); // Nil = the shader's default 0.85; ice is glassy (0.22)
            var on = _terrainDetailOn && (rough.VariantType == Variant.Type.Nil || rough.AsSingle() > 0.4f);
            if (on)
            {
                m.SetShaderParameter("det_albedo", _detailAlbedo!);
                m.SetShaderParameter("det_normal", _detailNormal!);
                m.SetShaderParameter("det_layer", (float)layer);
                m.SetShaderParameter("det_mean_colour", _detailMean![layer]);
            }
            m.SetShaderParameter("det_on", on ? 1f : 0f);
        }
        _detailRockCount = _fogMaterials.Count;
    }

    /// <summary>Once a frame (Refresh, last): the visual pass's per-frame work.</summary>
    private void UpdateVisualPass(float delta)
    {
        if (_detailRockCount != _fogMaterials.Count) ApplyRockDetail();
        UpdateDecals(delta);  // PlanetView.Decals.cs
        UpdateEffects(delta); // PlanetView.Effects.cs
    }

    /// <summary>The detail layer each terrain id uses on this world.</summary>
    private int DetailLayer(int tile)
    {
        var map = _surface.Map;
        var role = Look.Role(map.Terrain(tile).Id);
        var classL = Look.ClassId == "class_l";
        if (map.IsWater(tile)) return LayerSand; // seabed seen through the shallows
        return role switch
        {
            "plains" => classL ? LayerTundra : LayerGrass,
            "forest" => classL ? LayerLichen : LayerForest,
            "hills" => LayerDirt,
            "desert" => LayerSand,
            "mountains" => classL ? LayerBasalt : LayerRock,
            "ice" => LayerSnow,
            _ => LayerDirt,
        };
    }

    private ImageTexture DetailKindTexture()
    {
        var grid = _surface.Map.Grid;
        var data = new byte[grid.Width * grid.Height];
        for (var t = 0; t < grid.Count; t++) data[grid.Index(grid.Column(t), grid.Row(t))] = (byte)DetailLayer(t);
        return ImageTexture.CreateFromImage(Image.CreateFromData(grid.Width, grid.Height, false, Image.Format.R8, data));
    }

    /// <summary>Load the ten layers (once per run). False if none could be read (then the toggle leaves the ground as
    /// before).</summary>
    private static bool EnsureDetailTextures()
    {
        if (_detailTried) return !_detailMissing;
        _detailTried = true;
        var size = 0;
        var albedos = new Godot.Collections.Array<Image>();
        var normals = new Godot.Collections.Array<Image>();
        _detailMean = new Vector3[DetailKinds.Length];
        var found = 0;
        var lut = new float[256];
        for (var i = 0; i < 256; i++)
        {
            var c = i / 255f;
            lut[i] = c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        }
        for (var k = 0; k < DetailKinds.Length; k++)
        {
            var dir = "res://textures/terrain/" + DetailKinds[k];
            var albedo = LoadPlain(dir + "_albedo.jpg");
            var rough = LoadPlain(dir + "_rough.jpg");
            var normal = LoadPlain(dir + "_normal.jpg");
            if (albedo != null) found++;
            if (size == 0 && albedo != null) size = albedo.GetWidth();
            if (size == 0) size = 1024;
            albedo ??= Flat(size, new Color(0.5f, 0.5f, 0.5f));
            rough ??= Flat(size, new Color(0.9f, 0.9f, 0.9f));
            normal ??= Flat(size, new Color(0.5f, 0.5f, 1f));
            foreach (var img in new[] { albedo, rough, normal })
                if (img.GetWidth() != size || img.GetHeight() != size) img.Resize(size, size, Image.Interpolation.Lanczos);
            albedo.Convert(Image.Format.Rgba8);
            rough.Convert(Image.Format.L8);
            normal.Convert(Image.Format.Rgba8);
            var a = albedo.GetData();
            var r = rough.GetData();
            double sr = 0, sg = 0, sb = 0;
            var n = 0;
            for (var p = 0; p < r.Length; p++)
            {
                a[p * 4 + 3] = r[p];
                if ((p & 3) != 0) continue; // every fourth pixel is plenty for the mean
                sr += lut[a[p * 4]];
                sg += lut[a[p * 4 + 1]];
                sb += lut[a[p * 4 + 2]];
                n++;
            }
            _detailMean[k] = new Vector3((float)(sr / n), (float)(sg / n), (float)(sb / n));
            var packed = Image.CreateFromData(size, size, false, Image.Format.Rgba8, a);
            packed.GenerateMipmaps();
            normal.GenerateMipmaps(true);
            albedos.Add(packed);
            normals.Add(normal);
        }
        if (found == 0)
        {
            GD.PushWarning("Terrain detail: no textures in res://textures/terrain (the ground stays as before)");
            _detailMissing = true;
            return false;
        }
        if (found < DetailKinds.Length) GD.PushWarning($"Terrain detail: {DetailKinds.Length - found} of the ground layers are missing in res://textures/terrain (flat there)");
        _detailAlbedo = new Texture2DArray();
        _detailAlbedo.CreateFromImages(albedos);
        _detailNormal = new Texture2DArray();
        _detailNormal.CreateFromImages(normals);
        return true;
    }

    /// <summary>An image read as a plain file (no import), or null.</summary>
    private static Image? LoadPlain(string path)
    {
        if (!Godot.FileAccess.FileExists(path)) return null;
        var image = Image.LoadFromFile(path);
        return image == null || image.IsEmpty() ? null : image;
    }

    private static Image Flat(int size, Color colour)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(colour);
        return image;
    }
}
