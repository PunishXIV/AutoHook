using System.ComponentModel;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoElectricCurrent : BaseActionCast {
    public AutoElectricCurrent(bool isSpearfishing = true) : base(IDs.Actions.ElectricCurrent) {
        IsSpearFishing = isSpearfishing;
    }

    public override string GetName() => UIStrings.ElectricCurrent;

    public override bool ShowGpThreshold => false;

    public override bool CastCondition(WorldState ws) => EvaluateConditionSet(ws);

    protected override DrawOptionsDelegate DrawOptions => () => DrawAutoCastConditions();

    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;
}
