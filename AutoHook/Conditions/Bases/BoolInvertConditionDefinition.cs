namespace AutoHook.Conditions.Bases;

public abstract class BoolInvertConditionDefinition : IConditionDefinition {
    public string Id => GetType().Name;
    public abstract string Name { get; }
    public abstract ConditionScopeFlags AllowedScopes { get; }

    public virtual bool SnapshottableOnCast => false;

    protected abstract bool ReadValue(WorldState world);

    protected virtual bool ReadSnapshotValue(CastInfoSnapshot snapshot) => false;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var result = SnapshottableOnCast && world.Fishing.CastSnapshot is { Active: true } snap ? ReadSnapshotValue(snap) : ReadValue(world);
        return GetBool(parameters, "inv", false) ? !result : result;
    }

    public void DrawParams(Condition condition) { }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters) => string.Empty;
}
