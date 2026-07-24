using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoCollect : BaseActionCast {
    [DefaultValue(2)]
    public override int Priority { get; set; } = 2;
    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;

    public AutoCollect() : base(IDs.Actions.Collect) { }

    public override string GetName() => UIStrings.Collect;

    public override string GetHelpText() => UIStrings.CollectHelpText;

    public override bool CastCondition() => !Service.WorldState.HasStatus(IDs.Status.CollectorsGlove);
}
