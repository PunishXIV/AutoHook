using Dalamud.Bindings.ImGui;

namespace AutoHook.Conditions.Definitions;

public sealed class SessionCaughtCountCD : IConditionDefinition, ISimpleConditionValue<(bool Enabled, int Limit)> {
    public string Id => "FishCaughtCountCD"; // either migrate this or don't change it
    public string Name => "Session caught count";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.FishIgnore | ConditionScopeFlags.Hook;

    public string? IncompleteReason(IReadOnlyDictionary<string, object> parameters)
        => GetUInt(parameters, "id", 0) == 0 ? "select a fish" : null;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var fishId = GetUInt(parameters, "id", 0);
        var args = GetIntCompareParams(parameters, defaultValue: 1);
        if (fishId <= 0)
            return args.Invert;

        var result = CompareInt(world.Fishing.GetFishCaughtCount(fishId), args.Value, args.Op);
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
        DrawIntCompareParams(condition, "##session_caught_op", "Count", defaultValue: 1, clamp: v => Math.Max(1, v), valueWidth: 60);
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => ConditionParameterFormat.FormatFishCount(parameters);

    (bool Enabled, int Limit) ISimpleConditionValue<(bool Enabled, int Limit)>.FromParams(IReadOnlyDictionary<string, object> p)
        => (true, Math.Max(1, GetInt(p, "val", 1)));

    IReadOnlyDictionary<string, object>? ISimpleConditionValue<(bool Enabled, int Limit)>.ToParams((bool Enabled, int Limit) value, object? context) {
        if (!value.Enabled) return null;
        var fishId = context is int id ? id : 0;
        var dict = new IntCompareParams(value.Limit, ">=", false).ToParams();
        if (fishId > 0)
            dict["id"] = (long)fishId;
        return dict;
    }
}
