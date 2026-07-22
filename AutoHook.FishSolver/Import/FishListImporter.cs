using System.Text.Json;
using AutoHook.FishSolver.Models;

namespace AutoHook.FishSolver.Import;

public static class FishListImporter {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
    };

    public static List<FishRecord> LoadFromFile(string path) {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<FishRecord>>(json, JsonOptions) ?? [];
    }

    public static List<FishRecord> LoadFromJson(string json)
        => JsonSerializer.Deserialize<List<FishRecord>>(json, JsonOptions) ?? [];
}

public static class SolverOverridesMerger {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
    };

    public static List<FishOverride> LoadFromEmbedded() {
        try {
            return EmbeddedDataLoader.Load<List<FishOverride>>("solver_overrides.json");
        }
        catch {
            return [];
        }
    }

    public static List<FishOverride> LoadFromFile(string? path) {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return [];
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<FishOverride>>(json, JsonOptions) ?? [];
    }

    public static void ApplyOverrides(FishKnowledgeBase kb, IEnumerable<FishOverride> overrides) {
        foreach (var ovr in overrides) {
            if (!kb.FishById.TryGetValue(ovr.FishId, out var profile))
                continue;

            // solver_overrides.json can force archetype / hold / slap / early-cancel when inference is wrong
            var tactics = profile.Tactics;
            if (ovr.Archetype is { } archetype)
                tactics = tactics with { Archetype = archetype };
            if (ovr.HoldMode is { } holdMode)
                tactics = tactics with { HoldMode = holdMode };
            if (ovr.SlapTargetFishId is { } slap)
                tactics = tactics with { SlapTargetFishId = slap };
            if (ovr.EarlyCancelSec is { } cancel)
                tactics = tactics with { EarlyCancelSec = cancel };

            var acquisition = profile.Acquisition;
            if (ovr.IntuitionDurationSec is { } dur)
                acquisition = acquisition with { IntuitionDurationSec = dur };

            kb.FishById[ovr.FishId] = profile with {
                Tactics = tactics,
                Acquisition = acquisition,
            };
        }

        kb.Overrides.AddRange(overrides);
    }
}
