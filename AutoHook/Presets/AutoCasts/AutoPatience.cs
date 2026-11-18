using Dalamud.Bindings.ImGui;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoPatience : BaseActionCast {
    public int RefreshEarlyTime = 0;

    public override bool RequiresTimeWindow() => true;

    public override bool DoesCancelMooch() => true;

    public AutoPatience() : base(IDs.Actions.Patience2, priority: 12) { }

    public override string GetName() => UIStrings.AutoPatience_Patience;

    public override string GetHelpText() => UIStrings.CancelsCurrentMooch;

    public override bool CastCondition(WorldState ws) {
        if (!EvaluateConditionSet(ws))
            return false;

        if (ws.Player.HasStatus(IDs.Status.AnglersFortune) && ws.Player.GetStatusTime(IDs.Status.AnglersFortune) > RefreshEarlyTime)
            return false;

        if (ws.Player.HasStatus(IDs.Status.MakeshiftBait) || ws.Player.HasStatus(IDs.Status.PrizeCatch))
            return false;

        return true;
    }

    protected override void DrawOptions() {
        DrawUtil.Radio(UIStrings.Patience_I, ref Id, IDs.Actions.Patience);
        DrawUtil.Radio(UIStrings.Patience_II, ref Id, IDs.Actions.Patience2);
        DrawUtil.EditNumberField(UIStrings.RefreshWhenTimeIsLessThanOrEqual, ref RefreshEarlyTime, 0, 999);

        DrawAutoCastConditions();
    }
}
