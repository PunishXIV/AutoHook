namespace AutoHook.Presets.AutoCasts;

public sealed class AutoChum : BaseActionCast {
    public override bool DoesCancelMooch() => true;

    public AutoChum() : base(IDs.Actions.Chum, priority: 1) { }

    public override string GetName() => UIStrings.Chum;

    public override string GetHelpText() => UIStrings.CancelsCurrentMooch;
}
