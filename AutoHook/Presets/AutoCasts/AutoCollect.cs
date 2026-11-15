namespace AutoHook.Presets.AutoCasts;

public sealed class AutoCollect : BaseActionCast {
    public AutoCollect(bool isSpearfishing = false) : base(IDs.Actions.Collect, priority: 2, excludedPriority: true) => IsSpearFishing = isSpearfishing;

    public override string GetName() => UIStrings.Collect;

    public override string GetHelpText() => UIStrings.CollectHelpText;
    public override bool ShowGpThreshold => false;

    public override bool CastCondition(WorldState ws)
        => EvaluateConditionSet(ws) && !ws.Player.HasStatus(IDs.Status.CollectorsGlove);
}
