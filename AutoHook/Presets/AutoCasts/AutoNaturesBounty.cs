namespace AutoHook.Presets.AutoCasts;

public sealed class AutoNaturesBounty : BaseActionCast {
    public AutoNaturesBounty(bool isSpearfishing = true) : base(IDs.Actions.NaturesBounty, excludedPriority: true) {
        IsSpearFishing = isSpearfishing;
    }

    public override int DefaultGpThreshold => 100;

    public override string GetName() => UIStrings.Use_Natures_Bounty;

    public override bool CastCondition(WorldState ws)
        => EvaluateConditionSet(ws) && !ws.Player.HasStatus(IDs.Status.NaturesBounty);
}
