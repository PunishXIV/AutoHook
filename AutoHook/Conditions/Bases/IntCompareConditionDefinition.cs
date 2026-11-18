namespace AutoHook.Conditions.Bases;

public abstract class IntCompareConditionDefinition : IConditionDefinition {
    public string Id => GetType().Name;
    public abstract string Name { get; }
    public abstract ConditionScopeFlags AllowedScopes { get; }

    protected virtual string ValueKey => "val";
    protected virtual int DefaultValue => 0;
    protected virtual string DefaultOp => ">=";
    protected virtual string ValueLabel => "Value";
    protected virtual float ValueWidth => 80f;
    protected virtual Func<int, int>? Clamp => null;

    protected abstract int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters);

    protected virtual bool? InactiveResult(WorldState world, IReadOnlyDictionary<string, object> parameters) => null;

    public virtual bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        if (InactiveResult(world, parameters) is bool inactive)
            return inactive;

        var args = GetIntCompareParams(parameters, valueKey: ValueKey, defaultValue: DefaultValue, defaultOp: DefaultOp);
        return args.Apply(CompareInt(ReadValue(world, parameters), args.Value, args.Op));
    }

    public virtual void DrawParams(Condition condition)
        => DrawIntCompareParams(condition, $"##{Id}_op", ValueLabel, valueKey: ValueKey, defaultValue: DefaultValue, defaultOp: DefaultOp, clamp: Clamp, valueWidth: ValueWidth);

    public virtual string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => ConditionParameterFormat.FormatIntCompare(parameters, ValueKey, DefaultValue, DefaultOp);

    public int? SpecifiedIntDefault(string key) => key == ValueKey ? DefaultValue : null;
}
