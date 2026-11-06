using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FinalFrontier.Sim.Data;

public enum JsonKind { Object, Array, String, Number, True, False, Null }

/// <summary>A value in a data file's text: its kind, where it starts and ends, and its members or items.</summary>
public sealed class JsonSpan
{
    public JsonKind Kind { get; init; }
    public int Start { get; init; }
    public int End { get; internal set; }

    /// <summary>Object members in file order: key, where the key's opening quote is, and the value.</summary>
    public List<(string Key, int KeyStart, JsonSpan Value)> Members { get; } = new();
    public List<JsonSpan> Items { get; } = new();

    /// <summary>The decoded text of a string value.</summary>
    public string? Text { get; init; }

    public JsonSpan? Member(string key)
    {
        foreach (var m in Members)
            if (m.Key == key) return m.Value;
        return null;
    }

    /// <summary>What a record in a list is called in an id path: its "id", else its "class", else its "name".</summary>
    public string? Ident
    {
        get
        {
            if (Kind != JsonKind.Object) return null;
            foreach (var key in IdentKeys)
                if (Member(key) is { Kind: JsonKind.String } v) return v.Text;
            return null;
        }
    }

    internal static readonly string[] IdentKeys = { "id", "class", "name" };
}

/// <summary>
/// One data file's text with the position of every value (the Data Lab's "phase 0" groundwork). Edits replace or
/// insert text at those positions, so everything else in the file keeps its hand formatting. Paths address values by
/// id: "units/mining_crawler/breakdown/chance_per_min" (object keys; a record in a list by its id, class or name;
/// a plain list item by its position).
/// </summary>
public sealed class DataDocument
{
    public string Text { get; }
    public JsonSpan Root { get; }

    private DataDocument(string text, JsonSpan root)
    {
        Text = text;
        Root = root;
    }

    /// <summary>Parses a file's text; throws <see cref="FormatException"/> (with the line) if it isn't JSON.</summary>
    public static DataDocument Parse(string text)
    {
        var p = new Parser(text);
        var root = p.Value();
        p.SkipSpace();
        if (p.Pos < text.Length) throw p.Error("text after the end");
        return new DataDocument(text, root);
    }

    public static string[] Split(string path) => path.Length == 0 ? Array.Empty<string>() : path.Split('/');

    /// <summary>The value at a path (and the container it is in), or null with why not.</summary>
    public JsonSpan? Find(string path, out JsonSpan? parent, out string? problem) => Find(Split(path), out parent, out problem);

    public JsonSpan? Find(IReadOnlyList<string> segments, out JsonSpan? parent, out string? problem)
    {
        parent = null;
        problem = null;
        var node = Root;
        for (var i = 0; i < segments.Count; i++)
        {
            parent = node;
            var next = Child(node, segments[i], out problem);
            if (next == null)
            {
                problem = $"'{string.Join("/", segments.Take(i + 1))}' {problem}";
                return null;
            }
            node = next;
        }
        return node;
    }

    /// <summary>The child of an object by key, or of a list by record id / class / name, else by position.</summary>
    public static JsonSpan? Child(JsonSpan node, string segment, out string? problem)
    {
        problem = null;
        if (node.Kind == JsonKind.Object)
        {
            var v = node.Member(segment);
            if (v == null) problem = "doesn't exist";
            return v;
        }
        if (node.Kind == JsonKind.Array)
        {
            foreach (var key in JsonSpan.IdentKeys)
            {
                var hits = node.Items.Where(it => it.Kind == JsonKind.Object && it.Member(key) is { Kind: JsonKind.String } s && s.Text == segment).ToList();
                if (hits.Count == 1) return hits[0];
                if (hits.Count > 1)
                {
                    problem = $"matches {hits.Count} records";
                    return null;
                }
            }
            if (segment.Length > 0 && segment.All(char.IsAsciiDigit) && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                if (index < node.Items.Count) return node.Items[index];
                problem = "is past the end of the list";
                return null;
            }
            problem = "isn't in the list";
            return null;
        }
        problem = "is inside a plain value";
        return null;
    }

