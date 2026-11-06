using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FinalFrontier.Sim.Data;

/// <summary>How a game's data was loaded: from where, and what was refused on the way (empty = all fine).</summary>
public sealed record DataLoadResult(GameData Data, string From, IReadOnlyList<string> Refused)
{
    public bool UsedFallback => Refused.Count > 0;
}

/// <summary>What Promote did: whether the draft went into the base files, why not, which files changed, where the backup is.</summary>
public sealed record PromoteResult(bool Promoted, string Message, IReadOnlyList<string> Problems, IReadOnlyList<string> ChangedFiles, string? BackupFolder);

/// <summary>
/// The data/ folder on disk: the base files (data/manifest.json lists them), the override layers (data/layers/), the
/// draft (data/_draft/draft.json: edits waiting to be checked, git-ignored) and backups (data/_backups/, git-ignored).
/// Edits go to the draft first; <see cref="Promote"/> checks the drafted data with <see cref="DataChecks"/> and only
/// then writes it into the base files (keeping their layout), after backing up what it replaces. Tool-side code (file
/// access, no game state); the time stamp comes from the caller.
/// </summary>
public static class DataStore
{
    public const string DraftFolder = "_draft";
    public const string BackupFolder = "_backups";
    public const string LastGoodPack = BackupFolder + "/last_good.pack";

    private const string DraftNotes = "Edits from the Test panel's DATA tab (or the Data Lab) that are not in the base files yet. The game loads them on top of data/ in debug builds; Promote (Test panel, or dotnet run --project tools/DataPacker -- promote) checks them and writes them into the base files. Git-ignored.";

    /// <summary>
    /// The base files: manifest.json, every file it lists, and every layer file, by path under data/ with '/'.
    /// Listed files that are missing and .json files nothing lists are added to <paramref name="problems"/>.
    /// </summary>
    public static Dictionary<string, string> ReadBase(string dataDirectory, List<string> problems)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var manifestText = File.ReadAllText(Path.Combine(dataDirectory, DataManifest.FileName));
        files[DataManifest.FileName] = manifestText;
        var manifest = DataManifest.Parse(manifestText);
        var listed = new HashSet<string>(StringComparer.Ordinal) { DataManifest.FileName };
        foreach (var doc in manifest.Documents)
            foreach (var part in manifest.PartsOf(doc))
            {
                listed.Add(part);
                var full = Path.Combine(dataDirectory, part);
                if (File.Exists(full)) files[part] = File.ReadAllText(full);
                else problems.Add($"data/{part} (listed in manifest.json) is missing.");
            }
        foreach (var full in Directory.EnumerateFiles(dataDirectory, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(dataDirectory, full).Replace('\\', '/');
            if (rel.StartsWith(DraftFolder + "/", StringComparison.Ordinal) || rel.StartsWith(BackupFolder + "/", StringComparison.Ordinal)) continue;
            if (rel.StartsWith(DataLayer.Folder + "/", StringComparison.Ordinal)) files[rel] = File.ReadAllText(full);
            else if (!listed.Contains(rel)) problems.Add($"data/{rel} isn't listed in data/manifest.json, so nothing reads it.");
        }
        return files;
    }

    public static string DraftFile(string dataDirectory) => Path.Combine(dataDirectory, DraftFolder, "draft.json");

    /// <summary>The draft's text, or null when there is no draft.</summary>
    public static string? ReadDraft(string dataDirectory)
    {
        var path = DraftFile(dataDirectory);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>A copy of the files with the draft's patches applied to their texts (problems: patches that don't fit).</summary>
    public static Dictionary<string, string> WithDraft(IReadOnlyDictionary<string, string> files, string draftText, List<string> problems)
    {
        var copy = new Dictionary<string, string>(files, StringComparer.Ordinal);
        DataLayer draft;
        try
        {
            draft = DataLayer.Parse(DataLayer.DraftPath, draftText);
        }
        catch (JsonException e)
        {
            problems.Add($"data/{DataLayer.DraftPath}: not valid JSON ({e.Message})");
            return copy;
        }
        DataPatcher.Apply(copy, DataManifest.Parse(files[DataManifest.FileName]), draft, problems);
        return copy;
    }

    /// <summary>The base files with the draft (if any) in: what a debug game loads.</summary>
    public static Dictionary<string, string> ReadEffective(string dataDirectory, List<string> problems)
    {
        var files = ReadBase(dataDirectory, problems);
        var draft = ReadDraft(dataDirectory);
        return draft == null ? files : WithDraft(files, draft, problems);
    }

