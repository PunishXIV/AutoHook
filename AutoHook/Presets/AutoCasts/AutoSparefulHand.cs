namespace AutoHook.Presets.AutoCasts;

public sealed class AutoSparefulHand : BaseActionCast {
    public AutoSparefulHand() : base(IDs.Actions.SparefulHand, priority: 20) { }

    public override string GetName() => UIStrings.SparefulHand;

    public override string GetHelpText() => UIStrings.SparefulHand_HelpText;

    public uint? FishIdToCheck { get; set; }

    public override bool CastCondition(WorldState ws) {
        if (FishIdToCheck is { } fishId)
            ws.SwimbaitEvaluationFishId = fishId;
        try {
            return EvaluateConditionSet(ws);
        }
        finally {
            ws.SwimbaitEvaluationFishId = 0;
        }
    }
}
