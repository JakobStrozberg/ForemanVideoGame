using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Crewboss.Core;

/// <summary>The blocks in play order, with the names a crew would give them.</summary>
public static class Blocks
{
    public readonly record struct Info(string Id, string Title, string Blurb);

    public static readonly Info[] All =
    {
        new("Block1", "CREEK FLAT", "SMALL AND FLAT. LEARN THE ROPES."),
        new("Block2", "THE BENCH",  "ROLLING GROUND. WORK THE PIECES."),
        new("Block3", "BURN RIDGE", "BIG. HILLY. SWAMP AND ROCK. GET IT IN."),
    };

    public static Info Get(string id)
    {
        foreach (var b in All) if (b.Id == id) return b;
        return new Info(id, id.ToUpperInvariant(), "");
    }
}

/// <summary>
/// Best stars per block, saved as JSON in the user's app-data folder. A block
/// unlocks once the one before it has at least one star.
/// </summary>
public sealed class Progress
{
    public Dictionary<string, int> BestStars { get; set; } = new();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Crewboss", "progress.json");

    public static Progress Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Progress>(File.ReadAllText(FilePath)) ?? new Progress();
        }
        catch (Exception e) { Console.WriteLine($"Progress load failed: {e.Message}"); }
        return new Progress();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Console.WriteLine($"Progress save failed: {e.Message}"); }
    }

    public int Stars(string blockId) => BestStars.TryGetValue(blockId, out int s) ? s : 0;

    /// <summary>Keep the best result; returns true if this was a new best.</summary>
    public bool Record(string blockId, int stars)
    {
        if (stars <= Stars(blockId)) return false;
        BestStars[blockId] = stars;
        Save();
        return true;
    }

    public bool IsUnlocked(int index) => index == 0 || Stars(Blocks.All[index - 1].Id) >= 1;
}
