namespace AutoHook.Presets.AutoCasts;

public sealed class AutoReleaseFish : BaseActionCast {
    public AutoReleaseFish() : base(IDs.Actions.Release, priority: 14) { }

    public override string GetName() => UIStrings.ReleaseAllFish;

    protected override bool HasOptions => false;

    public override string GetHelpText() => UIStrings.ReleaseAllFishHelpText;

    public override bool CastCondition(WorldState ws) => true;
}
