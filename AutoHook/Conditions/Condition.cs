using Newtonsoft.Json;
using System.Threading;

namespace AutoHook.Conditions;

// one condition: type id + params. only serialized keys stored.
public class Condition {
    [JsonIgnore]
    private static int _nextUiId = 1;

    // registry key, e.g. "StatusActive", "BiteTimer".
    [JsonProperty("t")]
    public string TypeId { get; set; } = "";

    // type-specific params; only non-defaults when serializing.
    [JsonProperty("p")]
    [JsonConverter(typeof(ConditionParamConverter))]
    public Dictionary<string, object> Params { get; set; } = [];

    // false = skipped in eval (toggle without deleting).
    [JsonProperty("e")]
    public bool Enabled { get; set; } = true;

    // ui: stable imgui id so open/closed state doesn't reuse across recreate.
    [JsonIgnore]
    public int UiId { get; set; }

    public void EnsureUiId() {
        if (UiId <= 0)
            UiId = Interlocked.Increment(ref _nextUiId);
    }

    public bool Evaluate(WorldState world, ConditionRegistry registry) {
        if (!Enabled)
            return false;

        if (PresetConditionHelper.IsPresetType(TypeId))
            return PresetConditionHelper.EvaluateFromTypeId(TypeId, world, Params);

        return registry.Get(TypeId) is { } def && def.Evaluate(world, Params);
    }

    public (bool Result, List<(string Id, bool Result)> Trace) EvaluateWithTrace(WorldState world, ConditionRegistry registry) {
        if (!Enabled)
            return (false, []);

        if (PresetConditionHelper.IsPresetType(TypeId)) {
            var r = PresetConditionHelper.EvaluateFromTypeId(TypeId, world, Params);
            return (r, [(TypeId, r)]);
        }

        var result = registry.Get(TypeId) is { } def && def.Evaluate(world, Params);
        return (result, [(TypeId, result)]);
    }

    public string Describe(ConditionRegistry registry) {
        if (string.IsNullOrEmpty(TypeId))
            return "(empty)";

        var name = PresetConditionHelper.IsPresetType(TypeId)
            ? PresetConditionHelper.ResolveDisplayName(TypeId, PresetConditionHelper.GetEvaluationPreset()) ?? TypeId
            : registry.Get(TypeId)?.Name ?? TypeId;

        if (!Enabled)
            return $"{name} [disabled]";

        if (PresetConditionHelper.IsPresetType(TypeId))
            return name;

        var detail = registry.Get(TypeId)?.Definition?.DescribeParameters(Params) ?? ConditionParameterFormat.FormatGenericParams(Params);
        return string.IsNullOrEmpty(detail) ? name : $"{name}: {detail}";
    }
}
