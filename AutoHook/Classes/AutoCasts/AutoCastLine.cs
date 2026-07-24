using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoCastLine : BaseActionCast {
    [DefaultValue(true)]
    public bool IgnoreMooch = true;

    public override bool DoesCancelMooch() => !IgnoreMooch;

    public override bool RequiresTimeWindow() => true;

    public AutoCastLine() : base(IDs.Actions.Cast) {
        Enabled = true;
        Priority = 1;
    }

    public override string GetName() => UIStrings.AutoCastLine_Auto_Cast_Line;

    public override int Priority { get; set; } = 0;

    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;

    public override bool CastCondition() => EvaluateConditionSet();

    protected override DrawOptionsDelegate DrawOptions => () => {
        DrawAutoCastConditions();

        DrawUtil.Checkbox(UIStrings.IgnoreMooch, ref IgnoreMooch,
            UIStrings.IgnoreMoochHelpText);
    };
}
