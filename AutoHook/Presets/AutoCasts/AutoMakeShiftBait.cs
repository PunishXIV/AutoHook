using FFXIVClientStructs.FFXIV.Client.Game;

using System.ComponentModel;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoMakeShiftBait : BaseActionCast {
    [DefaultValue(5)]
    public int MakeshiftBaitStacks = 5;

    public override bool RequiresTimeWindow() => true;

    protected override int? AnglersArtStacksToUse => MakeshiftBaitStacks;

    public AutoMakeShiftBait() : base(IDs.Actions.MakeshiftBait, ActionType.Action) { }

    public override string GetName() => UIStrings.MakeShift_Bait;

    public override string GetHelpText() => UIStrings.TabAutoCasts_DrawMakeShiftBait_HelpText;

    public override bool CastCondition(WorldState ws) {
        if (!EvaluateConditionSet(ws))
            return false;

        if (ws.BlocksFortune())
            return false;

        var available = ws.ActionAvailable(IDs.Actions.MakeshiftBait);
        var hasStacks = ws.HasAnglersArtStacks(MakeshiftBaitStacks);

        return hasStacks && available;
    }

    protected override DrawOptionsDelegate DrawOptions => () => {
        var stack = MakeshiftBaitStacks;
        if (DrawUtil.EditNumberField(UIStrings.TabAutoCasts_When_Stack_Equals, ref stack)) {
            MakeshiftBaitStacks = Math.Max(5, Math.Min(stack, 10));
            Configuration.Save();
        }

        DrawAutoCastConditions();
    };

    [DefaultValue(9)]
    public override int Priority { get; set; } = 9;
    public override bool IsExcludedPriority { get; set; } = false;
}
