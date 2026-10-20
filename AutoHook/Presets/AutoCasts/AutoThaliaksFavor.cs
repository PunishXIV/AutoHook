using FFXIVClientStructs.FFXIV.Client.Game;

using System.ComponentModel;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoThaliaksFavor : BaseActionCast {
    [DefaultValue(3)]
    public int ThaliaksFavorStacks = 3;
    [DefaultValue(150)]
    public int ThaliaksFavorRecover = 150;

    public AutoThaliaksFavor(bool isSpearfishing = false) : base(IDs.Actions.ThaliaksFavor, ActionType.Action) {
        IsSpearFishing = isSpearfishing;
    }

    public override string GetName() => UIStrings.Thaliaks_Favor;

    protected override int? AnglersArtStacksToUse => ThaliaksFavorStacks;

    public override string GetHelpText() => UIStrings.TabAutoCasts_DrawThaliaksFavor_HelpText;

    public override bool RestoresGp => true;

    public override bool CastCondition(WorldState ws) {
        if (!EvaluateConditionSet(ws))
            return false;

        var hasStacks = ws.Player.GetStatusStacks(IDs.Status.AnglersArt) >= ThaliaksFavorStacks;
        var notOvercaped = ws.Player.CurrentGp + ThaliaksFavorRecover < ws.Player.MaxGp;

        return hasStacks && notOvercaped;
    }

    protected override DrawOptionsDelegate DrawOptions => () => {
        var stack = ThaliaksFavorStacks;
        if (DrawUtil.EditNumberField(UIStrings.TabAutoCasts_DrawExtraOptionsThaliaksFavor_, ref stack)) {
            ThaliaksFavorStacks = Math.Max(3, Math.Min(stack, 10));
            Configuration.Save();
        }
        DrawAutoCastConditions();
    };

    [DefaultValue(16)]
    public override int Priority { get; set; } = 16;
    public override bool IsExcludedPriority { get; set; } = false;
}
