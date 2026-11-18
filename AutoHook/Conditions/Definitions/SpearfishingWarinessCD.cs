namespace AutoHook.Conditions.Definitions;

public sealed class SpearfishingWarinessCD : IntCompareConditionDefinition {
    public override string Name => "Wariness";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Spearfishing;
    protected override string ValueLabel => "Wariness";
    protected override Func<int, int>? Clamp => static value => Math.Max(0, value);

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => world.Spearfishing.Wariness;
}
