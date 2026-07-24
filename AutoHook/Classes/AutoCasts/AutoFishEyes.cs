using FFXIVClientStructs.FFXIV.Client.Game;
using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoFishEyes : BaseActionCast {
    [DefaultValue(6)]
    public override int Priority { get; set; } = 6;
    public override bool IsExcludedPriority { get; set; } = false;

    public bool IgnoreMooch;

    public override bool DoesCancelMooch() => !IgnoreMooch;

    public override bool RequiresTimeWindow() => true;

    public AutoFishEyes() : base(IDs.Actions.FishEyes, ActionType.Action) { }

    public override string GetName() => UIStrings.Fish_Eyes;

    public override string GetHelpText() => UIStrings.CancelsCurrentMooch;

    public override bool CastCondition() => EvaluateConditionSet() && !Service.WorldState.HasStatus(IDs.Status.FishEyes);

    protected override DrawOptionsDelegate DrawOptions => () => {
        DrawUtil.Checkbox(UIStrings.IgnoreMooch, ref IgnoreMooch, UIStrings.IgnoreMoochFishEyes);
        DrawAutoCastConditions();
    };
}
