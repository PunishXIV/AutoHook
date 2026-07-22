using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoHook.FishSolver;

internal static class EmbeddedDataLoader {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Load<T>(string fileName) {
        var assembly = typeof(EmbeddedDataLoader).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource '{fileName}' not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Could not open embedded resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidOperationException($"Failed to deserialize '{fileName}'.");
    }
}
