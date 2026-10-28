namespace AutoHook.Conditions.Definitions;

public sealed class CurrentBaitCD : IConditionDefinition {
    public string Id => nameof(CurrentBaitCD);
    public string Name => "Current bait";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    public string? IncompleteReason(IReadOnlyDictionary<string, object> parameters)
        => GetIds(parameters).Count == 0 ? "select a bait" : null;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var ids = GetIds(parameters);
        if (ids.Count == 0)
            return GetBool(parameters, "inv", false);

        var result = ids.Contains(world.Fishing.BaitInfo.BaitId);
        return GetBool(parameters, "inv", false) ? !result : result;
    }

    public void DrawParams(Condition condition) {
        var ids = GetIds(condition.Params);
        var currentId = ids.Count > 0 ? ids[0] : 0;

        var currentBait = FishBaitCatalog.Get().Baits.FirstOrDefault(b => b.Id == currentId);
        var label = currentBait is { Id: > 0 }
            ? $"[#{currentBait.Id}] {currentBait.Name}"
            : "Select bait";

        DrawUtil.DrawComboSelector(
            FishBaitCatalog.Get().Baits,
            bait => $"[#{bait.Id}] {bait.Name}",
            label,
            bait => condition.Params["ids"] = new List<object> { (long)bait.Id });
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters) {
        var ids = GetIds(parameters);
        return ids.Count == 0 ? "any bait" : ConditionParameterFormat.FormatBaitId(ids[0]);
    }
}
