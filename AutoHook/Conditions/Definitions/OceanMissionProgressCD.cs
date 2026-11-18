using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AutoHook.Conditions.Definitions;

public sealed class OceanMissionProgressCD : IConditionDefinition {
    public string Name => "Ocean mission progress";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var slot = GetInt(parameters, "mission", 1);
        var args = GetIntCompareParams(parameters);
        var of = world.OceanFishing;
        var progress = slot switch {
            2 => of.Mission2.Progress,
            3 => of.Mission3.Progress,
            _ => of.Mission1.Progress,
        };
        var result = CompareInt(progress, args.Value, args.Op);
        return args.Apply(result);
    }

    public void DrawParams(Condition condition) {
        var mission = Math.Clamp(GetInt(condition.Params, "mission", 1), 1, 3);

        ImGui.SetNextItemWidth(60.Scaled());
        using (var combo = ImRaii.Combo("Mission", $"{mission}")) {
            if (combo.Success) {
                if (ImGui.Selectable("1", mission == 1)) condition.Params["mission"] = (long)1;
                if (ImGui.Selectable("2", mission == 2)) condition.Params["mission"] = (long)2;
                if (ImGui.Selectable("3", mission == 3)) condition.Params["mission"] = (long)3;
            }
        }

        ImGui.SameLine();
        DrawIntCompareParams(condition, "##mission_op", "Progress", clamp: v => Math.Max(0, v), valueWidth: 70);
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => ConditionParameterFormat.FormatIntCompare(parameters);
}
