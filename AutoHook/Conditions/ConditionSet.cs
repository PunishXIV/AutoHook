using Newtonsoft.Json;

namespace AutoHook.Conditions;

// groups AND/OR'd. empty = always true.
public class ConditionSet {
    // All = AND, Any = OR.
    [JsonProperty("m")]
    public ConditionCombineMode CombineMode { get; set; } = ConditionCombineMode.All;

    // non-empty groups only (minimal config).
    [JsonProperty("g")]
    public List<ConditionGroup> Groups { get; set; } = [];

    // optional expr over groups (A && B && (C || D)). overrides CombineMode when set.
    [JsonProperty("e")]
    public string? Expression { get; set; }

    // ui: expr selection start (token idx).
    [JsonIgnore]
    public int? ExprSelectionStart { get; set; }

    // ui: expr selection end (token idx).
    [JsonIgnore]
    public int? ExprSelectionEnd { get; set; }

    // ui: advanced expr editor expanded.
    [JsonIgnore]
    public bool ExprVisible { get; set; }

    // ui: slim "Advanced" section expanded.
    [JsonIgnore]
    public bool SlimAdvancedExpanded { get; set; }

    public bool Evaluate(WorldState world, ConditionRegistry registry) {
        if (Groups.Count == 0) return true;

        // Evaluate each group once (disabled groups: true for AND, false for OR so they don't affect result)
        var values = new bool[Groups.Count];
        for (var i = 0; i < Groups.Count; i++)
            values[i] = !Groups[i].Enabled
                ? (CombineMode == ConditionCombineMode.All)
                : Groups[i].Evaluate(world, registry);

        // If an expression is provided, try to use it first
        if (!string.IsNullOrWhiteSpace(Expression)) {
            try {
                if (ConditionExpression.TryEvaluate(Expression, values, out var result))
                    return result;
            }
            catch {
                // Fallback to CombineMode
            }
        }

        if (CombineMode == ConditionCombineMode.Any) {
            foreach (var v in values)
                if (v) return true;
            return false;
        }

        foreach (var v in values)
            if (!v) return false;
        return true;
    }

    public (bool Result, List<(string Id, bool Result)> Trace) EvaluateWithTrace(WorldState world, ConditionRegistry registry) {
        if (Groups.Count == 0)
            return (true, []);

        var values = new bool[Groups.Count];
        var trace = new List<(string, bool)>();
        for (var i = 0; i < Groups.Count; i++) {
            if (!Groups[i].Enabled) {
                values[i] = CombineMode == ConditionCombineMode.All;
                continue;
            }

            var (groupResult, groupTrace) = Groups[i].EvaluateWithTrace(world, registry);
            values[i] = groupResult;
            trace.AddRange(groupTrace.Select(t => ($"G{i}:{t.Id}", t.Result)));
        }

        if (!string.IsNullOrWhiteSpace(Expression)) {
            try {
                if (ConditionExpression.TryEvaluate(Expression, values, out var result))
                    return (result, trace);
            }
            catch { }
        }

        if (CombineMode == ConditionCombineMode.Any) {
            foreach (var v in values)
                if (v) return (true, trace);
            return (false, trace);
        }

        foreach (var v in values)
            if (!v) return (false, trace);
        return (true, trace);
    }

    public List<(string Label, bool Result)> DescribeEvaluation(WorldState world, ConditionRegistry registry) {
        if (Groups.Count == 0)
            return [];

        var values = new bool[Groups.Count];
        var trace = new List<(string, bool)>();
        for (var i = 0; i < Groups.Count; i++) {
            if (!Groups[i].Enabled) {
                values[i] = CombineMode == ConditionCombineMode.All;
                continue;
            }

            var (groupResult, groupTrace) = Groups[i].DescribeWithTrace(world, registry);
            values[i] = groupResult;
            trace.AddRange(groupTrace.Select(t => ($"G{i}: {t.Label}", t.Result)));
        }

        if (!string.IsNullOrWhiteSpace(Expression)) {
            try {
                if (ConditionExpression.TryEvaluate(Expression, values, out var result))
                    return trace;
            }
            catch { }
        }

        return trace;
    }
}
