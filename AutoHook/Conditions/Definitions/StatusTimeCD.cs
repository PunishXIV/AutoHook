using Dalamud.Bindings.ImGui;

namespace AutoHook.Conditions.Definitions;

public sealed class StatusTimeCD : IConditionDefinition {
    public string Id => nameof(StatusTimeCD);
    public string Name => "Status time";
    public ConditionScopeFlags AllowedScopes
        => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast | ConditionScopeFlags.Spearfishing;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var ids = GetStatusIds(parameters);
        var args = GetIntCompareParams(parameters, "sec");
        if (ids.Count == 0) return false;
        var result = ids.Any(id => CompareInt((int)Math.Floor(world.Player.Statuses.TryGetValue(id, out var t) ? t.Time : -1f), args.Value, args.Op));
        return args.Apply(result);
    }

    public void DrawParams(Condition condition) {
        new StatusActiveCD().DrawParams(condition);

        ImGui.SameLine();
        DrawIntCompareParams(condition, "##status_time_op", "Seconds", valueKey: "sec", clamp: v => Math.Max(-1, v), valueWidth: 60);
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters) {
        var names = ConditionParameterFormat.FormatStatusNames(GetStatusIds(parameters));
        return $"{names} time {ConditionParameterFormat.FormatIntCompare(parameters, "sec")}s";
    }

    public bool DescribeOmitsName => true;
}
