namespace AutoHook.Conditions.Definitions;

public sealed class CosmicCollectedTotalCD : IntCompareConditionDefinition {
    public override string Name => "Cosmic collected total";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;
    protected override string ValueLabel => "Collected";
    protected override float ValueWidth => 90f;
    protected override Func<int, int>? Clamp => static v => Math.Max(0, v);

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => world.WKS.CollectedTotal;
}
