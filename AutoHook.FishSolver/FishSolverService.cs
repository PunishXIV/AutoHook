using AutoHook.FishSolver.Engine;
using AutoHook.FishSolver.Import;
using AutoHook.FishSolver.Models;

namespace AutoHook.FishSolver;

public sealed class FishSolverService(FishKnowledgeBase knowledgeBase) {
    private readonly FishSolverEngine _engine = new(knowledgeBase);

    public FishKnowledgeBase KnowledgeBase { get; } = knowledgeBase;

    public static FishSolverService FromFishListFile(string fishListPath, IEnumerable<FishOverride>? overrides = null) {
        var records = FishListImporter.LoadFromFile(fishListPath);
        var sources = TryLoadFishingSources(fishListPath);
        var kb = FishKnowledgeBaseBuilder.Build(records, overrides, sources);
        return new FishSolverService(kb);
    }

    public static FishSolverService FromFishListJson(string json, IEnumerable<FishOverride>? overrides = null) {
        var records = FishListImporter.LoadFromJson(json);
        var kb = FishKnowledgeBaseBuilder.Build(records, overrides, TeamCraftFishSourceImporter.TryLoadEmbedded());
        return new FishSolverService(kb);
    }

    private static Dictionary<int, List<FishingSourceEntry>>? TryLoadFishingSources(string fishListPath) {
        var dir = Path.GetDirectoryName(fishListPath);
        if (!string.IsNullOrEmpty(dir)) {
            var alongside = Path.Combine(dir, "fishing-sources.json");
            if (File.Exists(alongside))
                return TeamCraftFishSourceImporter.LoadFromFile(alongside);
        }

        return TeamCraftFishSourceImporter.TryLoadEmbedded();
    }

    public SolverOutput? Solve(int targetFishId, PlayerProfile profile, int? preferredSpotId = null)
        => _engine.Solve(new SolverRequest {
            TargetFishId = targetFishId,
            PreferredSpotId = preferredSpotId,
            Profile = profile,
        });

    public SolverOutput? Solve(SolverRequest request) => _engine.Solve(request);
}
