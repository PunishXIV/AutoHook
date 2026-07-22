using System.Text.Json;
using AutoHook.FishSolver.Models;

namespace AutoHook.FishSolver.Import;

public static class TeamCraftFishSourceImporter {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
    };

    public static Dictionary<int, List<FishingSourceEntry>> LoadFromFile(string path) {
        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    public static Dictionary<int, List<FishingSourceEntry>>? TryLoadEmbedded() {
        try {
            var raw = EmbeddedDataLoader.Load<Dictionary<string, List<FishingSourceEntry>>>("fishing-sources.json");
            return Parse(raw);
        }
        catch {
            return null;
        }
    }

    public static Dictionary<int, List<FishingSourceEntry>> LoadFromJson(string json) {
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<FishingSourceEntry>>>(json, JsonOptions) ?? [];
        return Parse(raw);
    }

    private static Dictionary<int, List<FishingSourceEntry>> Parse(Dictionary<string, List<FishingSourceEntry>> raw) {
        var result = new Dictionary<int, List<FishingSourceEntry>>();
        foreach (var (key, entries) in raw) {
            if (!int.TryParse(key, out var fishId))
                continue;
            result[fishId] = entries;
        }
        return result;
    }
}

public sealed class FishingSourceEntry {
    public int Spot { get; set; }
    public int Bait { get; set; }
    public int Hookset { get; set; }
    public int Tug { get; set; }
    public bool Snagging { get; set; }
    public int? MinGathering { get; set; }
    public double? Spawn { get; set; }
    public double? Duration { get; set; }
    public List<int> Weathers { get; set; } = [];
    public List<int> WeathersFrom { get; set; } = [];
    public List<FishRecord.FishPredatorRecord> Predators { get; set; } = [];
}
