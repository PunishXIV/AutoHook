namespace AutoHook.Presets.AutoCasts;

public sealed class AutoPrizeCatch : BaseActionCast {
    public override bool DoesCancelMooch() => true;

    public AutoPrizeCatch() : base(IDs.Actions.PrizeCatch, priority: 13) { }

    public override string GetName() => UIStrings.Prize_Catch;

    public override string GetHelpText() => UIStrings.Use_Prize_Catch_HelpText;

    public override bool CastCondition(WorldState ws) {
        if (!EvaluateConditionSet(ws))
            return false;

        if (!Enabled)
            return false;

        if (ws.BlocksFortune())
            return false;

        return ws.ActionAvailable(IDs.Actions.PrizeCatch);
    }
}
