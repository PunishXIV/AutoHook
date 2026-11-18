namespace AutoHook.Conditions.Definitions;

public sealed class IntuitionEventCD : IConditionDefinition {
    public string Name => "Intuition Event";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var onLoss = GetBool(parameters, "inv", false);
        var status = world.Fishing.Intuition.Status;
        return onLoss ? status == IntuitionStatus.Lost : status == IntuitionStatus.Gained;
    }

    public void DrawParams(Condition condition) { }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => GetBool(parameters, "inv", false) ? "On Loss" : "On Gain";
}
