using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using FinalFrontier.Sim.Math;

namespace FinalFrontier.Sim.Data;

/// <summary>
/// A number in a data file. Path is exact ("units/2/breakdown/chance_per_min": property names, array positions);
/// Label is for reading ("units › mining_crawler › breakdown › chance_per_min": list entries named by their id).
/// Value is the number's text as written in the file.
/// </summary>
public sealed record DataNumber(string File, string Path, string Label, string Value)
{
    /// <summary>Written without a decimal point: the loader may read it as a whole number, so edits must stay whole.</summary>
    public bool IsInteger => Value.IndexOf('.') < 0;
}

/// <summary>One number to change in a data file: Value is the new number's text (e.g. "0.5").</summary>
public sealed record DataEdit(string File, string Path, string Value);

/// <summary>The whole text of one data file (a reload from disk).</summary>
public sealed record DataFile(string File, string Text);

/// <summary>
/// Live tuning of the data files for the test panel. Edits replace one number token in the file's text, so the
/// hand-formatted layout survives (and "save to JSON" writes back exactly that change). <see cref="Rebuild"/> loads a
/// new <see cref="GameData"/> from the patched texts and refuses anything that wouldn't load, would add data-check
/// problems, or would change ids the running game refers to (resources, techs, terrain, buildings, units).
/// Only numbers can be edited, so the shape of the data never changes.
/// </summary>
public static class DataTuning
{
    /// <summary>Every number in every data file the sim reads (paths like "surfaces/units/mining_crawler.json"), file by file in load order.</summary>
    public static IReadOnlyList<DataNumber> Numbers(GameData data)
    {
        var list = new List<DataNumber>();
        foreach (var f in data.Files)
            if (data.Sources.TryGetValue(f, out var text)) list.AddRange(Numbers(f, text));
        return list;
    }

    /// <summary>Every number in one file's text, in file order.</summary>
    public static IReadOnlyList<DataNumber> Numbers(string file, string text) =>
        Scan(Encoding.UTF8.GetBytes(text)).Select(n => new DataNumber(file, n.Path, n.Label, n.Raw)).ToList();

    /// <summary>The number at a path in the loaded data, or null.</summary>
    public static DataNumber? Find(GameData data, string file, string path) =>
        data.Sources.TryGetValue(file, out var text) ? Numbers(file, text).FirstOrDefault(n => n.Path == path) : null;

    /// <summary>Why <paramref name="value"/> can't replace the number <paramref name="original"/> (null = it can).</summary>
    public static string? ValueProblem(string original, string value)
    {
        var v = value.Trim();
        var digits = v.StartsWith('-') ? v[1..] : v;
        var dot = digits.IndexOf('.');
        var whole = dot < 0 ? digits : digits[..dot];
        var frac = dot < 0 ? "" : digits[(dot + 1)..];
        if (whole.Length == 0 || !whole.All(char.IsAsciiDigit) || (dot >= 0 && (frac.Length == 0 || !frac.All(char.IsAsciiDigit))))
            return "not a number (use digits, an optional minus sign and a decimal point)";
        if (original.IndexOf('.') < 0 && dot >= 0) return "this one must be a whole number";
        if (original.IndexOf('.') < 0)
            return long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && n is >= int.MinValue and <= int.MaxValue
                ? null : "too big";
        try
        {
            Fix64.Parse(v);
        }
        catch (Exception)
        {
            return "too big";
        }
        return null;
    }

    /// <summary>Replaces the number at <paramref name="path"/> with <paramref name="value"/>; returns why not (null = done).</summary>
    public static string? SetNumber(string text, string path, string value, out string patched)
    {
        patched = text;
        var bytes = Encoding.UTF8.GetBytes(text);
        List<Leaf> leaves;
        try
        {
            leaves = Scan(bytes);
        }
        catch (JsonException)
        {
            return "the file isn't valid JSON";
        }
        var leaf = leaves.FirstOrDefault(l => l.Path == path);
        if (leaf == null) return "no number at that path";
        var problem = ValueProblem(leaf.Raw, value);
        if (problem != null) return problem;
        var replacement = Encoding.UTF8.GetBytes(value.Trim());
        var result = new byte[bytes.Length - leaf.Length + replacement.Length];
        Array.Copy(bytes, 0, result, 0, leaf.Start);
        Array.Copy(replacement, 0, result, leaf.Start, replacement.Length);
        Array.Copy(bytes, leaf.Start + leaf.Length, result, leaf.Start + replacement.Length, bytes.Length - leaf.Start - leaf.Length);
        patched = Encoding.UTF8.GetString(result);
        return null;
    }

