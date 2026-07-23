using FFXIVClientStructs.FFXIV.Client.Game;
using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoSurfaceSlap : BaseActionCast {
    public AutoSurfaceSlap() : base(IDs.Actions.SurfaceSlap, ActionType.Action) { }

    public override string GetName() => UIStrings.Surface_Slap;

    public override string GetHelpText() => UIStrings.OverridesIdenticalCast;

    public override bool CastCondition() => EvaluateConditionSet()
        && !Service.WorldState.HasStatus(IDs.Status.IdenticalCast)
        && !Service.WorldState.HasStatus(IDs.Status.SurfaceSlap);

    protected override DrawOptionsDelegate DrawOptions => () => DrawAutoCastConditions();

    [DefaultValue(15)]
    public override int Priority { get; set; } = 15;
    public override bool IsExcludedPriority { get; set; } = false;
}
