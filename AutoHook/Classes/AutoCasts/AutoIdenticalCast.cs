using System;
using AutoHook.Data;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Bindings.ImGui;

namespace AutoHook.Classes.AutoCasts;

public class AutoIdenticalCast : BaseActionCast
{
    public bool OnlyUseUnderPatience;

    public bool OnlyWhenCordialAvailable;

    public bool OnlyUseAfterXAmount;
    public int CaughtAmountLimit = 1;

    public override bool DoesCancelMooch() => true;
    
    public AutoIdenticalCast() : base(UIStrings.Identical_Cast, IDs.Actions.IdenticalCast, ActionType.Action)
    {
        HelpText = UIStrings.OverridesSurfaceSlap;
    }

    public override string GetName()
        => Name = UIStrings.UseIdenticalCast;

    public override bool CastCondition()
    {
        if (PlayerRes.HasStatus(IDs.Status.IdenticalCast) || PlayerRes.HasStatus(IDs.Status.SurfaceSlap))
            return false;

        if (OnlyWhenCordialAvailable && PlayerRes.ActionOnCoolDown(IDs.Item.HiCordial, ActionType.Item))
            return false;
        
        if (OnlyUseUnderPatience && !PlayerRes.HasStatus(IDs.Status.AnglersFortune))
            return false;

        return true;
    }

    public bool IsAvailableToCast(int caughtAmount)
    {
        if (OnlyUseAfterXAmount && caughtAmount < CaughtAmountLimit)
            return false;
        
        return base.IsAvailableToCast();
    }
    
    protected override DrawOptionsDelegate DrawOptions => () =>
    {
        if (DrawUtil.Checkbox(UIStrings.Only_When_Patience_Active, ref OnlyUseUnderPatience))
        {
            Service.Save();
        }
        
        if (DrawUtil.Checkbox(UIStrings.Only_use_when_Cordial_is_available, ref OnlyWhenCordialAvailable))
        {
            Service.Save();
        }
        
        var stack = CaughtAmountLimit;

        if (DrawUtil.Checkbox(UIStrings.Only_use_when_the_fish_is_caught, ref OnlyUseAfterXAmount))
        {
            Service.Save();
        }
        
        ImGui.SameLine();
        
        ImGui.SetNextItemWidth(30);
        if (ImGui.InputInt(UIStrings.TimeS, ref stack, 0, 0))
        {
            CaughtAmountLimit = Math.Max(1, Math.Min(stack, 999));
            Service.Save();
        }
        
        if (DrawUtil.Checkbox(UIStrings.Dont_Cancel_Mooch, ref DontCancelMooch,
                UIStrings.IdenticalCast_HelpText, true))
        {
            Service.Save();
        }
    };

    public override int Priority { get; set; } = 8;
    public override bool IsExcludedPriority { get; set; } = false;
}