namespace AutoHook.Presets.AutoCasts;

public sealed class AutoMultiHook : BaseActionCast {
    public AutoMultiHook() : base(IDs.Actions.MultiHook, excludedPriority: true) { }

    public override string GetName() => UIStrings.Multihook;

    protected override string? LabelHelpMarker => UIStrings.Multihook_CosmicHelpText;

    public override bool CastCondition(WorldState ws) {
        if (!EvaluateConditionSet(ws))
            return false;
        if (ws.Player.HasStatus(IDs.Status.Multihook))
            return false;

        return ws.IsSlottedDutyActionReady(IDs.Actions.MultiHook);
    }
}
