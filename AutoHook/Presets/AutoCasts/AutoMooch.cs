namespace AutoHook.Presets.AutoCasts;

public sealed class AutoMooch : BaseActionCast {
    public AutoMooch2 Mooch2 = new();

    public override bool RequiresTimeWindow() => true;

    public AutoMooch() : base(IDs.Actions.Mooch, priority: 10, excludedPriority: true) { }

    [NonSerialized] public bool UseAlwaysMoochLabel;
    [NonSerialized] public bool SuppressHelpText;

    public override string GetName() => UseAlwaysMoochLabel ? UIStrings.Always_Mooch : UIStrings.AutoMooch;

    public override string GetHelpText() => SuppressHelpText ? string.Empty : UIStrings.AutoMooch_HelpText;

    public override bool CastCondition(WorldState ws) {
        if (!EvaluateConditionSet(ws))
            return false;

        if (Mooch2.IsAvailableToCast(ws)) {
            Id = IDs.Actions.Mooch2;
            return true;
        }

        if (ws.ActionAvailable(IDs.Actions.Mooch)) {
            Id = IDs.Actions.Mooch;
            return true;
        }

        return false;
    }

    protected override void DrawOptions() {
        Mooch2.DrawConfig(null);
        DrawAutoCastConditions();
    }
}
