namespace AutoHook.Conditions.Definitions;

public sealed class ReduceableFishCountCD : IntCompareConditionDefinition {
    public override string Name => "Reduceable fish count";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;
    protected override string ValueLabel => "Fish";
    protected override Func<int, int>? Clamp => static v => Math.Max(0, v);

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => world.Player.ReduceableFishCount;
}
