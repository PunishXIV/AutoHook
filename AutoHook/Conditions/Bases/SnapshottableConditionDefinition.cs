namespace AutoHook.Conditions.Bases;

public abstract class SnapshottableConditionDefinition : IConditionDefinition {
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract ConditionScopeFlags AllowedScopes { get; }
    public virtual bool SnapshottableOnCast => true;

    protected abstract bool EvaluateLive(WorldState world, IReadOnlyDictionary<string, object> parameters);
    protected abstract bool EvaluateSnapshot(CastInfoSnapshot snapshot, IReadOnlyDictionary<string, object> parameters);

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => SnapshottableOnCast && world.Fishing.CastSnapshot is { Active: true } snap ? EvaluateSnapshot(snap, parameters) : EvaluateLive(world, parameters);

    public abstract void DrawParams(Condition condition);

    public virtual string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => ConditionParameterFormat.FormatGenericParams(parameters);

    public virtual bool DescribeOmitsName => false;

    public virtual string? IncompleteReason(IReadOnlyDictionary<string, object> parameters) => null;
}