    /// <summary>
    /// Loads the game's data from a data/ folder, refusing anything that fails <see cref="GameData.Validate"/>:
    /// first base + draft; if the draft doesn't fit or fails, the base alone; if the base fails too, the last good
    /// data (data/_backups/last_good.pack, written whenever a load passes); if there is none, the failing data with
    /// its problems in Refused. Refused explains every step that was skipped (empty = loaded as asked).
    /// </summary>
    public static DataLoadResult LoadForGame(string dataDirectory, DataSetup setup, bool useDraft = true)
    {
        var refused = new List<string>();
        var readProblems = new List<string>();
        Dictionary<string, string>? baseFiles = null;
        try
        {
            baseFiles = ReadBase(dataDirectory, readProblems);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            refused.Add($"data/ couldn't be read: {e.Message}");
        }

        GameData? TryLoad(IReadOnlyDictionary<string, string> files, string what, List<string> extra)
        {
            try
            {
                var data = GameData.Load(files, setup);
                var problems = extra.Concat(data.Validate()).Distinct().ToList();
                if (problems.Count == 0) return data;
                refused.Add($"{what} fails the data checks ({problems.Count}): {string.Join(" | ", problems.Take(5))}{(problems.Count > 5 ? " | …" : "")}");
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or IndexOutOfRangeException or ArgumentException or NullReferenceException)
            {
                refused.Add($"{what} doesn't load: {e.Message}");
            }
            return null;
        }

        GameData? firstFailure = null;
        if (baseFiles != null)
        {
            var draft = useDraft ? ReadDraft(dataDirectory) : null;
            if (draft != null)
            {
                var draftProblems = new List<string>();
                var drafted = WithDraft(baseFiles, draft, draftProblems);
                if (TryLoad(drafted, "data/ with the draft", readProblems.Concat(draftProblems).ToList()) is { } withDraft)
                    return Good(withDraft, "data/ + draft (data/_draft/draft.json)");
            }
            if (TryLoad(baseFiles, "data/", readProblems) is { } plain) return Good(plain, "data/");
            try
            {
                firstFailure = GameData.Load(baseFiles, setup);
            }
            catch (Exception)
            {
                firstFailure = null;
            }
        }

        var lastGood = Path.Combine(dataDirectory, LastGoodPack);
        if (File.Exists(lastGood))
        {
            try
            {
                var data = GameData.LoadPack(File.ReadAllBytes(lastGood), setup);
                return new DataLoadResult(data, $"the last good data ({LastGoodPack}, saved {File.GetLastWriteTime(lastGood):yyyy-MM-dd HH:mm})", refused);
            }
            catch (Exception e) when (e is InvalidDataException or JsonException or IOException or KeyNotFoundException)
            {
                refused.Add($"{LastGoodPack} doesn't load either: {e.Message}");
            }
        }
        if (firstFailure != null) return new DataLoadResult(firstFailure, "data/ (failing its checks: nothing better to fall back to)", refused);
        throw new InvalidDataException("No usable game data: " + string.Join(" | ", refused));

        DataLoadResult Good(GameData data, string from)
        {
            SaveLastGood(dataDirectory, data);
            return new DataLoadResult(data, from, refused);
        }
    }

