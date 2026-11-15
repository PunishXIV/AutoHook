using System.ComponentModel;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoBigGameFishing : BaseActionCast {
    [DefaultValue(2)]
    public int AnglersStacks = 2;

    public AutoBigGameFishing() : base(IDs.Actions.BigGameFishing) { }

    public override string GetName() => UIStrings.BigGameFishing;

    public override bool CastCondition(WorldState ws)
        => EvaluateConditionSet(ws) && !ws.Player.HasStatus(IDs.Status.BigGameFishing) && ws.HasAnglersArtStacks(AnglersStacks);

    protected override DrawOptionsDelegate DrawOptions => () => {
        DrawUtil.EditNumberField(UIStrings.TabAutoCasts_DrawExtraOptionsThaliaksFavor_, ref AnglersStacks, 2, 10, steps: 1);

        DrawAutoCastConditions();
    };

    [DefaultValue(18)]
    public override int Priority { get; set; } = 18;
    public override bool IsExcludedPriority { get; set; } = false;
}
