using System.ComponentModel;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoBaitedBreath : BaseActionCast {
    public AutoBaitedBreath(bool isSpearfishing = true) : base(IDs.Actions.BaitedBreath) {
        IsSpearFishing = isSpearfishing;
    }

    public override int DefaultGpThreshold => 300;

    public override string GetName() => UIStrings.BaitedBreath;

    public override bool CastCondition(WorldState ws) => EvaluateConditionSet(ws);

    protected override DrawOptionsDelegate DrawOptions => () => DrawAutoCastConditions();

    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;
}
