using AutoHook.FishSolver;
using AutoHook.FishSolver.Models;

namespace AutoHook.FishSolverIntegration;

public sealed class FishSolverBridge {
    private FishSolverService? _service;
    private readonly object _lock = new();

    public bool IsLoaded => _service != null;

    public FishKnowledgeBase? KnowledgeBase => _service?.KnowledgeBase;

    public void EnsureLoaded(string fishListPath) {
        lock (_lock) {
            if (_service != null)
                return;
            _service = FishSolverService.FromFishListFile(fishListPath);
        }
    }

    public static Dictionary<CordialKind, int> ReadCordialInventory(Func<uint, int> getItemCount) => new() {
        [CordialKind.HiCordial] = getItemCount(IDs.Item.HiCordial),
        [CordialKind.CordialHq] = getItemCount(IDs.Item.HQCordial),
        [CordialKind.Cordial] = getItemCount(IDs.Item.Cordial),
        [CordialKind.WateredCordialHq] = getItemCount(IDs.Item.HQWateredCordial),
        [CordialKind.WateredCordial] = getItemCount(IDs.Item.WateredCordial),
    };

    public PlayerProfile CreateProfile(
        int playerLevel,
        int gpMax,
        IReadOnlyDictionary<CordialKind, int>? cordialInventory = null)
        => PlayerProfile.Create(playerLevel, gpMax, configureAssumptions: a => {
            a.CordialInventory = cordialInventory == null ? null : [with(cordialInventory)];
        });

    public SolverOutput? Solve(int fishId, int playerLevel, int gpMax, IReadOnlyDictionary<CordialKind, int>? cordialInventory = null, int? preferredSpotId = null) {
        if (_service == null)
            return null;

        return _service.Solve(new SolverRequest {
            TargetFishId = fishId,
            PreferredSpotId = preferredSpotId,
            Profile = CreateProfile(playerLevel, gpMax, cordialInventory),
        });
    }

    public CustomPresetConfig? BuildPreset(int fishId, int playerLevel, int gpMax, string? presetName = null, IReadOnlyDictionary<CordialKind, int>? cordialInventory = null, int? preferredSpotId = null) {
        var output = Solve(fishId, playerLevel, gpMax, cordialInventory, preferredSpotId);
        return output == null ? null : SolverPresetBuilder.Build(output, presetName);
    }
}
