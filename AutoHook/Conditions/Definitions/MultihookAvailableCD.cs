using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHook.Conditions.Definitions;

public sealed class MultihookAvailableCD : BoolInvertConditionDefinition {
    public override string Name => "Multihook";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.AutoCast;

    protected override bool ReadValue(WorldState world)
        => world.IsSlottedDutyActionReady(IDs.Actions.MultiHook, ActionType.Action);
}
