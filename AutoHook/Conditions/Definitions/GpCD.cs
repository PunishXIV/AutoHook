namespace AutoHook.Conditions.Definitions;

public sealed class GpCD : IntCompareConditionDefinition {
    public override string Name => "GP";
    public override ConditionScopeFlags AllowedScopes
        => ConditionScopeFlags.Hook | ConditionScopeFlags.AutoCordial | ConditionScopeFlags.AutoCast | ConditionScopeFlags.Spearfishing;
    protected override string ValueLabel => "GP";

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => (int)world.Player.CurrentGp;
}
