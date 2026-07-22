using Newtonsoft.Json;

namespace AutoHook.Conditions;

// conditions AND/OR'd. (X or Y) in "(X or Y) AND (A or B)".
public class ConditionGroup {
    [JsonProperty("m")]
    public ConditionCombineMode CombineMode { get; set; } = ConditionCombineMode.All;

    [JsonProperty("c")]
    public List<Condition> Conditions { get; set; } = [];

    // false = skipped in eval (toggle without deleting).
    [JsonProperty("a")]
    public bool Enabled { get; set; } = true;

    public bool Evaluate(WorldState world, ConditionRegistry registry) {
        if (!Enabled) return true;
        var active = Conditions.Where(c => c.Enabled).ToList();
        if (active.Count == 0) return true;

        return CombineMode == ConditionCombineMode.All ? active.All(c => c.Evaluate(world, registry)) : active.Any(c => c.Evaluate(world, registry));
    }

    public (bool Result, List<(string Id, bool Result)> Trace) EvaluateWithTrace(WorldState world, ConditionRegistry registry) {
        var trace = new List<(string, bool)>();
        if (!Enabled)
            return (true, trace);

        var active = Conditions.Where(c => c.Enabled).ToList();
        if (active.Count == 0)
            return (true, trace);

        if (CombineMode == ConditionCombineMode.All) {
            var all = true;
            foreach (var c in active) {
                var (r, t) = c.EvaluateWithTrace(world, registry);
                trace.AddRange(t);
                if (!r)
                    all = false;
            }
            return (all, trace);
        }

        var any = false;
        foreach (var c in active) {
            var (r, t) = c.EvaluateWithTrace(world, registry);
            trace.AddRange(t);
            if (r)
                any = true;
        }
        return (any, trace);
    }

    public (bool Result, List<(string Label, bool Result)> Trace) DescribeWithTrace(WorldState world, ConditionRegistry registry) {
        var trace = new List<(string, bool)>();
        if (!Enabled)
            return (true, trace);

        var active = Conditions.Where(c => c.Enabled).ToList();
        if (active.Count == 0)
            return (true, trace);

        if (CombineMode == ConditionCombineMode.All) {
            var all = true;
            foreach (var c in active) {
                var r = c.Evaluate(world, registry);
                trace.Add((c.Describe(registry), r));
                if (!r)
                    all = false;
            }
            return (all, trace);
        }

        var any = false;
        foreach (var c in active) {
            var r = c.Evaluate(world, registry);
            trace.Add((c.Describe(registry), r));
            if (r)
                any = true;
        }
        return (any, trace);
    }
}
