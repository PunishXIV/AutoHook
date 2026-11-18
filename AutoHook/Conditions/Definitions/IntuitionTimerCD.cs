namespace AutoHook.Conditions.Definitions;

public sealed class IntuitionTimerCD : IntCompareConditionDefinition {
    public override string Name => "Intuition time";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;
    protected override string ValueKey => "sec";
    protected override string ValueLabel => "Seconds";
    protected override Func<int, int>? Clamp => static v => Math.Max(0, v);

    protected override bool? InactiveResult(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var args = GetIntCompareParams(parameters, valueKey: ValueKey);
        return world.Fishing.Intuition.IsActive ? null : args.Invert;
    }

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => (int)Math.Floor(world.Fishing.Intuition.TimeRemaining);
}
