namespace AutoHook.Conditions.Definitions;

public sealed class OceanZoneTimeRemainingCD : IntCompareConditionDefinition {
    public override string Id => nameof(OceanZoneTimeRemainingCD);
    public override string Name => "Ocean time left";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;
    protected override string ValueKey => "sec";
    protected override string ValueLabel => "Seconds";
    protected override Func<int, int>? Clamp => static v => Math.Max(0, v);

    protected override bool? InactiveResult(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var args = GetIntCompareParams(parameters, valueKey: ValueKey);
        return world.OceanFishing == OceanFishingState.Empty ? args.Invert : null;
    }

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => (int)Math.Floor(world.OceanFishing.TimeLeftInZone);
}
