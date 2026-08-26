using AutoHook.Conditions;
using AutoHook.Configurations.Legacy;
using AutoHook.Spearfishing;
using Dalamud.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.IO.Compression;

namespace AutoHook.Configurations;

[Serializable]
public partial class Configuration : IPluginConfiguration {
    public const int LatestVersion = 9;

    public int Version { get; set; } = LatestVersion;
    public string CurrentLanguage { get; set; } = @"en";

    public bool HideLocButton = true;
    public bool PluginEnabled = true;
    public FishingPresets HookPresets = new();
    public SpearFishingPresets AutoGigConfig = new();
    public bool ShowDebugConsole = false;
    public bool ShowChatLogs = true;

    public int DelayBetweenCastsMin = 600;
    public int DelayBetweenCastsMax = 1000;

    public int DelayBetweenHookMin = 100;
    public int DelayBetweenHookMax = 200;

    public int DelayBeforeCancelMin = 1500;
    public int DelayBeforeCancelMax = 2000;

    public bool ShowStatus = true;
    public bool ShowPresetsAsSidebar = false;

    public bool HideTabDescription = false;

    public bool SwapToButtons = false;
    public int SwapType;

    public bool DontHideOptionsDisabled = true;
    public bool ResetAfkTimer = true;
    public bool BlockInputWhileFishing = false;
    public bool AutoStartFishing = false;
    public bool AutoOceanFish = false;
    public OceanFishGoalKind AutoOceanFishGoal = OceanFishGoalKind.Points;
    public bool AOF_Fallthrough = false;
    public bool SpectralRest = false;
    public bool DtrBarEnabled = false;
    public bool DtrPresetBarEnabled = false;

    public bool AutoCollectablesEnabled = true;
    public ConditionSet? AutoCollectablesConditions { get; set; }

    private void WriteVersionBackup(int fromVersion) {
        try {
            var dir = Svc.Interface.GetPluginConfigDirectory();
            var fileName = $"autohook_v{fromVersion}_backup.json";
            var path = Path.Combine(dir, fileName);

            if (File.Exists(path)) {
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                path = Path.Combine(dir, $"autohook_v{fromVersion}_backup_{stamp}.json");
            }

            var json = JsonConvert.SerializeObject(this, new JsonSerializerSettings { Formatting = Formatting.Indented, DefaultValueHandling = DefaultValueHandling.Include });

            File.WriteAllText(path, json, Encoding.UTF8);
            Service.PrintDebug(@$"[Configuration] Wrote backup to {path}");
        }
        catch (Exception e) {
            Svc.Log.Warning(@$"[Configuration] Failed to write v{fromVersion} backup: {e.Message}");
        }
    }

    public void Initiate() {
        if (HookPresets.DefaultPreset.ListOfBaits.Count != 0)
            return;

        var bait = new BaitFishClass(UIStrings.All_Baits, 0);
        var mooch = new BaitFishClass(UIStrings.All_Mooches, 0);

        HookPresets.DefaultPreset.ListOfBaits.Add(new HookConfig(bait));
        HookPresets.DefaultPreset.ListOfMooch.Add(new HookConfig(mooch));
    }

    private static readonly JsonSerializerSettings NewExportSettings = new() {
        DefaultValueHandling = DefaultValueHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore
    };

    public readonly record struct ExportSchema(int Version, string Prefix, bool UseBrotli = false);

    // legacy prefixes
    [NonSerialized] public const string ExportPrefixV2 = "AH_";
    [NonSerialized] public const string ExportPrefixV3 = "AH3_";

    private static readonly ExportSchema[] FishingPresetSchemas =
    [
        new(4, "AH4_"),
        new(6, "AH6_"),
        new(7, "AH7_", UseBrotli: true),
        new(8, "AH8_", UseBrotli: true),
        new(9, "AH9_", UseBrotli: true),
    ];

    private static readonly ExportSchema[] FolderExportSchemas =
    [
        new(1, "AHFOLDER_"),
        new(2, "AHFOLDER2_", UseBrotli: true),
        new(3, "AHFOLDER3_", UseBrotli: true),
        new(4, "AHFOLDER4_", UseBrotli: true),
    ];

    private static readonly ExportSchema[] SpearfishingSchemas =
    [
        new(1, "AHSF1_"),
        new(2, "AHSF2_", UseBrotli: true),
    ];

    public static ExportSchema LatestFishingPresetSchema => FishingPresetSchemas[^1];
    public static ExportSchema LatestFolderExportSchema => FolderExportSchemas[^1];
    public static ExportSchema LatestSpearfishingSchema => SpearfishingSchemas[^1];

