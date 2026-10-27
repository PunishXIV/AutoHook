namespace AutoHook.Conditions.Definitions;

public sealed class SessionTimeCD : IntCompareConditionDefinition {
    public override string Id => nameof(SessionTimeCD);
    public override string Name => "Session time";
    public override ConditionScopeFlags AllowedScopes
        => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    protected override string ValueKey => "min";
    protected override string ValueLabel => "Minutes";
    protected override int DefaultValue => 30;
    protected override string DefaultOp => ">";
    protected override Func<int, int>? Clamp => static v => Math.Max(0, v);

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => world.FishingSessionStartedAt is { } started ? (int)(world.CurrentTime - started).TotalSeconds : 0;

    public override bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var args = GetIntCompareParams(parameters, valueKey: ValueKey, defaultValue: DefaultValue, defaultOp: DefaultOp);
        return args.Apply(CompareInt(ReadValue(world, parameters), args.Value * 60, args.Op));
    }

    public override string DescribeParameters(IReadOnlyDictionary<string, object> parameters) {
        var text = ConditionParameterFormat.FormatIntCompare(parameters, ValueKey, DefaultValue, DefaultOp);
        return string.IsNullOrEmpty(text) ? text : $"{text} min";
    }
}
