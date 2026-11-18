using Newtonsoft.Json;
using System.ComponentModel;

namespace AutoHook.Conditions.Model;

// conditions AND/OR'd. (X or Y) in "(X or Y) AND (A or B)".
public class ConditionGroup {
    [JsonProperty("m")]
    public ConditionCombineMode CombineMode { get; set; } = ConditionCombineMode.All;

    [JsonProperty("c")]
    public List<Condition> Conditions { get; set; } = [];

    // false = skipped in eval (toggle without deleting).
    [JsonProperty("a")]
    [DefaultValue(true)]
    public bool Enabled = true;

    public bool Evaluate(WorldState world, ConditionRegistry registry) {
        if (!Enabled) return true;
        var active = Conditions.Where(c => c.Enabled).ToList();
        if (active.Count == 0) return true;

        return CombineMode == ConditionCombineMode.All ? active.All(c => c.Evaluate(world, registry)) : active.Any(c => c.Evaluate(world, registry));
    }

    public IEnumerable<(string Label, bool Result)> DescribeLines(WorldState world, ConditionRegistry registry) {
        if (!Enabled)
            yield break;

        foreach (var c in Conditions.Where(c => c.Enabled))
            yield return (c.Describe(registry), c.Evaluate(world, registry));
    }
}