    [NonSerialized]
    public static readonly IReadOnlyList<string> ExportPrefixes =
    [
        ExportPrefixV2,
        ExportPrefixV3,
        .. FishingPresetSchemas.Select(s => s.Prefix),
        .. SpearfishingSchemas.Select(s => s.Prefix),
        .. FolderExportSchemas.Select(s => s.Prefix),
    ];

    private static readonly ExportSchema[] AllVersionedSchemas =
    [
        .. FishingPresetSchemas,
        .. FolderExportSchemas,
        .. SpearfishingSchemas,
    ];

    public static bool TryGetFishingPresetSchema(string import, out ExportSchema schema)
        => TryMatchSchema(import, FishingPresetSchemas, out schema);

    public static bool TryGetFolderExportSchema(string import, out ExportSchema schema)
        => TryMatchSchema(import, FolderExportSchemas, out schema);

    public static bool TryGetSpearfishingSchema(string import, out ExportSchema schema)
        => TryMatchSchema(import, SpearfishingSchemas, out schema);

    public static bool IsFolderExport(string import)
        => TryGetFolderExportSchema(import, out _);

    public static bool IsSpearfishingExport(string import)
        => TryGetSpearfishingSchema(import, out _);

    private static bool TryMatchSchema(string import, ExportSchema[] schemas, out ExportSchema schema) {
        // longest prefix first
        foreach (var candidate in schemas.OrderByDescending(s => s.Prefix.Length)) {
            if (import.StartsWith(candidate.Prefix)) {
                schema = candidate;
                return true;
            }
        }

        schema = default;
        return false;
    }

    private static bool TryResolveExportPrefix(string import, out string prefix, out bool useBrotli) {
        if (import.StartsWith(ExportPrefixV2)) {
            prefix = ExportPrefixV2;
            useBrotli = false;
            return true;
        }

        if (import.StartsWith(ExportPrefixV3)) {
            prefix = ExportPrefixV3;
            useBrotli = false;
            return true;
        }

        foreach (var candidate in AllVersionedSchemas.OrderByDescending(s => s.Prefix.Length)) {
            if (import.StartsWith(candidate.Prefix)) {
                prefix = candidate.Prefix;
                useBrotli = candidate.UseBrotli;
                return true;
            }
        }

        prefix = "";
        useBrotli = false;
        return false;
    }

    // Got the export/import function from the UnknownX7's ReAction repo
    public static string ExportPreset(BasePresetConfig preset) {
        var json = JsonConvert.SerializeObject(preset, NewExportSettings);

        if (preset is AutoGigConfig) {
            var schema = LatestSpearfishingSchema;
            return schema.Prefix + CompressString(json, schema.UseBrotli);
        }

        if (preset is CustomPresetConfig) {
            var schema = LatestFishingPresetSchema;
            return schema.Prefix + CompressString(json, schema.UseBrotli);
        }

        return "Something went wrong while exporting the preset";
    }

    public class FolderExport(string name) {
        public string FolderName { get; set; } = name;
        public List<CustomPresetConfig> Presets { get; set; } = [];
        public List<FolderExport> ChildFolders { get; set; } = [];
    }

    public static string ExportFolder(PresetFolder folder, List<CustomPresetConfig> presets, List<PresetFolder> allFolders) {
        var folderExport = BuildFolderExport(folder, presets, allFolders);
        var schema = LatestFolderExportSchema;
        var exported = CompressString(JsonConvert.SerializeObject(folderExport, NewExportSettings), schema.UseBrotli);
        return schema.Prefix + exported;
    }

    private static FolderExport BuildFolderExport(PresetFolder folder, List<CustomPresetConfig> presets, List<PresetFolder> allFolders) {
        var folderExport = new FolderExport(folder.FolderName);

        foreach (var presetId in folder.PresetIds) {
            var preset = presets.FirstOrDefault(p => p.UniqueId == presetId);
            if (preset != null)
                folderExport.Presets.Add(preset);
        }

        foreach (var childFolder in allFolders.Where(f => f.ParentFolderId == folder.UniqueId))
            folderExport.ChildFolders.Add(BuildFolderExport(childFolder, presets, allFolders));

        return folderExport;
    }

    private static T? DeserializePresetImport<T>(string json, bool applyLegacyDefaults = false) where T : class {
        var token = JToken.Parse(json);
        var result = token.ToObject<T>(JsonSerializer.Create(new() { ObjectCreationHandling = ObjectCreationHandling.Replace }));
        if (result != null && applyLegacyDefaults)
            LegacyDefaults.Apply(token, result);
        return result;
    }

