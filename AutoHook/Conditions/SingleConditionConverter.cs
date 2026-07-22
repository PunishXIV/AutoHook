using Newtonsoft.Json;

namespace AutoHook.Conditions;

// serializes as ConditionSet only — same JSON as ConditionSet?.
public sealed class SingleConditionConverter : JsonConverter {
    public override bool CanConvert(Type objectType)
        => objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(SingleCondition<,>);

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) {
        var set = value == null ? null : GetBackingSet(value);
        serializer.Serialize(writer, set);
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) {
        var set = serializer.Deserialize<ConditionSet>(reader);
        var instance = existingValue ?? Activator.CreateInstance(objectType);
        if (instance != null)
            objectType.GetProperty("BackingSet")!.SetValue(instance, set);
        return instance;
    }

    private static ConditionSet? GetBackingSet(object value)
        => (ConditionSet?)value.GetType().GetProperty("BackingSet")!.GetValue(value);
}
