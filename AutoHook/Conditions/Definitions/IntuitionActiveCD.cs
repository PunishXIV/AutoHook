namespace AutoHook.Conditions.Definitions;

public sealed class IntuitionActiveCD : BoolInvertConditionDefinition {
    public override string Name => "Fisher's Intuition";
    public override ConditionScopeFlags AllowedScopes
        => ConditionScopeFlags.Hook | ConditionScopeFlags.AutoCordial | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;
    public override bool SnapshottableOnCast => true;

    protected override bool ReadValue(WorldState world)
        => world.Fishing.Intuition.IsActive;

    protected override bool ReadSnapshotValue(CastInfoSnapshot snapshot)
        => snapshot.IntuitionStatus is IntuitionStatus.Active or IntuitionStatus.Gained;
}
