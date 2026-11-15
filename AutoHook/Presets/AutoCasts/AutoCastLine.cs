namespace AutoHook.Presets.AutoCasts;

public sealed class AutoCastLine : BaseActionCast {
    public override bool DoesCancelMooch() => false;

    public override bool RequiresTimeWindow() => true;

    public AutoCastLine() : base(IDs.Actions.Cast, priority: 1, excludedPriority: true) {
        Enabled = true;
    }

    public override string GetName() => UIStrings.AutoCastLine_Auto_Cast_Line;
}
