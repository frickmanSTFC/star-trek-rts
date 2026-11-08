using System;
using System.Collections.Generic;
using System.IO;
using FinalFrontier.Sim.Data;
using Godot;

namespace FinalFrontier;

/// <summary>
/// Where the game's data comes from. Debug builds (editor runs) read the JSON folders next to the project
/// (../data, found by walking up from res://) plus the draft, through <see cref="DataStore.LoadForGame"/>: data that
/// fails its checks is refused and the last good copy is used instead, with the reasons in Messages. Release exports
/// read only res://gamedata.pack (made by tools/DataPacker; the JSON folders are never exported). A debug export with
/// no data folder beside it also reads the pack.
/// </summary>
public static class GameDataLoader
{
    public const string PackPath = "res://gamedata.pack";

    /// <summary>True when the last load gave the base data as it is (data/ or the release pack): no draft, no fallback.
    /// Achievements only count then (PlanetDriver.Achievements.cs).</summary>
    public static bool Official { get; private set; } = true;

    /// <param name="dataDirectory">The data/ folder in use, or "" when the data came from the pack.</param>
    /// <param name="messages">What was refused on the way (shown as warnings); empty = loaded as asked.</param>
    public static GameData Load(DataSetup setup, out string dataDirectory, out IReadOnlyList<string> messages)
    {
        dataDirectory = "";
        messages = Array.Empty<string>();
        if (OS.IsDebugBuild() && FindFolder() is { } dir)
        {
            var result = DataStore.LoadForGame(dir, setup);
            dataDirectory = dir;
            messages = result.Refused;
            Official = result.From == "data/";
            GD.Print($"Game data: {result.From} ({setup}), fingerprint {result.Data.Fingerprint[..12]}");
            foreach (var m in result.Refused) GD.PushError($"Game data: {m}");
            if (result.UsedFallback) GD.PushError($"Game data: running on {result.From}.");
            return result.Data;
        }
        // FileAccess reads from the exported .pck as well as from the project folder; File.ReadAllBytes would not.
        var bytes = Godot.FileAccess.GetFileAsBytes(PackPath);
        if (bytes == null || bytes.Length == 0)
            throw new InvalidOperationException($"No game data: {PackPath} is missing ({Godot.FileAccess.GetOpenError()}). " +
                                                "Build it with `dotnet run --project tools/DataPacker -- --out game/gamedata.pack` before exporting.");
        var data = GameData.LoadPack(bytes, setup);
        Official = true;
        GD.Print($"Game data: {PackPath}, fingerprint {data.Fingerprint[..12]}");
        return data;
    }

    private static string? FindFolder()
    {
        try
        {
            return GameData.FindDataDirectory(ProjectSettings.GlobalizePath("res://"));
        }
        catch (Exception e) when (e is DirectoryNotFoundException or ArgumentException or IOException)
        {
            return null;
        }
    }
}
