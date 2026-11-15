namespace AutoHook.Conditions.Definitions;

public sealed class OceanTimeOfDayCD : IConditionDefinition {
    public string Name => "Ocean time of day";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    private const int DefaultTod = (int)TimeOfDay.Day;

    private static readonly (int, string)[] Times = [((int)TimeOfDay.Day, "Day"), ((int)TimeOfDay.Sunset, "Sunset"), ((int)TimeOfDay.Night, "Night")];

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var wanted = (TimeOfDay)GetInt(parameters, "tod", DefaultTod);
        var result = world.OceanFishing.TimeOfDay == wanted;
        return GetBool(parameters, "inv", false) ? !result : result;
    }

    public void DrawParams(Condition condition) {
        // if this isn't set, it'll default to TimeOfDay.None, which will never be true
        if (!condition.Params.ContainsKey("tod"))
            condition.Params["tod"] = (long)DefaultTod;

        DrawChoiceParam(condition, "tod", "Time of day", Math.Clamp(GetInt(condition.Params, "tod", DefaultTod), 1, 3), Times);
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => (TimeOfDay)GetInt(parameters, "tod", DefaultTod) switch {
            TimeOfDay.Sunset => "sunset",
            TimeOfDay.Night => "night",
            _ => "day",
        };
}
