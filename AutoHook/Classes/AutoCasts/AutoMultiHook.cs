using FFXIVClientStructs.FFXIV.Client.Game;
using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoMultiHook : BaseActionCast {
    public AutoMultiHook() : base(IDs.Actions.MultiHook, ActionType.Action) { }

    public override string GetName() => UIStrings.Multihook;

    public override int Priority { get; set; } = 0;
    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;

    public override bool CastCondition() {
        if (!EvaluateConditionSet())
            return false;
        if (Service.WorldState.HasStatus(IDs.Status.Multihook))
            return false;

        return Service.WorldState.IsSlottedDutyActionReady(IDs.Actions.MultiHook);
    }

    protected override DrawOptionsDelegate DrawOptions => () => DrawAutoCastConditions();
}
