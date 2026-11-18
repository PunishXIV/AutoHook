using Dalamud.Bindings.ImGui;

namespace AutoHook.Conditions.Definitions;

public sealed class MoochAvailableCD : IConditionDefinition {
    public string Name => "Mooch available";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.AutoCast;

    private readonly record struct MoochAvailableParams(bool M1, bool M2, bool Invert) {
        public bool Apply(bool result) => Invert ? !result : result;

        public Dictionary<string, object> ToParams() {
            var dict = new Dictionary<string, object>();
            if (!M1)
                dict["m1"] = false;
            if (!M2)
                dict["m2"] = false;
            if (Invert)
                dict["inv"] = true;
            return dict;
        }
    }

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var args = GetParams(parameters);
        return args.Apply(ReadValue(world, args.M1, args.M2));
    }

    public void DrawParams(Condition condition) {
        var args = GetParams(condition.Params);
        var m1 = args.M1;
        var m2 = args.M2;
        var changed = false;
        if (ImGui.Checkbox(UIStrings.Mooch, ref m1))
            changed = true;
        ImGui.SameLine();
        if (ImGui.Checkbox(UIStrings.Mooch_II, ref m2))
            changed = true;
        if (changed)
            condition.Params = new MoochAvailableParams(m1, m2, args.Invert).ToParams();
    }

    private static MoochAvailableParams GetParams(IReadOnlyDictionary<string, object> p) {
        var m1 = GetBool(p, "m1", true);
        var m2 = GetBool(p, "m2", true);
        var inv = GetBool(p, "inv", false);
        return new MoochAvailableParams(m1, m2, inv);
    }

    private static bool ReadValue(WorldState world, bool m1, bool m2) {
        if (!m1 && !m2)
            return false;

        var prev = world.Fishing.PreviousCatch;
        return (m1 && prev.CanMoochPreviousCatch) || (m2 && prev.CanMooch2PreviousCatch);
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters) => string.Empty;
}
