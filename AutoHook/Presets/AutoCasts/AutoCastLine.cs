using System.ComponentModel;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoCastLine : BaseActionCast {
    public override bool DoesCancelMooch() => false;

    public override bool RequiresTimeWindow() => true;

    public override bool ShowGpThreshold => false;

    public AutoCastLine() : base(IDs.Actions.Cast) {
        Enabled = true;
        Priority = 1;
    }

    public override string GetName() => UIStrings.AutoCastLine_Auto_Cast_Line;

    public override int Priority { get; set; } = 0;

    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;

    public override bool CastCondition(WorldState ws) => EvaluateConditionSet(ws);

    protected override DrawOptionsDelegate DrawOptions => () => {
        DrawAutoCastConditions();
    };
}