    /// <summary>The id path of the value at an exact path of keys and list positions ("units/4/speed" → "units/mining_crawler/speed").</summary>
    public string IdPath(string indexPath)
    {
        var node = Root;
        var outSegs = new List<string>();
        foreach (var seg in Split(indexPath))
        {
            if (node.Kind == JsonKind.Array)
            {
                var item = node.Items[int.Parse(seg, CultureInfo.InvariantCulture)];
                var ident = item.Ident;
                // Only a name that picks this record (Child resolves it back the same way) replaces the position.
                outSegs.Add(ident != null && ReferenceEquals(Child(node, ident, out _), item) ? ident : seg);
                node = item;
            }
            else
            {
                outSegs.Add(seg);
                node = node.Member(seg) ?? throw new KeyNotFoundException(indexPath);
            }
        }
        return string.Join("/", outSegs);
    }

    // ---------------------------------------------------------------- edits (each returns the new text)

    public string Replace(JsonSpan node, string valueText) => Text[..node.Start] + valueText + Text[node.End..];

    /// <summary>Removes an object member or list item with its comma.</summary>
    public string Remove(JsonSpan container, JsonSpan child)
    {
        int index, count;
        int StartOf(int i) => container.Kind == JsonKind.Object ? container.Members[i].KeyStart : container.Items[i].Start;
        int EndOf(int i) => container.Kind == JsonKind.Object ? container.Members[i].Value.End : container.Items[i].End;
        if (container.Kind == JsonKind.Object)
        {
            index = container.Members.FindIndex(m => ReferenceEquals(m.Value, child));
            count = container.Members.Count;
        }
        else
        {
            index = container.Items.FindIndex(it => ReferenceEquals(it, child));
            count = container.Items.Count;
        }
        if (index < 0) throw new ArgumentException("not a child of that container");
        if (index < count - 1) return Text[..StartOf(index)] + Text[StartOf(index + 1)..];
        if (index > 0) return Text[..EndOf(index - 1)] + Text[EndOf(index)..];
        return Text[..(container.Start + 1)] + Text[(container.End - 1)..];
    }

    /// <summary>Adds a member at the end of an object, laid out like the members before it.</summary>
    public string AddMember(JsonSpan obj, string key, string valueText) =>
        Insert(obj, obj.Members.Count - 1, JsonSerializer.Serialize(key) + ": " + valueText);

    /// <summary>Adds an item to a list after item <paramref name="after"/> (-1 or the last = at the end), laid out like its neighbours.</summary>
    public string AddItem(JsonSpan list, string valueText, int after = -1) =>
        Insert(list, after < 0 ? list.Items.Count - 1 : after, valueText);

    private string Insert(JsonSpan container, int after, string entry)
    {
        var isObject = container.Kind == JsonKind.Object;
        var count = isObject ? container.Members.Count : container.Items.Count;
        int StartOf(int i) => isObject ? container.Members[i].KeyStart : container.Items[i].Start;
        int EndOf(int i) => isObject ? container.Members[i].Value.End : container.Items[i].End;
        if (count == 0) return Text[..(container.Start + 1)] + " " + entry + " " + Text[(container.End - 1)..];
        // The gap between two entries ("," + newline + indent, or ", "); with one entry, the gap after the bracket.
        string gap;
        if (count >= 2)
        {
            var a = after >= 1 ? after - 1 : 0;
            gap = Text[EndOf(a)..StartOf(a + 1)];
        }
        else
        {
            var lead = Text[(container.Start + 1)..StartOf(0)];
            gap = "," + (lead.Length == 0 ? " " : lead);
        }
        if (after < count - 1) return Text[..StartOf(after + 1)] + entry + gap + Text[StartOf(after + 1)..];
        return Text[..EndOf(count - 1)] + gap + entry + Text[EndOf(count - 1)..];
    }

