namespace AutoHook.Presets.AutoCasts;

public sealed class AutoVeteranTrade : BaseActionCast {
    public AutoVeteranTrade(bool isSpearfishing = true) : base(IDs.Actions.VeteranTrade, excludedPriority: true) {
        IsSpearFishing = isSpearfishing;
    }

    public override int DefaultGpThreshold => 200;

    public override string GetName() => UIStrings.VeteranTrade;

    public override bool CastCondition(WorldState ws)
        => EvaluateConditionSet(ws) && !ws.Player.HasStatus(IDs.Status.VeteranTrade);
}
