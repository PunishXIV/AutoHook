using System.ComponentModel;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoMakeShiftBait : BaseActionCast {
    [DefaultValue(5)]
    public int MakeshiftBaitStacks = 5;

    public override bool RequiresTimeWindow() => true;

    public AutoMakeShiftBait() : base(IDs.Actions.MakeshiftBait, priority: 9) { }

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

    protected override void DrawOptions() {
        DrawUtil.EditNumberField(UIStrings.TabAutoCasts_When_Stack_Equals, ref MakeshiftBaitStacks, 5, 10);

        DrawAutoCastConditions();
    }
}
