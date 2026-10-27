using Dalamud.Bindings.ImGui;

namespace AutoHook.Conditions.Definitions;

public sealed class StatusStacksCD : IConditionDefinition {
    public string Id => nameof(StatusStacksCD);
    public string Name => "Status stacks";
    public ConditionScopeFlags AllowedScopes
        => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast | ConditionScopeFlags.Spearfishing;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var ids = GetStatusIds(parameters);
        var args = GetIntCompareParams(parameters, "minStacks", 1);
        if (ids.Count == 0) return false;
        var result = ids.Any(id => CompareInt(world.Player.GetStatusStacks(id), args.Value, args.Op));
        return args.Apply(result);
    }

    public void DrawParams(Condition condition) {
        new StatusActiveCD().DrawParams(condition);

        ImGui.SameLine();
        DrawIntCompareParams(condition, "##stacks_op", "Stacks", valueKey: "minStacks", defaultValue: 1, clamp: v => Math.Max(1, v), valueWidth: 60);
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters) {
        var names = ConditionParameterFormat.FormatStatusNames(GetStatusIds(parameters));
        var stacks = GetInt(parameters, "minStacks", 1);
        return $"{names} stacks {ConditionParameterFormat.FormatIntCompare(parameters, "minStacks", stacks)}";
    }

    public bool DescribeOmitsName => true;
}
