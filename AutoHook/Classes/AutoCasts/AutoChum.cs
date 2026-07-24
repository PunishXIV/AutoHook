using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoChum : BaseActionCast {
    public override bool DoesCancelMooch() => true;

    public AutoChum() : base(IDs.Actions.Chum) { }

    public override string GetName() => UIStrings.Chum;

    public override string GetHelpText() => UIStrings.CancelsCurrentMooch;

    public override bool CastCondition() => EvaluateConditionSet();

    protected override DrawOptionsDelegate DrawOptions => () => DrawAutoCastConditions();

    [DefaultValue(1)]
    public override int Priority { get; set; } = 1;
    public override bool IsExcludedPriority { get; set; } = false;
}
