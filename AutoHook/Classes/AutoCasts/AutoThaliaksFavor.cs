using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoThaliaksFavor : BaseActionCast {
    public int ThaliaksFavorStacks = 3;
    public int ThaliaksFavorRecover = 150;

    public AutoThaliaksFavor(bool isSpearfishing = false) : base(IDs.Actions.ThaliaksFavor, ActionType.Action) {
        IsSpearFishing = isSpearfishing;
    }

    public override string GetName() => UIStrings.Thaliaks_Favor;

    public override string GetHelpText() => UIStrings.TabAutoCasts_DrawThaliaksFavor_HelpText;

    public override bool RestoresGp => true;

    public override bool CastCondition() {
        if (!EvaluateConditionSet())
            return false;

        var hasStacks = Service.WorldState.GetStatusStacks(IDs.Status.AnglersArt) >= ThaliaksFavorStacks;
        var notOvercaped = Service.WorldState.Player.CurrentGp + ThaliaksFavorRecover < Service.WorldState.Player.MaxGp;

        return hasStacks && notOvercaped;
    }

    protected override DrawOptionsDelegate DrawOptions => () => {
        var stack = ThaliaksFavorStacks;
        if (DrawUtil.EditNumberField(UIStrings.TabAutoCasts_DrawExtraOptionsThaliaksFavor_, ref stack)) {
            ThaliaksFavorStacks = Math.Max(3, Math.Min(stack, 10));
            Service.Save();
        }
        DrawAutoCastConditions();
    };

    public override int Priority { get; set; } = 16;
    public override bool IsExcludedPriority { get; set; } = false;
}
