namespace AutoHook.Conditions.Definitions;

public sealed class ShadowedNodeCD : BoolInvertConditionDefinition {
    public override string Name => "Shadowed node";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Spearfishing;

    protected override bool ReadValue(WorldState world)
        => world.Spearfishing.Spot.IsShadowNode;
}
