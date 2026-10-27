using Newtonsoft.Json;
using System.IO;

namespace AutoHook.Presets.Config;

public partial class Configuration {
    private static readonly JsonSerializerSettings SaveSettings = new() {
        Formatting = Formatting.Indented,
    };

    private static readonly JsonSerializer PlainSerializer = JsonSerializer.Create(SaveSettings);

    private const int SaveDebounceMs = 250;

    // marks where a cached preset goes in the root json. control chars are always escaped inside json strings, so these can't collide with data.
    private const char PresetMarkerStart = '\u0001';
    private const char PresetMarkerEnd = '\u0002';

    private static bool _savePending;
    private static long _savePendingSince;
    private static bool _savingStarted;

    // preset -> its standalone json
    private static Dictionary<CustomPresetConfig, string> _presetJsonCache = [with(ReferenceEqualityComparer.Instance)];
    private static readonly HashSet<CustomPresetConfig> _dirtyPresets = [with(ReferenceEqualityComparer.Instance)];

    private static Task _writeTask = Task.CompletedTask;

    // preset open in the preset editor; can differ from the selected (active) preset. Save() marks it dirty.
    public static CustomPresetConfig? EditingPreset { get; set; }

    // call once after the config is loaded, before the ui can edit it.
    public static void StartSaving() {
        foreach (var preset in C.HookPresets.CustomPresets)
            _presetJsonCache[preset] = SerializePreset(preset);

        IFramework.Get().Update += OnFrameworkUpdate;
        _savingStarted = true;
    }

    public static void StopSaving() {
        if (!_savingStarted)
            return;

        IFramework.Get().Update -= OnFrameworkUpdate;
        _savingStarted = false;
    }

    // ui edits land on the preset being edited or the active one, so both are marked dirty.
    public static void Save() => Save(EditingPreset, C.HookPresets.SelectedPreset);

    // for edits to a preset that may be neither the edited nor the active one.
    public static void Save(CustomPresetConfig? changed) => Save(changed, null);

    private static void Save(CustomPresetConfig? a, CustomPresetConfig? b) {
        if (a != null)
            _dirtyPresets.Add(a);
        if (b != null)
            _dirtyPresets.Add(b);

        if (!_savePending) {
            _savePending = true;
            _savePendingSince = Environment.TickCount64;
        }
    }

    // write pending changes now. rebuildAll re-serializes every preset, correcting any edit that was never marked dirty (used on unload).
    public static Task FlushAsync(bool rebuildAll = false) {
        if (rebuildAll) {
            _presetJsonCache.Clear();
            _savePending = true;
        }

        if (_savePending)
            QueueWrite();
        return _writeTask;
    }

    private static void OnFrameworkUpdate(IFramework _) {
        if (_savePending && Environment.TickCount64 - _savePendingSince >= SaveDebounceMs)
            QueueWrite();
    }

    private static void QueueWrite() {
        _savePending = false;
        var path = Svc.Interface.ConfigFile.FullName;
        var (root, presets) = CaptureSnapshot(C);
        _writeTask = _writeTask.ContinueWith(_ => {
            try {
                WriteToDisk(path, root, presets);
            }
            catch (Exception ex) {
                IPluginLog.Get().Error(ex, "[Configuration] Save failed.");
            }
        }, TaskScheduler.Default);
    }

    // re-serialize dirty/new presets, then the root with markers in place of the custom presets.
    private static (string Root, string[] Presets) CaptureSnapshot(Configuration config) {
        var list = config.HookPresets.CustomPresets;
        var cache = new Dictionary<CustomPresetConfig, string>(list.Count, ReferenceEqualityComparer.Instance);
        var indexes = new Dictionary<CustomPresetConfig, int>(list.Count, ReferenceEqualityComparer.Instance);
        var presets = new string[list.Count];

        for (var i = 0; i < list.Count; i++) {
            var preset = list[i];
            if (_dirtyPresets.Contains(preset) || !_presetJsonCache.TryGetValue(preset, out var json))
                json = SerializePreset(preset);
            cache[preset] = json;
            indexes.TryAdd(preset, i);
            presets[i] = json;
        }

        // rebuilt from the current list, which drops deleted presets.
        _presetJsonCache = cache;
        _dirtyPresets.Clear();

        var root = JsonConvert.SerializeObject(config, new JsonSerializerSettings {
            Formatting = Formatting.Indented,
            Converters = [new PresetMarkerConverter(indexes)],
        });
        return (root, presets);
    }

    private static string SerializePreset(CustomPresetConfig preset)
        => JsonConvert.SerializeObject(preset, SaveSettings);

    private static void WriteToDisk(string path, string root, string[] presets) {
        var tempPath = path + ".new";
        using (var writer = new StreamWriter(tempPath, append: false, Encoding.UTF8))
            WriteSpliced(writer, root, presets);
        File.Move(tempPath, path, overwrite: true);
    }

    // root json with each marker replaced by its cached preset, re-indented to the marker's depth.
    private static void WriteSpliced(TextWriter writer, string root, string[] presets) {
        var pos = 0;
        while (true) {
            var start = root.IndexOf(PresetMarkerStart, pos);
            if (start < 0) {
                writer.Write(root.AsSpan(pos));
                return;
            }

            var end = root.IndexOf(PresetMarkerEnd, start);
            var index = int.Parse(root.AsSpan(start + 1, end - start - 1));
            var lineStart = root.LastIndexOf('\n', start) + 1;
            var indent = root[lineStart..start];

            writer.Write(root.AsSpan(pos, start - pos));
            WriteIndented(writer, presets[index], indent);
            pos = end + 1;
        }
    }

    private static void WriteIndented(TextWriter writer, string json, string indent) {
        var start = 0;
        for (var nl = json.IndexOf('\n'); nl >= 0; nl = json.IndexOf('\n', start)) {
            writer.Write(json.AsSpan(start, nl + 1 - start));
            writer.Write(indent);
            start = nl + 1;
        }
        writer.Write(json.AsSpan(start));
    }

    // writes custom presets as markers; anything else (DefaultPreset) serializes normally.
    private sealed class PresetMarkerConverter(Dictionary<CustomPresetConfig, int> indexes) : JsonConverter {
        public override bool CanRead => false;

        public override bool CanConvert(Type objectType) => objectType == typeof(CustomPresetConfig);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) {
            if (value is CustomPresetConfig preset && indexes.TryGetValue(preset, out var index))
                writer.WriteRawValue($"{PresetMarkerStart}{index}{PresetMarkerEnd}");
            else
                PlainSerializer.Serialize(writer, value);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            => throw new NotSupportedException();
    }
}
