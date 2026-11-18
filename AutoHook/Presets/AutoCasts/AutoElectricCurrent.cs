namespace AutoHook.Presets.AutoCasts;

public sealed class AutoElectricCurrent : BaseActionCast {
    public AutoElectricCurrent(bool isSpearfishing = true) : base(IDs.Actions.ElectricCurrent, excludedPriority: true) {
        IsSpearFishing = isSpearfishing;
    }

    public override string GetName() => UIStrings.ElectricCurrent;

    public override bool ShowGpThreshold => false;
}
