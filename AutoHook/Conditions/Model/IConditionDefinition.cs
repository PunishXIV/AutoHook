namespace AutoHook.Conditions.Model;

public interface IConditionDefinition {
    string Id { get; }
    string Name { get; }
    ConditionScopeFlags AllowedScopes { get; }

    // when true, Evaluate uses CastSnapshot while line is in water.
    bool SnapshottableOnCast => false;

    bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters);

    void DrawParams(Condition condition);

    string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => ConditionParameterFormat.FormatGenericParams(parameters);

    bool IsIncomplete(IReadOnlyDictionary<string, object> parameters) => false;
}

public static class ConditionDefinitionExtensions {
    public static ConditionTypeDef ToTypeDef(this IConditionDefinition def)
        => new() {
            Id = def.Id,
            Name = def.Name,
            AllowedScopes = def.AllowedScopes,
            Evaluate = def.Evaluate,
            DrawParams = def.DrawParams,
            Definition = def,
        };
}
