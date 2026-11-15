namespace AutoHook.Presets.AutoCasts;

public sealed class AutoFishEyes : BaseActionCast {
    public override bool DoesCancelMooch() => true;

    public override bool RequiresTimeWindow() => true;

    public AutoFishEyes() : base(IDs.Actions.FishEyes, priority: 6) { }

    public override string GetName() => UIStrings.Fish_Eyes;

    public override string GetHelpText() => UIStrings.CancelsCurrentMooch;

    public override bool CastCondition(WorldState ws) => EvaluateConditionSet(ws) && !ws.Player.HasStatus(IDs.Status.FishEyes);
}
