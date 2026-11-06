using System.Globalization;
using FinalFrontier.Sim.Data;

// The data tool. Run from the repo root:
//   dotnet run --project tools/DataPacker -- --out game/gamedata.pack   check the base data, then pack it for release
//   dotnet run --project tools/DataPacker -- check [--draft]             run every data check (base, or base + draft)
//   dotnet run --project tools/DataPacker -- promote                     draft → base files (checked, backed up)
//   dotnet run --project tools/DataPacker -- fingerprint [--pack FILE]   the data fingerprint (folders, or a pack)
// Options: --data DIR (default: the data/ folder found above the current directory).

var verb = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "pack";
string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
bool Flag(string name) => args.Contains(name);

string dataDir;
try
{
    dataDir = Option("--data") ?? GameData.FindDataDirectory(Directory.GetCurrentDirectory());
}
catch (DirectoryNotFoundException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

int Report(IReadOnlyList<string> problems, string what)
{
    if (problems.Count == 0)
    {
        Console.WriteLine($"{what}: all data checks pass.");
        return 0;
    }
    Console.Error.WriteLine($"{what}: {problems.Count} problem(s):");
    foreach (var p in problems) Console.Error.WriteLine("  - " + p);
    return 1;
}

GameData LoadChecked(bool withDraft, List<string> problems)
{
    var files = withDraft ? DataStore.ReadEffective(dataDir, problems) : DataStore.ReadBase(dataDir, problems);
    var data = GameData.Load(files);
    problems.AddRange(DataChecks.All(data).Where(p => !problems.Contains(p)));
    return data;
}

switch (verb)
{
    case "pack":
    {
        var outPath = Option("--out") ?? Path.Combine(Path.GetDirectoryName(dataDir)!, "game", "gamedata.pack");
        var problems = new List<string>();
        var data = LoadChecked(false, problems);
        if (Report(problems, "data/") != 0)
        {
            Console.Error.WriteLine("Not packed.");
            return 1;
        }
        if (DataStore.ReadDraft(dataDir) != null)
            Console.WriteLine("Note: data/_draft/draft.json is not packed (promote it first if it should ship).");
        var pack = DataPack.Write(data.Sources);
        GameData.LoadPack(pack); // read it back once
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllBytes(outPath, pack);
        Console.WriteLine($"Wrote {outPath}: {data.Sources.Count} files, {pack.Length / 1024.0:0.0} KB (from {data.Sources.Values.Sum(t => t.Length) / 1024.0:0.0} KB of JSON).");
        Console.WriteLine($"Data fingerprint: {data.Fingerprint}");
        return 0;
    }
    case "check":
    {
        var problems = new List<string>();
        LoadChecked(Flag("--draft"), problems);
        return Report(problems, Flag("--draft") ? "data/ + draft" : "data/");
    }
    case "promote":
    {
        var result = DataStore.Promote(dataDir, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        (result.Promoted ? Console.Out : Console.Error).WriteLine(result.Message);
        foreach (var f in result.ChangedFiles) Console.WriteLine("  changed data/" + f);
        foreach (var p in result.Problems) Console.Error.WriteLine("  - " + p);
        return result.Promoted ? 0 : 1;
    }
    case "fingerprint":
    {
        if (Option("--pack") is { } packPath)
        {
            DataPack.Read(File.ReadAllBytes(packPath), out var fp);
            Console.WriteLine(fp);
        }
        else Console.WriteLine(GameData.Load(dataDir).Fingerprint);
        return 0;
    }
    default:
        Console.Error.WriteLine($"Unknown command '{verb}' (pack, check, promote, fingerprint).");
        return 2;
}
