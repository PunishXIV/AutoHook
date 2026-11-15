namespace AutoHook.Presets.AutoCasts;

public sealed class AutoBaitedBreath : BaseActionCast {
    public AutoBaitedBreath(bool isSpearfishing = true) : base(IDs.Actions.BaitedBreath, excludedPriority: true) {
        IsSpearFishing = isSpearfishing;
    }

    public override int DefaultGpThreshold => 300;

    public override string GetName() => UIStrings.BaitedBreath;
}
