using Dalamud.Bindings.ImGui;

namespace AutoHook.Conditions.Definitions;

public sealed class SpearfishingCaughtCountCD : IConditionDefinition {
    public string Id => nameof(SpearfishingCaughtCountCD);
    public string Name => "Spearfishing caught count";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Spearfishing;

    public string? IncompleteReason(IReadOnlyDictionary<string, object> parameters)
        => GetUInt(parameters, "id", 0) == 0 ? "select a fish" : null;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var fishId = GetUInt(parameters, "id", 0);
        var args = GetIntCompareParams(parameters, defaultValue: 1);
        if (fishId == 0)
            return args.Invert;

        var result = CompareInt(world.Spearfishing.GetFishCaughtCount(fishId), args.Value, args.Op);
        return args.Apply(result);
    }

    public void DrawParams(Condition condition) {
        var fishId = GetInt(condition.Params, "id", 0);
        var currentFish = FishBaitCatalog.Get().SpearFishes.FirstOrDefault(fish => fish.Id == fishId);
        var selectedName = currentFish?.Label ?? "-";

        DrawUtil.DrawComboSelector(FishBaitCatalog.Get().SpearFishes, fish => fish.Label, selectedName, fish => condition.Params["id"] = (long)fish.Id);
        ImGui.SameLine();
        DrawIntCompareParams(condition, "##spearfishing_caught_op", "Count", defaultValue: 1, clamp: value => Math.Max(1, value), valueWidth: 60);
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => ConditionParameterFormat.FormatFishCount(parameters);
}
