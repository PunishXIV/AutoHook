namespace AutoHook.Presets.AutoCasts;

public sealed class AutoSurfaceSlap : BaseActionCast {
    public AutoSurfaceSlap() : base(IDs.Actions.SurfaceSlap, priority: 15) { }

    public override string GetName() => UIStrings.Surface_Slap;

    public override string GetHelpText() => UIStrings.OverridesIdenticalCast;

    public override bool CastCondition(WorldState ws) => EvaluateConditionSet(ws)
        && !ws.Player.HasStatus(IDs.Status.IdenticalCast)
        && !ws.Player.HasStatus(IDs.Status.SurfaceSlap);
}
