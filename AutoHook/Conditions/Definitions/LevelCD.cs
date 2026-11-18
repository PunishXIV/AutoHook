namespace AutoHook.Conditions.Definitions;

public sealed class LevelCD : IntCompareConditionDefinition {
    public override string Name => "Level";
    public override ConditionScopeFlags AllowedScopes
        => ConditionScopeFlags.Hook | ConditionScopeFlags.AutoCordial | ConditionScopeFlags.AutoCast | ConditionScopeFlags.Spearfishing;
    protected override string ValueLabel => "Level";
    protected override Func<int, int>? Clamp => static v => Math.Clamp(v, 1, 100);

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => world.Player.Level;
}
