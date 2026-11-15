namespace AutoHook.Conditions.Definitions;

public sealed class CollectableWindowCD : BoolInvertConditionDefinition {
    public override string Name => "Collectable window";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.AutoCast;

    protected override bool ReadValue(WorldState world)
        => world.Fishing.CollectableWindowOpen;
}