    public static (PresetFolder Folder, List<PresetFolder> Folders, List<CustomPresetConfig> Presets)? ImportFolder(string import) {
        import = import.Trim();
        if (!TryGetFolderExportSchema(import, out var schema))
            return null;

        try {
            var json = DecompressString(import);
            if (schema.Version < LatestFolderExportSchema.Version) {
                var fromVer = schema.Version switch {
                    <= 1 => 0, // before condition sets
                    2 => 7, // before lure-nesting
                    3 => 8, // before ActionCooldownCD chk field
                    _ => LatestFishingPresetSchema.Version
                };
                json = ConfigurationJsonMigrator.MigrateImportedFolderExport(json, fromVer);
            }
            var folderData = DeserializePresetImport<FolderExport>(json);

            if (folderData == null)
                return null;

            var allFolders = new List<PresetFolder>();
            var allPresets = new List<CustomPresetConfig>();
            var root = ImportFolderExport(folderData, null, allFolders, allPresets);

            return (root, allFolders, allPresets);
        }
        catch (Exception e) {
            Svc.Log.Error($"Failed to import folder: {e.Message}");
            return null;
        }
    }

    private static PresetFolder ImportFolderExport(FolderExport data, Guid? parentFolderId, List<PresetFolder> allFolders, List<CustomPresetConfig> allPresets) {
        var folder = new PresetFolder(data.FolderName) {
            ParentFolderId = parentFolderId
        };

        foreach (var preset in data.Presets) {
            preset.UniqueId = Guid.NewGuid();
            folder.AddPreset(preset.UniqueId);
            allPresets.Add(preset);
        }

        allFolders.Add(folder);

        foreach (var child in data.ChildFolders ?? [])
            ImportFolderExport(child, folder.UniqueId, allFolders, allPresets);

        return folder;
    }

    public static BasePresetConfig? ImportPreset(string import) {
        import = import.Trim();
        var json = DecompressString(import);

        if (import.StartsWith(ExportPrefixV2)) {
            var old = DeserializePresetImport<BaitPresetConfig>(json, applyLegacyDefaults: true);
            return old == null ? null : LegacyPresetMapper.ConvertOldPreset(old);
        }

        if (import.StartsWith(ExportPrefixV3)) {
            var old = DeserializePresetImport<OldPresetConfig>(json, applyLegacyDefaults: true);
            return old == null ? null : LegacyPresetMapper.ConvertOldPresetV3(old);
        }

        if (TryGetSpearfishingSchema(import, out _))
            return DeserializePresetImport<AutoGigConfig>(json);

        if (!TryGetFishingPresetSchema(import, out var schema))
            return null;

        json = ConfigurationJsonMigrator.MigrateImportedPreset(json, schema.Version);
        return DeserializePresetImport<CustomPresetConfig>(json);
    }

    public static string CompressString(string s, bool useBrotli = false) {
        var bytes = Encoding.UTF8.GetBytes(s);
        using var ms = new MemoryStream();
        using (Stream compressor = useBrotli
                   ? new BrotliStream(ms, CompressionLevel.SmallestSize)
                   : new GZipStream(ms, CompressionMode.Compress))
            compressor.Write(bytes, 0, bytes.Length);

        return Convert.ToBase64String(ms.ToArray());
    }

    public static string DecompressString(string s) {
        s = s.Trim();
        if (!TryResolveExportPrefix(s, out var prefix, out var useBrotli))
            throw new ApplicationException(UIStrings.DecompressString_Invalid_Import);

        var data = Convert.FromBase64String(s[prefix.Length..].Trim());

        using var ms = new MemoryStream(data);
        using Stream decompressor = useBrotli
            ? new BrotliStream(ms, CompressionMode.Decompress)
            : new GZipStream(ms, CompressionMode.Decompress);
        using var result = new MemoryStream();
        decompressor.CopyTo(result);
        return Encoding.UTF8.GetString(result.ToArray());
    }

    public static string DecompressBase64(string base64) {
        try {
            var bytes = Convert.FromBase64String(base64);
            using var compressedStream = new MemoryStream(bytes);
            using var zipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var resultStream = new MemoryStream();
            zipStream.CopyTo(resultStream);
            bytes = resultStream.ToArray();
            return Encoding.UTF8.GetString(bytes, 1, bytes.Length - 1);
        }
        catch (Exception e) {
            Svc.Log.Error(@$"Failed to DecompressBase64: {e.Message}");
            return "";
        }
    }
}
