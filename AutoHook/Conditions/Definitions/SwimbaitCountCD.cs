using Dalamud.Bindings.ImGui;

namespace AutoHook.Conditions.Definitions;

public sealed class SwimbaitCountCD : IConditionDefinition {
    public string Name => "Swimbait count";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.AutoCast;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var args = GetIntCompareParams(parameters);
        var fishId = GetInt(parameters, "id", 0);
        if (fishId == 0 && world.SwimbaitEvaluationFishId != 0)
            fishId = (int)world.SwimbaitEvaluationFishId;

        var count = fishId > 0
            ? world.GetSwimbaitCountForFish((uint)fishId)
            : world.GetSwimbaitCount();
        var result = CompareInt(count, args.Value, ResolveOp(parameters));
        return args.Apply(result);
    }

    public void DrawParams(Condition condition) {
        var fishId = GetInt(condition.Params, "id", 0);
        var selectedName = fishId switch {
            0 => "Current slot fish",
            > 0 when FishBaitCatalog.Get().Fishes.FirstOrDefault(f => f.Id == fishId) is { } fish => $"[#{fish.Id}] {fish.Name}",
            _ => "-",
        };

        var fishOptions = new List<BaitFishClass> { new("Current slot fish", 0) };
        fishOptions.AddRange(FishBaitCatalog.Get().Fishes.Select(f => new BaitFishClass(f)));
        DrawUtil.DrawComboSelector(fishOptions, fish => fish.Id == 0 ? fish.Name : $"[#{fish.Id}] {fish.Name}", selectedName, fish => condition.Params["id"] = (long)fish.Id);

        ImGui.SameLine();
        DrawIntCompareParams(condition, "##swimbait_op", "Swimbaits");
    }

    private static string ResolveOp(IReadOnlyDictionary<string, object> parameters) {
        if (parameters.TryGetValue("op", out var opObj) && opObj != null)
            return opObj.ToString() ?? ">=";
        if (parameters.TryGetValue("above", out var aboveObj)) {
            var above = aboveObj is bool b ? b : Convert.ToInt32(aboveObj) != 0;
            return above ? ">=" : "<=";
        }

        return ">=";
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => ConditionParameterFormat.FormatSwimbaitCount(parameters);
}