    /// <summary>
    /// The data with whole files replaced, then single numbers edited, loaded and checked. Returns why it was refused
    /// (null = <paramref name="result"/> is the new data).
    /// </summary>
    public static string? Rebuild(GameData data, IReadOnlyList<DataEdit> edits, IReadOnlyList<DataFile> files, out GameData? result)
    {
        result = null;
        if (edits.Count == 0 && files.Count == 0) return "nothing to change";
        var sources = new Dictionary<string, string>(data.Sources, StringComparer.Ordinal);
        foreach (var f in files)
        {
            if (!sources.ContainsKey(f.File)) return $"{f.File} isn't a data file the game reads";
            sources[f.File] = f.Text;
        }
        foreach (var e in edits)
        {
            if (!sources.TryGetValue(e.File, out var text)) return $"{e.File} isn't a data file the game reads";
            var problem = SetNumber(text, e.Path, e.Value, out var patched);
            if (problem != null) return $"{e.File} {e.Path}: {problem}";
            sources[e.File] = patched;
        }
        GameData next;
        try
        {
            next = GameData.Load(sources, data.Setup);
        }
        catch (Exception ex) when (ex is JsonException or System.IO.InvalidDataException or InvalidOperationException or FormatException or KeyNotFoundException or OverflowException or IndexOutOfRangeException or ArgumentException)
        {
            return $"the data no longer loads ({ex.GetType().Name})";
        }
        var incompatible = CompatibilityProblem(data, next);
        if (incompatible != null) return incompatible;
        var before = data.Validate();
        var added = next.Validate().FirstOrDefault(p => !before.Contains(p));
        if (added != null) return $"data check: {added}";
        result = next;
        return null;
    }

    /// <summary>Why the running game can't switch to <paramref name="next"/> (null = it can): ids it refers to must stay put.</summary>
    public static string? CompatibilityProblem(GameData current, GameData next)
    {
        if (!current.Resources.Select(r => r.Id).SequenceEqual(next.Resources.Select(r => r.Id))) return "the resource list changed (start a new game for that)";
        if (!current.Techs.Select(t => t.Id).SequenceEqual(next.Techs.Select(t => t.Id))) return "the research list changed (start a new game for that)";
        if (!current.Terrain.Select(t => t.Id).SequenceEqual(next.Terrain.Select(t => t.Id))) return "the terrain list changed (start a new game for that)";
        var buildings = next.Buildings.Select(b => b.Id).ToHashSet();
        var gone = current.Buildings.FirstOrDefault(b => !buildings.Contains(b.Id));
        if (gone != null) return $"building '{gone.Id}' is gone";
        var units = next.SurfaceUnits.Select(u => u.Id).ToHashSet();
        var goneUnit = current.SurfaceUnits.FirstOrDefault(u => !units.Contains(u.Id));
        if (goneUnit != null) return $"vehicle '{goneUnit.Id}' is gone";
        var surfaces = next.Surfaces.Select(s => s.ClassId).ToHashSet();
        var goneSurface = current.Surfaces.FirstOrDefault(s => !surfaces.Contains(s.ClassId));
        if (goneSurface != null) return $"planet surface '{goneSurface.ClassId}' is gone";
        return null;
    }

    // ---------------------------------------------------------------- scanning

    private sealed class Frame
    {
        public bool IsArray;
        public int Index;
        public string? Key;
        public string Segment = "";
        public string? Alias;
    }

    private sealed record Leaf(string Path, string Label, int Start, int Length, string Raw);

    /// <summary>Walks the JSON tokens, noting every number with its path, label and byte position.</summary>
    private static List<Leaf> Scan(byte[] bytes)
    {
        var found = new List<(Frame[] Frames, string Segment, int Start, int Length, string Raw)>();
        var stack = new List<Frame>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    stack[^1].Key = reader.GetString();
                    break;
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    stack.Add(new Frame { IsArray = reader.TokenType == JsonTokenType.StartArray, Segment = stack.Count == 0 ? "" : SegmentIn(stack[^1]) });
                    break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    stack.RemoveAt(stack.Count - 1);
                    if (stack.Count > 0) Advance(stack[^1]);
                    break;
                default:
                    if (stack.Count == 0) break; // a bare value at the root
                    var top = stack[^1];
                    var segment = SegmentIn(top);
                    if (reader.TokenType == JsonTokenType.Number && !segment.Contains('/'))
                        found.Add((stack.ToArray(), segment, (int)reader.TokenStartIndex, reader.ValueSpan.Length, Encoding.UTF8.GetString(reader.ValueSpan)));
                    // List entries are named by their id (or a planet by its class) in labels.
                    if (!top.IsArray && reader.TokenType == JsonTokenType.String && (top.Key == "id" || (top.Key == "class" && top.Alias == null)))
                        top.Alias = reader.GetString();
                    Advance(top);
                    break;
            }
        }
        return found
            .Where(f => f.Frames.Skip(1).All(x => !x.Segment.Contains('/')))
            .Select(f => new Leaf(
                string.Join("/", f.Frames.Skip(1).Select(x => x.Segment).Append(f.Segment)),
                string.Join(" › ", f.Frames.Skip(1).Select(x => x.Alias ?? x.Segment).Append(f.Segment)),
                f.Start, f.Length, f.Raw))
            .ToList();
    }

    private static string SegmentIn(Frame f) => f.IsArray ? f.Index.ToString(CultureInfo.InvariantCulture) : f.Key ?? "";

    private static void Advance(Frame f)
    {
        if (f.IsArray) f.Index++;
    }
}
