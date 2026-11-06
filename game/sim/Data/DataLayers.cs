using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FinalFrontier.Sim.Data;

/// <summary>
/// Which override layers a game uses on top of the base data, applied in this order: the era's
/// (layers/eras/&lt;era&gt;.json), each faction's in the order given (layers/factions/&lt;faction&gt;.json), then the
/// scenario's (layers/scenarios/&lt;id&gt;.json). A missing era or faction layer just means "no overrides"; a named
/// scenario must exist. <see cref="Base"/> = no layers.
/// </summary>
public sealed record DataSetup(string? Era = null, IReadOnlyList<string>? Factions = null, string? Scenario = null)
{
    public static readonly DataSetup Base = new();

    public IReadOnlyList<string> LayerPaths(IReadOnlyDictionary<string, string> files, List<string> problems)
    {
        var paths = new List<string>();
        void Add(string path, bool required)
        {
            if (files.ContainsKey(path)) paths.Add(path);
            else if (required) problems.Add($"data/{path}: no such scenario layer.");
        }
        if (Era != null) Add($"{DataLayer.Folder}/eras/{Era}.json", false);
        foreach (var f in Factions ?? Array.Empty<string>()) Add($"{DataLayer.Folder}/factions/{f}.json", false);
        if (Scenario != null) Add($"{DataLayer.Folder}/scenarios/{Scenario}.json", true);
        return paths;
    }

    public override string ToString() =>
        $"era {Era ?? "(base)"}, factions {(Factions is { Count: > 0 } f ? string.Join(", ", f) : "(none)")}, scenario {Scenario ?? "(none)"}";
}

/// <summary>
/// One change to the data, addressed by document and id path (see <see cref="DataDocument"/>):
/// set (replace an existing value), remove (a field or record), add_field (a new field in an object),
/// add_record (a new record in a list, at the end or after the record named in After), list_add / list_remove
/// (a plain value in a list such as build_menu).
/// </summary>
public sealed record DataPatch(string Op, string Doc, string Path, JsonElement? Value = null, string? After = null)
{
    public static readonly string[] Ops = { "set", "remove", "add_field", "add_record", "list_add", "list_remove" };

    public string Where => $"{Doc}:{Path}";

    /// <summary>The patch as one line of a layer file.</summary>
    public string ToJson()
    {
        var parts = new List<string> { $"\"op\": {JsonSerializer.Serialize(Op)}", $"\"doc\": {JsonSerializer.Serialize(Doc)}", $"\"path\": {JsonSerializer.Serialize(Path)}" };
        if (Value is { } v) parts.Add($"\"value\": {DataDocument.Format(v)}");
        if (After != null) parts.Add($"\"after\": {JsonSerializer.Serialize(After)}");
        return "{ " + string.Join(", ", parts) + " }";
    }
}

/// <summary>
/// An override file: { "_notes": "…", "patches": [ { "op": "set", "doc": "surfaces", "path": "orbit/era", "value": "ent" }, … ] }.
/// Layers live in data/layers/eras|factions|scenarios/; the Test panel's unpromoted edits are a layer too
/// (data/_draft/draft.json).
/// </summary>
public sealed record DataLayer(string Path, IReadOnlyList<DataPatch> Patches)
{
    public const string Folder = "layers";
    public const string DraftPath = "_draft/draft.json";

    public static DataLayer Parse(string path, string text)
    {
        using var doc = JsonDocument.Parse(text);
        var patches = new List<DataPatch>();
        if (doc.RootElement.TryGetProperty("patches", out var list))
            foreach (var p in list.EnumerateArray())
            {
                string? S(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                patches.Add(new DataPatch(S("op") ?? "", S("doc") ?? "", S("path") ?? "",
                    p.TryGetProperty("value", out var value) ? value.Clone() : null, S("after")));
            }
        return new DataLayer(path, patches);
    }

    /// <summary>A layer file's text, one patch per line.</summary>
    public static string Write(string notes, IEnumerable<DataPatch> patches)
    {
        var lines = patches.Select(p => "    " + p.ToJson()).ToList();
        return "{\n  \"_notes\": " + JsonSerializer.Serialize(notes) + ",\n  \"patches\": [\n" + string.Join(",\n", lines) + (lines.Count > 0 ? "\n" : "") + "  ]\n}\n";
    }
}

/// <summary>Applies patches to the data files' texts (keeping their layout) and checks layers against each other.</summary>
public static class DataPatcher
{
    /// <summary>
    /// Applies a layer's patches in order to the texts in <paramref name="files"/> (changed in place). A patch that
    /// doesn't fit (unknown document, path that doesn't exist, a record that's already there, …) is skipped and
    /// reported as "data/&lt;layer&gt;: …".
    /// </summary>
    public static void Apply(Dictionary<string, string> files, DataManifest manifest, DataLayer layer, List<string> problems)
    {
        foreach (var patch in layer.Patches)
        {
            var problem = Apply(files, manifest, patch);
            if (problem != null) problems.Add($"data/{layer.Path}: {patch.Op} {patch.Where}: {problem}");
        }
    }

