using AutoHook.Data;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Bindings.ImGui;

namespace AutoHook.Classes.AutoCasts;

public class AutoPrizeCatch : BaseActionCast
{
    public bool UseWhenMoochIIOnCD = false;

    public bool UseOnlyWithIdenticalCast = false;
    public bool UseOnlyWithActiveSlap = false;

    public override bool DoesCancelMooch() => true;

    public AutoPrizeCatch() : base(UIStrings.Prize_Catch, Data.IDs.Actions.PrizeCatch, ActionType.Action)
    {
        HelpText = UIStrings.Use_Prize_Catch_HelpText;
    }

    public override string GetName()
        => Name = UIStrings.Prize_Catch;

    public override bool CastCondition()
    {
        if (!Enabled)
            return false;

        if (UseWhenMoochIIOnCD && !PlayerRes.ActionOnCoolDown(IDs.Actions.Mooch2))
            return false;
        
        var slapOrIc = true;
        if (UseOnlyWithIdenticalCast || UseOnlyWithActiveSlap)
            slapOrIc = UseOnlyWithIdenticalCast && PlayerRes.HasStatus(IDs.Status.IdenticalCast) ||
                    UseOnlyWithActiveSlap && PlayerRes.HasStatus(IDs.Status.SurfaceSlap);

        if (PlayerRes.HasStatus(IDs.Status.MakeshiftBait))
            return false;

        if (PlayerRes.HasStatus(IDs.Status.PrizeCatch))
            return false;

        if (PlayerRes.HasStatus(IDs.Status.AnglersFortune))
            return false;

        return slapOrIc && PlayerRes.ActionTypeAvailable(IDs.Actions.PrizeCatch);
    }

    protected override DrawOptionsDelegate DrawOptions => () =>
    {
        DrawUtil.Checkbox(UIStrings.AutoCastExtraOptionPrizeCatch,
            ref UseWhenMoochIIOnCD, UIStrings.ExtraOptionPrizeCatchHelpMarker);

        DrawUtil.Checkbox(UIStrings.UseIcActive, ref UseOnlyWithIdenticalCast);

        DrawUtil.Checkbox(UIStrings.UseSlapActive, ref UseOnlyWithActiveSlap);
    };

    public override int Priority { get; set; } = 13;
    public override bool IsExcludedPriority { get; set; } = false;
}