    /// <summary>A value as one line in the files' hand style: { "id": "x", "cost": { "duranium": 5 } }, [1, 2]; numbers as written.</summary>
    public static string Format(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Any()
            ? "{ " + string.Join(", ", value.EnumerateObject().Select(p => JsonSerializer.Serialize(p.Name) + ": " + Format(p.Value))) + " }"
            : "{}",
        JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(Format)) + "]",
        JsonValueKind.String => JsonSerializer.Serialize(value.GetString(), FormatOptions),
        _ => value.GetRawText(),
    };

    private static readonly JsonSerializerOptions FormatOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // ---------------------------------------------------------------- parser

    private sealed class Parser
    {
        private readonly string _t;
        public int Pos;

        public Parser(string text) => _t = text;

        public FormatException Error(string what)
        {
            var line = 1 + _t.Take(System.Math.Min(Pos, _t.Length)).Count(c => c == '\n');
            return new FormatException($"not valid JSON at line {line}: {what}");
        }

        public void SkipSpace()
        {
            while (Pos < _t.Length && _t[Pos] is ' ' or '\t' or '\r' or '\n') Pos++;
        }

        private char Peek()
        {
            SkipSpace();
            if (Pos >= _t.Length) throw Error("unexpected end");
            return _t[Pos];
        }

        public JsonSpan Value()
        {
            var c = Peek();
            var start = Pos;
            switch (c)
            {
                case '{':
                {
                    var node = new JsonSpan { Kind = JsonKind.Object, Start = start };
                    Pos++;
                    if (Peek() == '}')
                    {
                        node.End = ++Pos;
                        return node;
                    }
                    while (true)
                    {
                        if (Peek() != '"') throw Error("expected a key");
                        var keyStart = Pos;
                        var key = ReadString();
                        if (Peek() != ':') throw Error("expected ':'");
                        Pos++;
                        node.Members.Add((key, keyStart, Value()));
                        var n = Peek();
                        Pos++;
                        if (n == ',') continue;
                        if (n != '}') throw Error("expected ',' or '}'");
                        node.End = Pos;
                        return node;
                    }
                }
                case '[':
                {
                    var node = new JsonSpan { Kind = JsonKind.Array, Start = start };
                    Pos++;
                    if (Peek() == ']')
                    {
                        node.End = ++Pos;
                        return node;
                    }
                    while (true)
                    {
                        node.Items.Add(Value());
                        var n = Peek();
                        Pos++;
                        if (n == ',') continue;
                        if (n != ']') throw Error("expected ',' or ']'");
                        node.End = Pos;
                        return node;
                    }
                }
                case '"':
                {
                    var text = ReadString();
                    return new JsonSpan { Kind = JsonKind.String, Start = start, End = Pos, Text = text };
                }
                default:
                {
                    while (Pos < _t.Length && _t[Pos] is not (',' or '}' or ']' or ' ' or '\t' or '\r' or '\n')) Pos++;
                    var word = _t[start..Pos];
                    var kind = word switch
                    {
                        "true" => JsonKind.True,
                        "false" => JsonKind.False,
                        "null" => JsonKind.Null,
                        _ => IsNumber(word) ? JsonKind.Number : throw Error($"'{word}' isn't a value"),
                    };
                    return new JsonSpan { Kind = kind, Start = start, End = Pos };
                }
            }
        }

        private static bool IsNumber(string w)
        {
            var bytes = Encoding.UTF8.GetBytes(w);
            var reader = new Utf8JsonReader(bytes);
            try
            {
                return reader.Read() && reader.TokenType == JsonTokenType.Number && reader.BytesConsumed == bytes.Length;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private string ReadString()
        {
            var start = Pos;
            Pos++;
            var plain = true;
            while (true)
            {
                if (Pos >= _t.Length) throw Error("unclosed string");
                var c = _t[Pos];
                if (c == '"') break;
                if (c == '\\')
                {
                    plain = false;
                    Pos++;
                }
                Pos++;
            }
            Pos++;
            return plain ? _t[(start + 1)..(Pos - 1)] : JsonSerializer.Deserialize<string>(_t[start..Pos])!;
        }
    }
}
