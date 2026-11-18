namespace AutoHook.Conditions.Definitions;

public sealed class SpectralInZoneCD : IConditionDefinition {
    public string Name => "Spectral in zone";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    private static readonly (int, string)[] Zones = [(0, "Zone 1"), (1, "Zone 2"), (2, "Zone 3")];

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var zone = GetInt(parameters, "zone", 0);
        var result = world.SpectralHistory.Any(r => r.ZoneIndex == zone);
        return GetBool(parameters, "inv", false) ? !result : result;
    }

    public void DrawParams(Condition condition)
        => DrawChoiceParam(condition, "zone", "Zone", Math.Clamp(GetInt(condition.Params, "zone", 0), 0, 2), Zones);

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => $"zone {GetInt(parameters, "zone", 0) + 1}";
}
