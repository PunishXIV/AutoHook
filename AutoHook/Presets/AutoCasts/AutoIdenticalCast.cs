namespace AutoHook.Presets.AutoCasts;

public sealed class AutoIdenticalCast : BaseActionCast {
    public AutoIdenticalCast() : base(IDs.Actions.IdenticalCast, priority: 8) { }

    public override string GetName() => UIStrings.Identical_Cast;

    public override string GetHelpText() => UIStrings.OverridesSurfaceSlap;

    public override bool CastCondition(WorldState ws) => EvaluateConditionSet(ws)
        && !ws.Player.HasStatus(IDs.Status.IdenticalCast)
        && !ws.Player.HasStatus(IDs.Status.SurfaceSlap);
}
