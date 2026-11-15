namespace AutoHook.Presets.AutoCasts;

public sealed class AutoMooch2 : BaseActionCast {
    public AutoMooch2() : base(IDs.Actions.Mooch2, priority: 11, excludedPriority: true) { }

    public override string GetName() => UIStrings.Mooch_II;

    protected override bool HasOptions => false;

    public override string GetHelpText() => UIStrings.AutoMooch_HelpText;

    public override bool CastCondition(WorldState ws) => true;
}
