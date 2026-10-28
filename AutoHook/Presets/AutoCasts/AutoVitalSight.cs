using System.ComponentModel;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoVitalSight : BaseActionCast {
    [DefaultValue(2)]
    public int RequiredStacks = 2;

    public AutoVitalSight(bool isSpearfishing = true) : base(IDs.Actions.VitalSight) {
        IsSpearFishing = isSpearfishing;
    }

    public override string GetName() => UIStrings.VitalSight;

    public override bool ShowGpThreshold => false;

    public override bool CastCondition(WorldState ws) {
        if (!EvaluateConditionSet(ws))
            return false;

        if (ws.Player.HasStatus(IDs.Status.VitalSight))
            return false;

        return ws.Player.GetStatusStacks(IDs.Status.AnglersArt) >= RequiredStacks;
    }

    protected override DrawOptionsDelegate DrawOptions => () => {
        var stack = RequiredStacks;
        if (DrawUtil.EditNumberField(UIStrings.TabAutoCasts_DrawExtraOptionsThaliaksFavor_, ref stack)) {
            RequiredStacks = Math.Max(2, Math.Min(stack, 10));
            Configuration.Save();
        }
        DrawAutoCastConditions();
    };

    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;
}
