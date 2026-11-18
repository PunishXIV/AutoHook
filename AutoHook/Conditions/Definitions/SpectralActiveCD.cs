namespace AutoHook.Conditions.Definitions;

public sealed class SpectralActiveCD : BoolInvertConditionDefinition {
    public override string Name => "Spectral current";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;
    public override bool SnapshottableOnCast => true;

    protected override bool ReadValue(WorldState world)
        => world.SpectralCurrentStatus == SpectralCurrentStatus.Active;

    protected override bool ReadSnapshotValue(CastInfoSnapshot snapshot)
        => snapshot.SpectralCurrentStatus == SpectralCurrentStatus.Active;
}
