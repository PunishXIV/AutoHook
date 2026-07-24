using FFXIVClientStructs.FFXIV.Client.Game;
using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoSparefulHand : BaseActionCast {
    public AutoSparefulHand() : base(IDs.Actions.SparefulHand, ActionType.Action) { }

    public override string GetName() => UIStrings.SparefulHand;

    public override string GetHelpText() => UIStrings.SparefulHand_HelpText;

    public uint? FishIdToCheck { get; set; }

    public override bool CastCondition() {
        var ws = Service.WorldState;
        if (FishIdToCheck is { } fishId)
            ws.SwimbaitEvaluationFishId = fishId;
        try {
            return EvaluateConditionSet();
        }
        finally {
            ws.SwimbaitEvaluationFishId = 0;
        }
    }

    protected override DrawOptionsDelegate? DrawOptions => () => DrawAutoCastConditions(showSubPrefix: false);

    [DefaultValue(20)]
    public override int Priority { get; set; } = 20;
    public override bool IsExcludedPriority { get; set; } = false;
}