    /// <summary>Keeps a pack of data that passed its checks, for <see cref="LoadForGame"/> to fall back to.</summary>
    public static void SaveLastGood(string dataDirectory, GameData data)
    {
        var path = Path.Combine(dataDirectory, LastGoodPack);
        try
        {
            if (File.Exists(path))
            {
                DataPack.Read(File.ReadAllBytes(path), out var fingerprint);
                if (fingerprint == data.Fingerprint) return;
            }
        }
        catch (InvalidDataException)
        {
            // a damaged copy gets replaced
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, DataPack.Write(data.Sources));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a read-only checkout just has no fallback copy
        }
    }

    // ---------------------------------------------------------------- the draft

    /// <summary>
    /// The draft patches that turn the base files into <paramref name="edited"/> (texts by path): one "set" per number
    /// that differs, by id path. Only numbers are compared (what the DATA tab edits).
    /// </summary>
    public static List<DataPatch> NumberPatches(IReadOnlyDictionary<string, string> baseFiles, IReadOnlyDictionary<string, string> edited)
    {
        var manifest = DataManifest.Parse(baseFiles[DataManifest.FileName]);
        var patches = new List<DataPatch>();
        foreach (var (path, text) in edited.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (!baseFiles.TryGetValue(path, out var before) || before == text) continue;
            var doc = manifest.DocumentOf(path);
            if (doc == null) continue;
            var oldDoc = DataDocument.Parse(before);
            var newDoc = DataDocument.Parse(text);
            var old = DataTuning.Numbers(path, before).ToDictionary(n => oldDoc.IdPath(n.Path), n => n.Value, StringComparer.Ordinal);
            foreach (var n in DataTuning.Numbers(path, text))
            {
                var idPath = newDoc.IdPath(n.Path);
                if (old.TryGetValue(idPath, out var was) && was != n.Value)
                    patches.Add(new DataPatch("set", doc, idPath, JsonDocument.Parse(n.Value).RootElement.Clone()));
            }
        }
        return patches;
    }

    /// <summary>
    /// Writes the draft so that loading base + draft gives <paramref name="edited"/>'s numbers: the number edits are
    /// recomputed against the base files, other patches already in the draft are kept. No patches left = no draft.
    /// Returns the number of patches in the draft.
    /// </summary>
    public static int SaveDraft(string dataDirectory, IReadOnlyDictionary<string, string> edited)
    {
        var problems = new List<string>();
        var baseFiles = ReadBase(dataDirectory, problems);
        var manifest = DataManifest.Parse(baseFiles[DataManifest.FileName]);
        var patches = new List<DataPatch>();
        if (ReadDraft(dataDirectory) is { } old)
            foreach (var p in DataLayer.Parse(DataLayer.DraftPath, old).Patches)
                if (!(p.Op == "set" && IsNumber(baseFiles, manifest, p))) patches.Add(p);
        patches.AddRange(NumberPatches(baseFiles, edited));
        var path = DraftFile(dataDirectory);
        if (patches.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return 0;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, DataLayer.Write(DraftNotes, patches));
        return patches.Count;
    }

    private static bool IsNumber(IReadOnlyDictionary<string, string> files, DataManifest manifest, DataPatch p)
    {
        if (!manifest.HasDocument(p.Doc)) return false;
        foreach (var part in manifest.PartsOf(p.Doc))
            if (files.TryGetValue(part, out var text) && DataDocument.Parse(text).Find(p.Path, out _, out _) is { Kind: JsonKind.Number })
                return true;
        return false;
    }

    /// <summary>Throws the draft away (backed up first, in case).</summary>
    public static void DiscardDraft(string dataDirectory, string stamp)
    {
        var path = DraftFile(dataDirectory);
        if (!File.Exists(path)) return;
        var backup = Path.Combine(dataDirectory, BackupFolder, stamp + "-discarded");
        Directory.CreateDirectory(backup);
        File.Copy(path, Path.Combine(backup, "draft.json"), true);
        File.Delete(path);
    }

    /// <summary>
    /// Promote: applies the draft to the base files, runs <see cref="DataChecks.All"/> on the result and, only if it
    /// passes, writes the changed base files (with the draft's edits made in place, so their layout stays), after
    /// copying the files it replaces and the draft to data/_backups/&lt;stamp&gt;/. The draft is then removed.
    /// A draft that doesn't fit or fails a check never touches the base files.
    /// </summary>
    public static PromoteResult Promote(string dataDirectory, string stamp)
    {
        var draftText = ReadDraft(dataDirectory);
        if (draftText == null) return new PromoteResult(false, "No draft to promote (data/_draft/draft.json).", Array.Empty<string>(), Array.Empty<string>(), null);
        var problems = new List<string>();
        var baseFiles = ReadBase(dataDirectory, problems);
        if (problems.Count > 0) return Refuse("data/ itself has problems", problems);
        var promoted = WithDraft(baseFiles, draftText, problems);
        if (problems.Count > 0) return Refuse("the draft doesn't fit the base files", problems);
        GameData data;
        try
        {
            data = GameData.Load(promoted);
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or IndexOutOfRangeException or ArgumentException or NullReferenceException)
        {
            return Refuse("the drafted data doesn't load", new[] { e.Message });
        }
        var checks = DataChecks.All(data);
        if (checks.Count > 0) return Refuse("the drafted data fails the data checks", checks);

        var changed = promoted.Where(f => !baseFiles.TryGetValue(f.Key, out var t) || t != f.Value).Select(f => f.Key).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var backup = Path.Combine(dataDirectory, BackupFolder, stamp);
        Directory.CreateDirectory(backup);
        foreach (var f in changed)
        {
            var target = Path.Combine(backup, f);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, baseFiles[f]);
        }
        Directory.CreateDirectory(Path.Combine(backup, DraftFolder));
        File.WriteAllText(Path.Combine(backup, DataLayer.DraftPath), draftText);
        foreach (var f in changed) File.WriteAllText(Path.Combine(dataDirectory, f), promoted[f]);
        File.Delete(DraftFile(dataDirectory));
        SaveLastGood(dataDirectory, data);
        var rel = $"data/{BackupFolder}/{stamp}/";
        return new PromoteResult(true, changed.Count == 0
            ? $"The draft changed nothing; removed it (copy in {rel})."
            : $"Promoted the draft into {changed.Count} file(s); the old versions are in {rel}.", Array.Empty<string>(), changed, rel);

        PromoteResult Refuse(string why, IReadOnlyList<string> list) =>
            new(false, $"Not promoted: {why}. The base files are unchanged.", list, Array.Empty<string>(), null);
    }
}
