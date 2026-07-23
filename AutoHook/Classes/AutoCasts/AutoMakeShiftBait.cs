using FFXIVClientStructs.FFXIV.Client.Game;

using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoMakeShiftBait : BaseActionCast {
    [DefaultValue(5)]
    public int MakeshiftBaitStacks = 5;

    public override bool RequiresTimeWindow() => true;

    public AutoMakeShiftBait() : base(IDs.Actions.MakeshiftBait, ActionType.Action) { }

    public override string GetName() => UIStrings.MakeShift_Bait;

    public override string GetHelpText() => UIStrings.TabAutoCasts_DrawMakeShiftBait_HelpText;

    public override bool CastCondition() {
        if (!EvaluateConditionSet())
            return false;

        if (Service.WorldState.BlocksFortune())
            return false;

        var available = Service.WorldState.ActionAvailable(IDs.Actions.MakeshiftBait);
        var hasStacks = Service.WorldState.HasAnglersArtStacks(MakeshiftBaitStacks);

        return hasStacks && available;
    }

    protected override DrawOptionsDelegate DrawOptions => () => {
        var stack = MakeshiftBaitStacks;
        if (DrawUtil.EditNumberField(UIStrings.TabAutoCasts_When_Stack_Equals, ref stack)) {
            MakeshiftBaitStacks = Math.Max(5, Math.Min(stack, 10));
            Service.Save();
        }

        DrawAutoCastConditions();
    };

    [DefaultValue(9)]
    public override int Priority { get; set; } = 9;
    public override bool IsExcludedPriority { get; set; } = false;
}
