using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FinalFrontier.Sim.Data;

/// <summary>
/// data/manifest.json: which files make up each data document, in order. A document's files are merged top-down: a
/// table that appears in several files is joined (lists one after the other, objects key by key, recursively); the
/// same key with two plain values is a problem (the first one wins).
/// </summary>
public sealed class DataManifest
{
    public const string FileName = "manifest.json";

    public IReadOnlyList<string> Documents { get; }
    private readonly Dictionary<string, IReadOnlyList<string>> _parts;

    private DataManifest(List<string> documents, Dictionary<string, IReadOnlyList<string>> parts)
    {
        Documents = documents;
        _parts = parts;
    }

    public static DataManifest Parse(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var documents = new List<string>();
        var parts = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var d in doc.RootElement.GetProperty("documents").EnumerateObject())
        {
            documents.Add(d.Name);
            parts[d.Name] = d.Value.EnumerateArray().Select(p => p.GetString()!).ToList();
        }
        return new DataManifest(documents, parts);
    }

    public bool HasDocument(string doc) => _parts.ContainsKey(doc);

    public IReadOnlyList<string> PartsOf(string doc) =>
        _parts.TryGetValue(doc, out var p) ? p : throw new KeyNotFoundException($"data/manifest.json has no document '{doc}'.");

    /// <summary>The document a file belongs to, or null.</summary>
    public string? DocumentOf(string path) => Documents.FirstOrDefault(d => _parts[d].Contains(path));

    /// <summary>Merges a document's files (texts by path) into one JSON value.</summary>
    public JsonElement Assemble(IReadOnlyDictionary<string, string> files, string doc, List<string> problems)
    {
        var roots = new List<(string Path, JsonElement Root)>();
        foreach (var path in PartsOf(doc))
        {
            if (!files.TryGetValue(path, out var text))
            {
                problems.Add($"data/{path} (listed in manifest.json) is missing.");
                continue;
            }
            try
            {
                using var parsed = JsonDocument.Parse(text);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object) problems.Add($"data/{path}: must be a JSON object.");
                else roots.Add((path, parsed.RootElement.Clone()));
            }
            catch (JsonException e)
            {
                problems.Add($"data/{path}: not valid JSON ({e.Message})");
            }
        }
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
            WriteMerged(w, roots, doc, problems);
        using var merged = JsonDocument.Parse(ms.ToArray());
        return merged.RootElement.Clone();
    }

    private static void WriteMerged(Utf8JsonWriter w, List<(string Path, JsonElement Value)> objects, string where, List<string> problems)
    {
        w.WriteStartObject();
        var keys = new List<string>();
        foreach (var (_, o) in objects)
            foreach (var p in o.EnumerateObject())
                if (!keys.Contains(p.Name)) keys.Add(p.Name);
        foreach (var key in keys)
        {
            var values = new List<(string Path, JsonElement Value)>();
            foreach (var (path, o) in objects)
                if (o.TryGetProperty(key, out var v)) values.Add((path, v));
            w.WritePropertyName(key);
            if (values.Count > 1 && values.All(v => v.Value.ValueKind == JsonValueKind.Object))
                WriteMerged(w, values, $"{where}/{key}", problems);
            else if (values.Count > 1 && values.All(v => v.Value.ValueKind == JsonValueKind.Array))
            {
                w.WriteStartArray();
                foreach (var (_, v) in values)
                    foreach (var item in v.EnumerateArray()) item.WriteTo(w);
                w.WriteEndArray();
            }
            else
            {
                if (values.Count > 1)
                    problems.Add($"data: '{where}/{key}' is given in both {values[0].Path} and {values[1].Path} (the first one is used).");
                values[0].Value.WriteTo(w);
            }
        }
        w.WriteEndObject();
    }
}

/// <summary>
/// A hash of a data set that ignores layout: SHA-256 over every file's path and its compact JSON (number text as
/// written), sorted by path. The same data loaded from the folders or from the release pack gives the same
/// fingerprint, so a multiplayer lobby can compare it (whitespace and Windows line endings don't count).
/// </summary>
public static class DataFingerprint
{
    public static string Of(IReadOnlyDictionary<string, string> files)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in files.Keys.OrderBy(p => p, StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(path));
            sha.AppendData(new byte[] { 0 });
            sha.AppendData(Canonical(files[path]));
            sha.AppendData(new byte[] { 0 });
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Compact JSON of a file (or its raw bytes when it isn't JSON, so a broken file still changes the hash).</summary>
    public static byte[] Canonical(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms)) doc.RootElement.WriteTo(w);
            return ms.ToArray();
        }
        catch (JsonException)
        {
            return Encoding.UTF8.GetBytes(text);
        }
    }
}
