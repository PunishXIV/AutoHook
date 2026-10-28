using Newtonsoft.Json;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Threading;

namespace AutoHook.Conditions.Model;

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
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    // ui: stable imgui id so open/closed state doesn't reuse across recreate.
    [JsonIgnore]
    public int UiId { get; set; }

    public void EnsureUiId() {
        if (UiId <= 0)
            UiId = Interlocked.Increment(ref _nextUiId);
    }

    [OnSerializing]
    private void OnSerializing(StreamingContext _)
        => ConditionParamConverter.PushDefinition(Registry.Get(TypeId)?.Definition);

    [OnSerialized]
    private void OnSerialized(StreamingContext _)
        => ConditionParamConverter.PopDefinition();

    public bool Evaluate(WorldState world, ConditionRegistry registry) {
        if (!Enabled)
            return false;

        if (PresetConditionHelper.IsPresetType(TypeId))
            return PresetConditionHelper.EvaluateFromTypeId(TypeId, world, Params);

        return registry.Get(TypeId) is { } def && def.Evaluate(world, Params);
    }

    public string Describe(ConditionRegistry registry)
        => Enabled ? Summary(registry) : $"{Summary(registry)} [disabled]";

    // like Describe but without the disabled suffix (ui shows that visually).
    public string Summary(ConditionRegistry registry) {
        if (string.IsNullOrEmpty(TypeId))
            return "(empty)";

        var isPreset = PresetConditionHelper.IsPresetType(TypeId);
        var name = isPreset
            ? PresetConditionHelper.ResolveDisplayName(TypeId, PresetConditionHelper.GetEvaluationPreset()) ?? TypeId
            : registry.Get(TypeId)?.Name ?? TypeId;
        var prefix = GetBool(Params, "inv", false) ? "NOT " : "";

        if (isPreset)
            return prefix + name;

        var def = registry.Get(TypeId)?.Definition;
        var detail = def?.DescribeParameters(Params) ?? ConditionParameterFormat.FormatGenericParams(Params);
        if (string.IsNullOrEmpty(detail))
            return prefix + name;
        if (def?.DescribeOmitsName == true)
            return prefix + detail;
        // omit colon for straight comparisons
        return IsComparison(detail) ? $"{prefix}{name} {detail}" : $"{prefix}{name}: {detail}";
    }

    private static bool IsComparison(string detail)
        => detail.Length > 0 && detail[0] is '<' or '>' or '=' or '≤' or '≥' or '!';
}
