using Dalamud.Bindings.ImGui;

namespace AutoHook.Conditions.Definitions;

public sealed class FishCaughtCounterCD : IConditionDefinition {
    public string Id => "FishCountCD"; // either migrate or don't change
    public string Name => "Fish caught counter";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    public string? IncompleteReason(IReadOnlyDictionary<string, object> parameters)
        => GetInt(parameters, "id", 0) <= 0 ? "select a fish" : null;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var fishId = GetInt(parameters, "id", 0);
        var args = GetIntCompareParams(parameters);
        if (fishId <= 0)
            return args.Invert;

        var presets = Configuration.C.HookPresets.CustomPresets.Append(Configuration.C.HookPresets.DefaultPreset);
        var total = presets
            .SelectMany(p => p.ListOfFish)
            .Where(f => f.Fish.Id == fishId)
            .Sum(f => FishingCounters.GetFishCount(f.UniqueId));

        var result = CompareInt(total, args.Value, args.Op);
        return args.Apply(result);
    }

    public void DrawParams(Condition condition) {
        var fishId = GetInt(condition.Params, "id", 0);
        var currentFish = FishBaitCatalog.Get().Fishes.FirstOrDefault(f => f.Id == fishId);
        var selectedName = currentFish is { Id: > 0 }
            ? $"[#{currentFish.Id}] {currentFish.Name}"
            : "-";

        DrawUtil.DrawComboSelector(FishBaitCatalog.Get().Fishes, fish => $"[#{fish.Id}] {fish.Name}", selectedName, fish => condition.Params["id"] = (long)fish.Id);

        ImGui.SameLine();
        DrawIntCompareParams(condition, "##fishcount_op", "Count", clamp: v => Math.Max(0, v));
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => ConditionParameterFormat.FormatFishCount(parameters);
}
