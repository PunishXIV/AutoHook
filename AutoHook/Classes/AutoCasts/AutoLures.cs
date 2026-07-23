using Dalamud.Bindings.ImGui;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoLures : BaseActionCast {
    [DefaultValue(3)]
    public int LureStacks = 3;
    public bool CancelAttempt;

    public LureTarget LureTarget;

    public AutoLures() : base(IDs.Actions.AmbitiousLure) { }

    public override string GetName() => UIStrings.UseLures;

    private uint StatusId => Id == IDs.Actions.AmbitiousLure ? IDs.Status.AmbitiousLure : IDs.Status.ModestLure;

    public override bool CastCondition() {
        if (Service.WorldState.GetStatusStacks(StatusId) >= LureStacks)
            return false;

        if (Service.WorldState.FishingState is not (FishingState.AmbitiousLure or FishingState.LineInWater))
            return false;

        return EvaluateConditionSet();
    }

    protected override DrawOptionsDelegate DrawOptions => () => {
        DrawUtil.TextV(UIStrings.LureType);
        ImGui.SameLine();

        if (ImGui.RadioButton(UIStrings.AmbitiousLure, Id == IDs.Actions.AmbitiousLure)) {
            Id = IDs.Actions.AmbitiousLure;
            Service.Save();
        }

        ImGui.SameLine();

        if (ImGui.RadioButton(UIStrings.ModestLure, Id == IDs.Actions.ModestLure)) {
            Id = IDs.Actions.ModestLure;
            Service.Save();
        }

        var stack = LureStacks;

        DrawUtil.TextV(UIStrings.AutoLures_Target_Fish);
        ImGui.SameLine();
        if (ImGui.RadioButton(UIStrings.AnyTarget, LureTarget == LureTarget.Any)) {
            LureTarget = LureTarget.Any;
            Service.Save();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton(UIStrings.OnlySpecial, LureTarget == LureTarget.Special)) {
            LureTarget = LureTarget.Special;
            Service.Save();
        }

        ImGui.SameLine();
        DrawUtil.Info($"{UIStrings.SpecialFishExemple} {GameRes.LureFishes.FirstOrDefault()?.Name}");

        ImGui.SameLine();
        if (ImGui.RadioButton(UIStrings.NotSpecial, LureTarget == LureTarget.NotSpecial)) {
            LureTarget = LureTarget.NotSpecial;
            Service.Save();
        }

        if (DrawUtil.EditNumberField(UIStrings.MaxAttempts, ref stack, "", 1)) {
            LureStacks = Math.Clamp(stack, 1, 3);
            Service.Save();
        }

        DrawUtil.Checkbox(UIStrings.CancelAttempt, ref CancelAttempt);

        DrawAutoCastConditions();
    };

    public void TryCasting(bool lureSuccess) {
        if (!EzThrottler.Check("CastingLure"))
            return;

        if (Service.WorldState.GetStatusStacks(StatusId) >= LureStacks && CancelAttempt && !lureSuccess) {
            PlayerRes.CastActionDelayed(IDs.Actions.Rest);
            return;
        }

        if (!IsAvailableToCast() || lureSuccess)
            return;

        if (!PlayerRes.TryCastActionNoDelay(Id, ActionType.Action, GetName()))
            return;

        EzThrottler.Throttle("CastingLure", 2500);
    }

    public override int Priority { get; set; } = 0;
    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;
}
