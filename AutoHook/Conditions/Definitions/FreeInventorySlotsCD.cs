namespace AutoHook.Conditions.Definitions;

public sealed class FreeInventorySlotsCD : IntCompareConditionDefinition {
    public override string Name => "Free inventory slots";
    public override ConditionScopeFlags AllowedScopes
        => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast | ConditionScopeFlags.Spearfishing;
    protected override string ValueLabel => "Slots";
    protected override Func<int, int>? Clamp => static v => Math.Max(0, v);

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => world.Player.FreeInventorySlots;
}