using System.Globalization;
using FinalFrontier.Balance;
using FinalFrontier.Sim.Data;

// Headless balance runner. See balance/README.md.
//   dotnet run -c Release --project tools/BalanceRunner -- --orders balance/orders/standard_opener.json --seeds 1,1701,99999 --minutes 30 --out balance/reports/x
//   ... --compare <dataDirA> <dataDirB>            A/B two data folders
//   ... --set "buildings.json:refinery.cycles_per_min=16"   A = data, B = data with these edits (repeatable)
//   ... --orders a.json,b.json                    compare build orders on the same data
//   ... --factions federation,klingon              compare factions (same order and data)
return Cli.Start(args);

internal static class Cli
{
    public static int Start(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        string? dataDir = null, outDir = null, title = null;
        var compare = new List<string>();
        var orders = new List<string>();
        var factions = new List<string>();
        var sets = new List<string>();
        var seeds = new List<ulong> { 1, 1701, 99999 };
        double minutes = 30;
        var sample = 10;
        var quiet = false;
        var labels = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--data": dataDir = Next(); break;
                case "--orders": case "--order": orders.AddRange(Next().Split(',', StringSplitOptions.RemoveEmptyEntries)); break;
                case "--seeds": seeds = Next().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => ulong.Parse(s, CultureInfo.InvariantCulture)).ToList(); break;
                case "--minutes": minutes = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--sample": sample = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--out": outDir = Next(); break;
                case "--title": title = Next(); break;
                case "--compare": compare.Add(Next()); compare.Add(Next()); break;
                case "--set": sets.Add(Next()); break;
                case "--factions": case "--faction": factions.AddRange(Next().Split(',', StringSplitOptions.RemoveEmptyEntries)); break;
                case "--labels": labels.AddRange(Next().Split(',')); break;
                case "--quiet": quiet = true; break;
                case "-h": case "--help": Console.WriteLine(Help); return 0;
                default: throw new ArgumentException($"unknown option {args[i]} (try --help)");
            }
        }
        var repo = FindRepo();
        if (orders.Count == 0) orders.Add(Path.Combine(repo ?? ".", "balance", "orders", "standard_opener.json"));

        // Data variants: one folder, two folders (--compare), or the folder plus edits (--set).
        var dataVariants = new List<(string Label, string Dir, GameData Data, List<string> Edits)>();
        if (compare.Count > 0)
        {
            dataVariants.Add(("A", compare[0], LoadData(compare[0]), new List<string>()));
            var b = LoadData(compare[1]);
            dataVariants.Add(("B", compare[1], sets.Count > 0 ? Edit(b, sets) : b, sets));
        }
        else
        {
            var dir = dataDir ?? GameData.FindDataDirectory(Directory.GetCurrentDirectory());
            var data = LoadData(dir);
            dataVariants.Add(("A", dir, data, new List<string>()));
            if (sets.Count > 0) dataVariants.Add(("B", dir, Edit(data, sets), sets));
        }

        var loaded = orders.Select(BuildOrder.Load).ToList();
        if (factions.Count == 0) factions.Add("");
        var variants = new List<Variant>();
        foreach (var (dLabel, dir, data, edits) in dataVariants)
            foreach (var o in loaded)
                foreach (var f in factions)
                {
                    var order = f.Length == 0 ? o : With(o, f);
                    if (data.Factions.All(x => x.Id != order.Faction)) throw new ArgumentException($"unknown faction '{order.Faction}'");
                    var parts = new List<string>();
                    if (dataVariants.Count > 1) parts.Add(dLabel);
                    if (loaded.Count > 1) parts.Add(order.Name);
                    if (factions.Count > 1) parts.Add(order.Faction);
                    variants.Add(new Variant
                    {
                        Label = parts.Count == 0 ? order.Name : string.Join(" · ", parts),
                        Data = Relative(dir, repo),
                        Order = order.Name,
                        Faction = order.Faction,
                        Edits = edits,
                    });
                    var v = variants[^1];
                    foreach (var seed in seeds)
                    {
                        var r = Runner.Run(data, order, seed, minutes, sample, v.Label);
                        v.Runs.Add(r);
                        if (!quiet)
                            Console.WriteLine($"{v.Label,-28} seed {seed,-6} {r.Ticks} ticks in {r.WallMs / 1000:0.00} s = {r.TicksPerSecond:0} ticks/s (×{r.Speedup:0} real time)  steps {r.StepsDone}/{r.StepsTotal}  hash {r.StateHash}");
                    }
                }
        if (labels.Count == variants.Count)
            for (var i = 0; i < labels.Count; i++) variants[i].Label = labels[i];
        if (variants.Count > 4) Console.Error.WriteLine("note: more than 4 variants; the report's chart colours repeat after 4.");

        title ??= variants.Count == 1 ? $"Balance run: {variants[0].Order}" : "Balance comparison: " + string.Join(" vs ", variants.Select(v => v.Label));
        outDir ??= Path.Combine(repo ?? ".", "balance", "reports", "latest");
        Report.Write(outDir, variants, title, minutes, sample);
        if (!quiet) Summary(variants);
        Console.WriteLine($"report: {Path.GetFullPath(Path.Combine(outDir, "report.html"))}");
        return 0;
    }

    private static GameData LoadData(string dir) => GameData.Load(Path.GetFullPath(dir)); // only through GameData's public loader

    /// <summary>"file:label.path=value" edits applied with the test panel's DataTuning (number tokens only; the label is
    /// the Data tab's, e.g. buildings.json:refinery.cycles_per_min or surfaces.json:units.mining_crawler.extract_per_min).</summary>
    private static GameData Edit(GameData data, List<string> sets)
    {
        var edits = new List<DataEdit>();
        var numbers = DataTuning.Numbers(data);
        foreach (var s in sets)
        {
            var colon = s.IndexOf(':');
            var eq = s.LastIndexOf('=');
            if (colon < 0 || eq < colon) throw new FormatException($"--set {s}: write file:label=value, e.g. buildings.json:refinery.cycles_per_min=16");
            var file = s[..colon];
            var label = s[(colon + 1)..eq].Trim();
            var value = s[(eq + 1)..].Trim();
            var dotted = (DataNumber n) => n.Label.Replace(" › ", ".");
            var hits = numbers.Where(n => n.File == file && (dotted(n) == label || dotted(n).EndsWith("." + label, StringComparison.Ordinal) || n.Path == label)).ToList();
            if (hits.Count == 0) throw new FormatException($"--set {s}: no number '{label}' in {file}");
            if (hits.Count > 1) throw new FormatException($"--set {s}: '{label}' matches {hits.Count} numbers ({string.Join(", ", hits.Take(4).Select(dotted))}…); give more of the path");
            edits.Add(new DataEdit(file, hits[0].Path, value));
        }
        if (DataTuning.Rebuild(data, edits, Array.Empty<DataFile>(), out var next) is { } problem) throw new FormatException("--set: " + problem);
        return next!;
    }

    private static BuildOrder With(BuildOrder o, string faction) => new()
    {
        Name = o.Name,
        Description = o.Description,
        Faction = faction,
        Planet = o.Planet,
        LandingPrefer = o.LandingPrefer,
        SurveyMaxDistance = o.SurveyMaxDistance,
        CrawlerPrefer = o.CrawlerPrefer,
        CrawlerMaxDistance = o.CrawlerMaxDistance,
        MaintainBelow = o.MaintainBelow,
        RepairBelow = o.RepairBelow,
        Research = o.Research,
        Steps = o.Steps,
    };

    private static string? FindRepo()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "balance")) && Directory.Exists(Path.Combine(d.FullName, "game"))) return d.FullName;
        return null;
    }

    private static string Relative(string dir, string? repo) =>
        repo == null ? dir : Path.GetRelativePath(repo, Path.GetFullPath(dir)).Replace('\\', '/');

    private static void Summary(List<Variant> variants)
    {
        Console.WriteLine();
        var keys = new[] { "ms:built:refinery", "ms:first_metal", "ms:built:vehicle_bay", "ms:first_parasteel", "ms:mk2", "ms:second_base", "end:duranium", "end:tritanium", "end:parasteel", "peak_storage", "stall:storage_full", "crawler_uptime", "speed" };
        Console.WriteLine($"{"measure (median of seeds)",-44}" + string.Join("", variants.Select(v => $"{v.Label,22}")));
        foreach (var k in keys)
        {
            var m0 = variants[0].Measures.FirstOrDefault(m => m.Key == k);
            if (m0 == null) continue;
            Console.WriteLine($"{m0.Label,-44}" + string.Join("", variants.Select(v =>
            {
                var m = v.Measures.First(x => x.Key == k);
                var s = m.Spread;
                var text = s.Median is not { } x ? "-" : m.Unit == "time" ? $"{(int)x / 60}:{(int)x % 60:00}" : x.ToString("0.#", CultureInfo.InvariantCulture);
                return $"{text + (s.Reached < s.Runs ? $" ({s.Reached}/{s.Runs})" : ""),22}";
            })));
        }
    }

    private const string Help = """
        BalanceRunner: plays the surface economy headless with scripted build orders and reports what happened.
          --data <dir>             data folder (default: the repo's data/)
          --orders <a.json,b.json> build orders (default balance/orders/standard_opener.json); several = compare them
          --seeds 1,1701,99999     planet seeds (one run each)
          --minutes 30             game time per run
          --sample 10              seconds between samples
          --out <dir>              report folder (default balance/reports/latest)
          --compare <dirA> <dirB>  A/B two data folders
          --set file:label=value   B = A with this number changed (repeatable), e.g. buildings.json:refinery.cycles_per_min=16
          --factions a,b           run each order as these factions
          --labels A,B             names for the variants
          --title <text>           report title
          --quiet                  only print the report path
        """;
}
