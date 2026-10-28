using System.ComponentModel;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoVeteranTrade : BaseActionCast {
    public AutoVeteranTrade(bool isSpearfishing = true) : base(IDs.Actions.VeteranTrade) {
        IsSpearFishing = isSpearfishing;
    }

    public override int DefaultGpThreshold => 200;

    public override string GetName() => UIStrings.VeteranTrade;

    public override bool CastCondition(WorldState ws)
        => EvaluateConditionSet(ws) && !ws.Player.HasStatus(IDs.Status.VeteranTrade);

    protected override DrawOptionsDelegate DrawOptions => () => DrawAutoCastConditions();

    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;
}
