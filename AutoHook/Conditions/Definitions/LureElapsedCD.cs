using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AutoHook.Conditions.Definitions;

public sealed class LureElapsedCD : IConditionDefinition {
    public string Name => "Lure elapsed";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.AutoCast;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        if (world.Fishing.LastLureCastBiteTime is null)
            return true;

        var args = GetParams(parameters);
        var elapsed = world.Fishing.BiteInfo.BiteTimeSeconds - world.Fishing.LastLureCastBiteTime.Value;
        return args.Apply(CompareDouble(elapsed, args.Seconds, args.Op));
    }

    public void DrawParams(Condition condition) {
        var args = GetParams(condition.Params);
        var label = args.Op is ">" or ">=" or "<" or "<=" or "=" ? args.Op : ">=";

        ImGui.SetNextItemWidth(50.Scaled());
        using (var combo = ImRaii.Combo("##lure_elapsed_op", label)) {
            if (combo.Success) {
                foreach (var choice in new[] { ">", ">=", "<", "<=", "=" }) {
                    if (!ImGui.Selectable(choice, choice == args.Op))
                        continue;

                    args = args with { Op = choice };
                    condition.Params = args.ToParams();
                }
            }
        }

        ImGui.SameLine();
        var sec = (float)args.Seconds;
        ImGui.SetNextItemWidth(80.Scaled());
        if (ImGui.InputFloat("Seconds", ref sec, 0.1f, 1f, "%.1f")) {
            sec = Math.Max(0f, sec);
            args = args with { Seconds = sec };
            condition.Params = args.ToParams();
        }
    }

    private readonly record struct LureElapsedParams(double Seconds, string Op, bool Invert) {
        public bool Apply(bool result) => Invert ? !result : result;

        public Dictionary<string, object> ToParams() {
            var dict = new Dictionary<string, object> {
                ["sec"] = Seconds,
            };
            if (!string.IsNullOrEmpty(Op) && Op != ">=")
                dict["op"] = Op;
            if (Invert)
                dict["inv"] = true;
            return dict;
        }
    }

    private static LureElapsedParams GetParams(IReadOnlyDictionary<string, object> p) {
        var sec = GetDouble(p, "sec", 0);
        var op = GetOp(p, "op", ">=");
        var inv = GetBool(p, "inv", false);
        return new LureElapsedParams(sec, op, inv);
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters)
        => ConditionParameterFormat.FormatDoubleCompare(parameters, valueKey: "sec", defaultOp: ">=");
}
