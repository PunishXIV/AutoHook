using Dalamud.Bindings.ImGui;

namespace AutoHook.Conditions.Definitions;

public sealed class HookCountCD : IConditionDefinition, ISimpleConditionValue<(bool Enabled, int Limit)> {
    public string Name => "Hook count";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var guid = GetGuid(parameters);
        var args = GetIntCompareParams(parameters, defaultValue: 1);
        if (guid == Guid.Empty)
            return args.Invert;

        var count = FishingCounters.GetFishCount(guid);
        return args.Apply(CompareInt(count, args.Value, args.Op));
    }

    public void DrawParams(Condition condition) {
        ImGui.SetNextItemWidth(70.Scaled());
        var guidText = GetGuidString(condition.Params);
        if (ImGui.InputText("Hook GUID", ref guidText, 128) && Guid.TryParse(guidText, out var guid))
            condition.Params["guid"] = guid.ToString();

        ImGui.SameLine();
        DrawIntCompareParams(condition, "##hook_count_op", "Count", defaultValue: 1, clamp: v => Math.Max(1, v), valueWidth: 60);
    }

    (bool Enabled, int Limit) ISimpleConditionValue<(bool Enabled, int Limit)>.FromParams(IReadOnlyDictionary<string, object> p)
        => (true, Math.Max(1, GetInt(p, "val", 1)));

    IReadOnlyDictionary<string, object>? ISimpleConditionValue<(bool Enabled, int Limit)>.ToParams((bool Enabled, int Limit) value, object? context) {
        if (!value.Enabled)
            return null;

        var dict = new IntCompareParams(value.Limit, ">=", false).ToParams();
        if (context is Guid guid && guid != Guid.Empty)
            dict["guid"] = guid.ToString();
        return dict;
    }

    private static Guid GetGuid(IReadOnlyDictionary<string, object> parameters) {
        if (!parameters.TryGetValue("guid", out var o) || o == null)
            return Guid.Empty;
        return Guid.TryParse(o.ToString(), out var guid) ? guid : Guid.Empty;
    }

    private static string GetGuidString(IReadOnlyDictionary<string, object> parameters) {
        var guid = GetGuid(parameters);
        return guid == Guid.Empty ? string.Empty : guid.ToString();
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => ConditionParameterFormat.FormatIntCompare(parameters, defaultValue: 1);
}
