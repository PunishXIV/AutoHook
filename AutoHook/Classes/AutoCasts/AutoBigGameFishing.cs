using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoBigGameFishing : BaseActionCast {
    [DefaultValue(2)]
    public int AnglersStacks = 2;

    public AutoBigGameFishing() : base(IDs.Actions.BigGameFishing) { }

    public override string GetName() => UIStrings.BigGameFishing;

    public override bool CastCondition() {
        if (!EvaluateConditionSet())
            return false;

        if (Service.WorldState.HasStatus(IDs.Status.BigGameFishing))
            return false;

        return Service.WorldState.HasAnglersArtStacks(AnglersStacks);
    }

    protected override DrawOptionsDelegate DrawOptions => () => {
        var stack = AnglersStacks;
        if (DrawUtil.EditNumberField(UIStrings.TabAutoCasts_DrawExtraOptionsThaliaksFavor_, ref stack, "", 1)) {
            AnglersStacks = Math.Max(2, Math.Min(stack, 10));
            Service.Save();
        }

        DrawAutoCastConditions();
    };

    [DefaultValue(18)]
    public override int Priority { get; set; } = 18;
    public override bool IsExcludedPriority { get; set; } = false;
}
