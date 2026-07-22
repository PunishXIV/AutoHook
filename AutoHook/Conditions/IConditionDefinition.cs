using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;

namespace AutoHook.Conditions;

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

    public static List<uint> GetIds(IReadOnlyDictionary<string, object> p) {
        if (!p.TryGetValue("ids", out var o)) return [];
        if (o is List<object> list)
            return [.. list.Select(x => Convert.ToUInt32(x))];
        if (o is long single) return [(uint)single];
        return [];
    }

    public static List<uint> GetStatusIds(IReadOnlyDictionary<string, object> p) => GetIds(p);

    public static List<uint> GetWeatherIds(IReadOnlyDictionary<string, object> p) {
        if (!p.TryGetValue("ids", out var o)) return [];
        if (o is List<object> list)
            return [.. list.Select(x => Convert.ToByte(x))];
        if (o is long single) return [(byte)single];
        return [];
    }

    public static bool GetBool(IReadOnlyDictionary<string, object> p, string key, bool def) {
        if (!p.TryGetValue(key, out var o)) return def;
        if (o is bool b) return b;
        if (o is long l) return l != 0;
        return def;
    }

    public static int GetInt(IReadOnlyDictionary<string, object> p, string key, int def)
        => !p.TryGetValue(key, out var o) ? def : Convert.ToInt32(o);

    public static uint GetUInt(IReadOnlyDictionary<string, object> p, string key, uint def)
        => !p.TryGetValue(key, out var o) ? def : Convert.ToUInt32(o);

    public static double GetDouble(IReadOnlyDictionary<string, object> p, string key, double def)
        => !p.TryGetValue(key, out var o) ? def : Convert.ToDouble(o);

    public static string GetOp(IReadOnlyDictionary<string, object> p, string key, string def)
        => !p.TryGetValue(key, out var o) || o == null ? def : o.ToString() ?? def;

    public static bool CompareInt(int lhs, int rhs, string op) {
        return op switch {
            ">" => lhs > rhs,
            ">=" => lhs >= rhs,
            "<" => lhs < rhs,
            "<=" => lhs <= rhs,
            "=" => lhs == rhs,
            _ => lhs >= rhs,
        };
    }

    public static bool CompareDouble(double lhs, double rhs, string op) {
        return op switch {
            ">" => lhs > rhs,
            ">=" => lhs >= rhs,
            "<" => lhs < rhs,
            "<=" => lhs <= rhs,
            "=" => lhs == rhs,
            _ => lhs >= rhs,
        };
    }

    public static void DrawIdsParams(Condition cond, string label) {
        var ids = GetIds(cond.Params);
        var text = string.Join(", ", ids);
        var buf = text;
        ImGui.SetNextItemWidth(140.Scaled());
        if (!ImGui.InputText(label, ref buf, 128))
            return;

        var list = new List<object>();
        foreach (var part in buf.Split(',', StringSplitOptions.RemoveEmptyEntries)) {
            if (uint.TryParse(part.Trim(), out var id))
                list.Add((long)id);
        }

        if (list.Count > 0)
            cond.Params["ids"] = list;
        else
            cond.Params.Remove("ids");
    }

    public static List<(double min, double max)> GetRanges(IReadOnlyDictionary<string, object> p) {
        if (!p.TryGetValue("r", out var o) || o is not List<object> list) return [];
        var result = new List<(double, double)>();
        for (var i = 0; i + 1 < list.Count; i += 2)
            result.Add((Convert.ToDouble(list[i]), Convert.ToDouble(list[i + 1])));
        return result;
    }

    public readonly record struct RangeParams(IReadOnlyList<(double Min, double Max)> Ranges, bool Invert) {
        public bool Apply(bool result) => Invert ? !result : result;

        public Dictionary<string, object> ToParams() {
            var dict = new Dictionary<string, object>();
            if (Ranges.Count > 0) {
                var list = new List<object>(Ranges.Count * 2);
                foreach (var (min, max) in Ranges) {
                    list.Add(min);
                    list.Add(max);
                }
                dict["r"] = list;
            }
            if (Invert)
                dict["inv"] = true;
            return dict;
        }
    }

    public static RangeParams GetRangeParams(IReadOnlyDictionary<string, object> p) {
        var ranges = GetRanges(p);
        var inv = GetBool(p, "inv", false);
        return new RangeParams(ranges, inv);
    }

    public readonly record struct IntCompareParams(int Value, string Op, bool Invert) {
        public bool Apply(bool result) => Invert ? !result : result;

        public Dictionary<string, object> ToParams(string valueKey = "val", string defaultOp = ">=") {
            var dict = new Dictionary<string, object> {
                [valueKey] = (long)Value,
            };
            if (!string.IsNullOrEmpty(Op) && Op != defaultOp)
                dict["op"] = Op;
            if (Invert)
                dict["inv"] = true;
            return dict;
        }
    }

    public static IntCompareParams GetIntCompareParams(
        IReadOnlyDictionary<string, object> p,
        string valueKey = "val",
        int defaultValue = 0,
        string defaultOp = ">=") {
        var value = GetInt(p, valueKey, defaultValue);
        var op = GetOp(p, "op", defaultOp);
        var inv = GetBool(p, "inv", false);
        return new IntCompareParams(value, op, inv);
    }

    public static void DrawIntCompareParams(
        Condition condition,
        string comboId,
        string valueLabel,
        string valueKey = "val",
        int defaultValue = 0,
        string defaultOp = ">=",
        float valueWidth = 80f,
        Func<int, int>? clamp = null) {
        clamp ??= static v => v;
        var args = GetIntCompareParams(condition.Params, valueKey, defaultValue, defaultOp);
        var val = args.Value;
        var label = args.Op is ">" or ">=" or "<" or "<=" or "=" ? args.Op : defaultOp;

        ImGui.SetNextItemWidth(50.Scaled());
        using var combo = ImRaii.Combo(comboId, label);
        if (combo) {
            foreach (var choice in new[] { ">", ">=", "<", "<=", "=" }) {
                if (!ImGui.Selectable(choice, choice == args.Op))
                    continue;

                ApplyIntCompareParams(condition, args with { Op = choice }, valueKey, defaultOp);
            }
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(valueWidth.Scale());
        if (ImGui.InputInt(valueLabel, ref val)) {
            val = clamp(val);
            ApplyIntCompareParams(condition, args with { Value = val }, valueKey, defaultOp);
        }
    }

    private static void ApplyIntCompareParams(
        Condition condition,
        IntCompareParams args,
        string valueKey,
        string defaultOp) {
        condition.Params[valueKey] = (long)args.Value;
        if (args.Op != defaultOp)
            condition.Params["op"] = args.Op;
        else
            condition.Params.Remove("op");

        if (args.Invert)
            condition.Params["inv"] = true;
        else
            condition.Params.Remove("inv");
    }

    public static void DrawSingleRangeParams(Condition condition) {
        var args = GetRangeParams(condition.Params);
        var ranges = args.Ranges;
        var min = ranges.Count > 0 ? ranges[0].Min : 0;
        var max = ranges.Count > 0 ? ranges[0].Max : 0;

        ImGui.SetNextItemWidth(80f.Scale());
        if (ImGui.InputDouble("Min", ref min, 0.1, 1, "%.1f")) {
            var list = ranges.Count == 0 ? new List<(double, double)>() : [.. ranges];
            if (list.Count == 0)
                list.Add((min, max));
            else
                list[0] = (min, max);
            args = args with { Ranges = list };
            condition.Params = args.ToParams();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f.Scale());
        if (ImGui.InputDouble("Max (0 = no cap)", ref max, 0.1, 1, "%.1f")) {
            var list = ranges.Count == 0 ? new List<(double, double)>() : [.. ranges];
            if (list.Count == 0)
                list.Add((min, max));
            else
                list[0] = (min, max);
            args = args with { Ranges = list };
            condition.Params = args.ToParams();
        }
    }
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

