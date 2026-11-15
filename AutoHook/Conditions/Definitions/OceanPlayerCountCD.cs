namespace AutoHook.Conditions.Definitions;

public sealed class OceanPlayerCountCD : IntCompareConditionDefinition {
    public override string Name => "Ocean player count";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;
    protected override string ValueLabel => "Players";
    protected override Func<int, int>? Clamp => static v => Math.Max(0, v);

    protected override bool? InactiveResult(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var args = GetIntCompareParams(parameters);
        return world.OceanFishing == OceanFishingState.Empty ? args.Invert : null;
    }

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => world.OceanFishing.PlayerCount;
}
