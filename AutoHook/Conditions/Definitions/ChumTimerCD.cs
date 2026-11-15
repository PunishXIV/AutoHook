namespace AutoHook.Conditions.Definitions;

public sealed class ChumTimerCD : IConditionDefinition {
    public string Name => "Chum timer";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var args = GetRangeParams(parameters);
        if (!world.Fishing.ChumActive) return args.Invert;
        var ranges = args.Ranges;
        if (ranges.Count == 0) return true;
        var t = world.Fishing.BiteInfo.BiteTimeSeconds;
        var result = false;
        foreach (var (min, max) in ranges)
            if (t >= min && (max <= 0 || t <= max)) { result = true; break; }
        return args.Apply(result);
    }

    public void DrawParams(Condition condition)
        => DrawSingleRangeParams(condition);

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => ConditionParameterFormat.FormatRanges(parameters);
}
