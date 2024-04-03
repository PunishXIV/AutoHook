using System;
using AutoHook.Data;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHook.Classes.AutoCasts;

public class AutoMakeShiftBait : BaseActionCast
{
    public int MakeshiftBaitStacks = 5;
    public bool _onlyUseWithIntuition;

    public bool OnlyWhenMoochNotUp;
    public bool UseOnlyWhenMoochIIOnCD;

    public override bool RequiresTimeWindow() => true;

    public AutoMakeShiftBait() : base(UIStrings.MakeShift_Bait, IDs.Actions.MakeshiftBait, ActionType.Action)
    {
        HelpText = UIStrings.TabAutoCasts_DrawMakeShiftBait_HelpText;
    }

    public override string GetName()
        => Name = UIStrings.MakeShift_Bait;

    public override bool CastCondition()
    {
        if (!Enabled)
            return false;

        if (PlayerRes.HasStatus(IDs.Status.MakeshiftBait))
            return false;

        if (PlayerRes.HasStatus(IDs.Status.PrizeCatch))
            return false;

        if (PlayerRes.HasStatus(IDs.Status.AnglersFortune))
            return false;

        if (!PlayerRes.HasStatus(IDs.Status.FishersIntuition) && _onlyUseWithIntuition)
            return false;

        if (PlayerRes.IsMoochAvailable() && OnlyWhenMoochNotUp)
            return false;

        if (UseOnlyWhenMoochIIOnCD && !PlayerRes.ActionOnCoolDown(IDs.Actions.Mooch2))
            return false;

        bool available = PlayerRes.ActionTypeAvailable(IDs.Actions.MakeshiftBait);
        bool hasStacks = PlayerRes.HasAnglersArtStacks(MakeshiftBaitStacks);

        return hasStacks && available;
    }

    protected override DrawOptionsDelegate DrawOptions => () =>
    {
        var stack = MakeshiftBaitStacks;
        if (DrawUtil.EditNumberField(UIStrings.TabAutoCasts_When_Stack_Equals, ref stack))
        {
            // value has to be between 5 and 10
            MakeshiftBaitStacks = Math.Max(5, Math.Min(stack, 10));
            Service.Save();
        }

        if (DrawUtil.Checkbox(UIStrings.OnlyUseWhenFisherSIntutionIsActive, ref _onlyUseWithIntuition))
        {
            Service.Save();
        }
        
        if (DrawUtil.Checkbox(UIStrings.OnlyWhenMoochNotAvailable, ref OnlyWhenMoochNotUp))
        {
            Service.Save();
        }

        if (DrawUtil.Checkbox(UIStrings.AutoCastExtraOptionMakeshiftBait, ref UseOnlyWhenMoochIIOnCD))
        {
            Service.Save();
        }
    };

    public override int Priority { get; set; } = 9;
    public override bool IsExcludedPriority { get; set; } = false;
}