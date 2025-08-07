using AutoHook.Data;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using Dalamud.Bindings.ImGui;
using System;
using System.Linq;
using AutoHook.Enums;
using ECommons.Throttlers;
using static AutoHook.Enums.FishingState;

namespace AutoHook.Classes.AutoCasts;

public class AutoLures : BaseActionCast
{
    public int LureStacks = 3;
    public bool CancelAttempt;

    public LureTarget LureTarget;

    public AutoLures() : base(UIStrings.UseLures, IDs.Actions.AmbitiousLure)
    { }

    public bool OnlyWhenActiveSlap;
    public bool OnlyWhenNotActiveSlap;

    public bool OnlyWhenActiveIdentical;
    public bool OnlyWhenNotActiveIdentical;
    public bool OnlyCastLarge;

    public override string GetName()
        => Name = UIStrings.UseLures;

    private uint StatusId => Id == IDs.Actions.AmbitiousLure ? IDs.Status.AmbitiousLure : IDs.Status.ModestLure;

    public override bool CastCondition()
    {
        if (PlayerRes.GetStatusStacks(StatusId) >= LureStacks)
            return false;

        if (Service.BaitManager.FishingState is not (NormalFishing or LureFishing))
            return false;

        if (OnlyCastLarge && !PlayerRes.HasAnyStatus([IDs.Status.AnglersFortune, IDs.Status.PrizeCatch]))
            return false;

        if (OnlyWhenActiveIdentical && !PlayerRes.HasStatus(IDs.Status.IdenticalCast))
            return false;

        if (OnlyWhenNotActiveIdentical && PlayerRes.HasStatus(IDs.Status.IdenticalCast))
            return false;

        if (OnlyWhenActiveSlap && !PlayerRes.HasStatus(IDs.Status.SurfaceSlap))
            return false;

        if (OnlyWhenNotActiveSlap && PlayerRes.HasStatus(IDs.Status.SurfaceSlap))
            return false;

        return true;
    }


    protected override DrawOptionsDelegate DrawOptions => () =>
    {
        DrawUtil.TextV(UIStrings.LureType);
        ImGui.SameLine();

        if (ImGui.RadioButton(UIStrings.AmbitiousLure, Id == IDs.Actions.AmbitiousLure))
        {
            Id = IDs.Actions.AmbitiousLure;
            Service.Save();
        }

        ImGui.SameLine();

        if (ImGui.RadioButton(UIStrings.ModestLure, Id == IDs.Actions.ModestLure))
        {
            Id = IDs.Actions.ModestLure;
            Service.Save();
        }

        var stack = LureStacks;

        DrawUtil.TextV(UIStrings.AutoLures_Target_Fish);
        ImGui.SameLine();
        if (ImGui.RadioButton(UIStrings.AnyTarget, LureTarget == LureTarget.Any))
        {
            LureTarget = LureTarget.Any;
            Service.Save();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton(UIStrings.OnlySpecial, LureTarget == LureTarget.Special))
        {
            LureTarget = LureTarget.Special;
            Service.Save();
        }

        ImGui.SameLine();
        DrawUtil.Info($"{UIStrings.SpecialFishExemple} {GameRes.LureFishes.FirstOrDefault()?.Name}");

        if (DrawUtil.EditNumberField(UIStrings.MaxAttempts, ref stack, "", 1))
        {
            // value has to be between 3 and 10
            LureStacks = Math.Clamp(stack, 1, 3);
            Service.Save();
        }

        DrawUtil.Checkbox(UIStrings.CancelAttempt, ref CancelAttempt);
        DrawUtil.Checkbox(UIStrings.OnlyCastLarge, ref OnlyCastLarge);

        DrawUtil.DrawTreeNodeEx(UIStrings.Surface_Slap_Options, DrawSurfaceSwap);
        DrawUtil.DrawTreeNodeEx(UIStrings.Identical_Cast_Options, DrawIdenticalCast);
    };

    public void TryCasting(bool lureSuccess)
    {
        if (!EzThrottler.Check("CastingLure"))
            return;

        if (PlayerRes.GetStatusStacks(StatusId) >= LureStacks && CancelAttempt && !lureSuccess)
        {
            PlayerRes.CastActionDelayed(IDs.Actions.Rest);
            return;
        }

        if (!IsAvailableToCast() || lureSuccess)
            return;

        PlayerRes.CastActionDelayed(Id);
        EzThrottler.Throttle("CastingLure", 2500);
    }

    private void DrawSurfaceSwap()
    {
        ImGui.Indent();

        if (DrawUtil.Checkbox(UIStrings.LureSSActive, ref OnlyWhenActiveSlap))
        {
            OnlyWhenNotActiveSlap = false;
            Service.Save();
        }

        if (DrawUtil.Checkbox(UIStrings.LureSSNotActive, ref OnlyWhenNotActiveSlap))
        {
            OnlyWhenActiveSlap = false;
            Service.Save();
        }

        ImGui.Unindent();
    }

    private void DrawIdenticalCast()
    {
        ImGui.Indent();

        if (DrawUtil.Checkbox(UIStrings.LureICActive, ref OnlyWhenActiveIdentical))
        {
            OnlyWhenNotActiveIdentical = false;
            Service.Save();
        }

        if (DrawUtil.Checkbox(UIStrings.LureICNotActive, ref OnlyWhenNotActiveIdentical))
        {
            OnlyWhenActiveIdentical = false;
            Service.Save();
        }

        ImGui.Unindent();
    }

    public override int Priority { get; set; } = 0;
    public override bool IsExcludedPriority { get; set; } = true;
}