namespace AutoHook.Presets.AutoCasts;

public sealed class AutoSnagging : BaseActionCast {
    public AutoSnagging() : base(IDs.Actions.Snagging, priority: 3, excludedPriority: true) { }

    public override string GetName() => UIStrings.Snagging;

    protected override bool HasOptions => false;

    public override string GetHelpText() => UIStrings.SnaggingHelpText;
    public override bool ShowGpThreshold => false;

    public override bool CastCondition(WorldState ws) => !ws.Player.HasStatus(IDs.Status.Snagging);
}