    /// <summary>Applies one patch; returns why not (null = done).</summary>
    public static string? Apply(Dictionary<string, string> files, DataManifest manifest, DataPatch p)
    {
        if (!DataPatch.Ops.Contains(p.Op)) return $"unknown op (use {string.Join(", ", DataPatch.Ops)})";
        if (!manifest.HasDocument(p.Doc)) return "no such document in data/manifest.json";
        var needsValue = p.Op != "remove";
        if (needsValue && p.Value == null) return "needs a value";
        var segs = DataDocument.Split(p.Path);
        if (segs.Length == 0 && p.Op != "add_field") return "needs a path";
        var parts = manifest.PartsOf(p.Doc).Where(files.ContainsKey).ToList();
        var docs = new Dictionary<string, DataDocument>();
        foreach (var path in parts)
        {
            try
            {
                docs[path] = DataDocument.Parse(files[path]);
            }
            catch (FormatException e)
            {
                return $"data/{path} {e.Message}";
            }
        }

        // Where a path is: the files of the document it resolves in.
        List<(string Path, JsonSpan Node, JsonSpan? Parent)> Resolve(IReadOnlyList<string> s)
        {
            var hits = new List<(string, JsonSpan, JsonSpan?)>();
            foreach (var path in parts)
                if (docs[path].Find(s, out var parent, out _) is { } node) hits.Add((path, node, parent));
            return hits;
        }
        string Missing(IReadOnlyList<string> s)
        {
            string? problem = null;
            foreach (var path in parts)
            {
                docs[path].Find(s, out _, out var pr);
                if (problem == null || (pr != null && pr.Length > problem.Length)) problem = pr;
            }
            return problem ?? $"'{string.Join("/", s)}' doesn't exist";
        }

        switch (p.Op)
        {
            case "set":
            case "remove":
            {
                var hits = Resolve(segs);
                if (hits.Count == 0) return Missing(segs);
                if (hits.Count > 1)
                {
                    // A table split over several files: only whole tables can't be set or removed this way.
                    return $"is split over {string.Join(", ", hits.Select(h => h.Path))}; change its records instead";
                }
                var (path, node, parent) = hits[0];
                files[path] = p.Op == "set" ? docs[path].Replace(node, ValueText(p.Value!.Value)) : docs[path].Remove(parent!, node);
                return null;
            }
            case "add_field":
            {
                var parentSegs = segs[..^1];
                var key = segs.Length > 0 ? segs[^1] : "";
                if (key.Length == 0) return "needs the new field's name at the end of the path";
                if (Resolve(segs).Count > 0) return "already exists (use set)";
                var hits = Resolve(parentSegs);
                if (hits.Count == 0) return Missing(parentSegs);
                var (path, node, _) = hits[^1];
                if (node.Kind != JsonKind.Object) return $"'{string.Join("/", parentSegs)}' isn't an object";
                files[path] = docs[path].AddMember(node, key, ValueText(p.Value!.Value));
                return null;
            }
            case "add_record":
            {
                var v = p.Value!.Value;
                if (v.ValueKind != JsonValueKind.Object) return "the value must be a record (an object)";
                var ident = JsonSpan.IdentKeys.Select(k => v.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null).FirstOrDefault(x => x != null);
                if (ident == null) return "the record needs an id (or class / name)";
                var hits = Resolve(segs);
                if (hits.Count == 0) return Missing(segs);
                if (hits.Any(h => h.Node.Kind != JsonKind.Array)) return "isn't a list";
                if (Resolve(segs.Append(ident).ToList()).Count > 0) return $"already has a record '{ident}'";
                var target = hits[^1];
                var after = -1;
                if (p.After != null)
                {
                    var found = hits.Select(h => (h, i: h.Node.Items.FindIndex(it => it.Ident == p.After))).FirstOrDefault(x => x.i >= 0);
                    if (found.h.Path == null) return $"has no record '{p.After}' to add after";
                    target = found.h;
                    after = found.i;
                }
                files[target.Path] = docs[target.Path].AddItem(target.Node, ValueText(v), after);
                return null;
            }
            default: // list_add, list_remove
            {
                var hits = Resolve(segs);
                if (hits.Count == 0) return Missing(segs);
                if (hits.Any(h => h.Node.Kind != JsonKind.Array)) return "isn't a list";
                var want = Canonical(p.Value!.Value.GetRawText());
                var holders = hits.SelectMany(h => h.Node.Items.Where(it => Canonical(files[h.Path][it.Start..it.End]) == want).Select(it => (h, it))).ToList();
                if (p.Op == "list_add")
                {
                    if (holders.Count > 0) return "already has that value";
                    var (path, node, _) = hits[^1];
                    files[path] = docs[path].AddItem(node, ValueText(p.Value!.Value));
                    return null;
                }
                if (holders.Count == 0) return "doesn't have that value";
                var (holder, item) = holders[0];
                files[holder.Path] = docs[holder.Path].Remove(holder.Node, item);
                return null;
            }
        }
    }

