using FFXIVClientStructs.FFXIV.Client.Game;
using System.ComponentModel;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoMultiHook : BaseActionCast {
    public AutoMultiHook() : base(IDs.Actions.MultiHook, ActionType.Action) { }

    public override string GetName() => UIStrings.Multihook;

    protected override string? LabelHelpMarker => UIStrings.Multihook_CosmicHelpText;

    public override int Priority { get; set; } = 0;
    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;

    public override bool CastCondition(WorldState ws) {
        if (!EvaluateConditionSet(ws))
            return false;
        if (ws.Player.HasStatus(IDs.Status.Multihook))
            return false;

        return ws.IsSlottedDutyActionReady(IDs.Actions.MultiHook);
    }

    protected override DrawOptionsDelegate DrawOptions => () => DrawAutoCastConditions();
}
