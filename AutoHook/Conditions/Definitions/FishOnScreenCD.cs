namespace AutoHook.Conditions.Definitions;

public sealed class FishOnScreenCD : IntCompareConditionDefinition {
    public override string Name => "Fish on screen";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Spearfishing;
    protected override string ValueLabel => "Fish";
    protected override int DefaultValue => 2;
    protected override Func<int, int>? Clamp => static value => Math.Clamp(value, 0, 3);

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => world.Spearfishing.FishOnScreenCount;
}