    private static string ValueText(JsonElement v) => v.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.String ? DataDocument.Format(v) : v.GetRawText();

    private static string Canonical(string json) => System.Text.Encoding.UTF8.GetString(DataFingerprint.Canonical(json));

    /// <summary>
    /// Every layer file in a data set checked on its own against the base (each patch must fit), plus conflicts:
    /// two layers that can be active in the same game changing the same value (or one inside the other).
    /// Two era layers or two scenario layers never meet; anything else can.
    /// </summary>
    public static List<string> CheckLayers(IReadOnlyDictionary<string, string> files, DataManifest manifest)
    {
        var problems = new List<string>();
        var layers = new List<DataLayer>();
        foreach (var path in files.Keys.Where(p => p.StartsWith(DataLayer.Folder + "/", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal))
        {
            DataLayer layer;
            try
            {
                layer = DataLayer.Parse(path, files[path]);
            }
            catch (JsonException e)
            {
                problems.Add($"data/{path}: not valid JSON ({e.Message})");
                continue;
            }
            var kind = Kind(path);
            if (kind == null) problems.Add($"data/{path}: layers go in layers/eras/, layers/factions/ or layers/scenarios/.");
            var copy = new Dictionary<string, string>(files, StringComparer.Ordinal);
            Apply(copy, manifest, layer, problems);
            layers.Add(layer);
        }
        for (var i = 0; i < layers.Count; i++)
            for (var j = i + 1; j < layers.Count; j++)
            {
                var (a, b) = (layers[i], layers[j]);
                var (ka, kb) = (Kind(a.Path), Kind(b.Path));
                if (ka == kb && ka is "eras" or "scenarios") continue;
                foreach (var pa in a.Patches)
                    foreach (var pb in b.Patches.Where(pb => Overlaps(pa, pb)))
                        problems.Add($"Conflict: data/{a.Path} and data/{b.Path} both change {pb.Where} ({pa.Op} / {pb.Op}); " +
                                     "in a game with both, the later one (era → factions → scenario) wins.");
            }
        return problems;
    }

    /// <summary>"eras", "factions" or "scenarios" for a layer file, else null.</summary>
    public static string? Kind(string path)
    {
        var segs = path.Split('/');
        return segs.Length == 3 && segs[0] == DataLayer.Folder && segs[1] is "eras" or "factions" or "scenarios" ? segs[1] : null;
    }

    public static bool Overlaps(DataPatch a, DataPatch b)
    {
        if (a.Doc != b.Doc) return false;
        // Adding or removing different plain values in the same list doesn't clash (nor do different new records, below).
        if (a.Op is "list_add" or "list_remove" && b.Op is "list_add" or "list_remove")
            return a.Path == b.Path && Same(a.Value, b.Value);
        string Target(DataPatch p) => p.Op == "add_record" && p.Value is { } v ? p.Path + "/" + (JsonSpan.IdentKeys.Select(k => v.TryGetProperty(k, out var x) ? x.ToString() : null).FirstOrDefault(x => x != null) ?? "") : p.Path;
        var (ta, tb) = (Target(a), Target(b));
        return ta == tb || ta.StartsWith(tb + "/", StringComparison.Ordinal) || tb.StartsWith(ta + "/", StringComparison.Ordinal);
    }

    private static bool Same(JsonElement? a, JsonElement? b) =>
        a is { } x && b is { } y && Canonical(x.GetRawText()) == Canonical(y.GetRawText());
}
