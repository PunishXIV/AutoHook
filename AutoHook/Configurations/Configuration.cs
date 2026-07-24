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
    public const int LatestVersion = 7;

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

    // Got the export/import function from the UnknownX7's ReAction repo
    public static string ExportPreset(BasePresetConfig preset) {
        var exported = CompressString(JsonConvert.SerializeObject(preset, NewExportSettings), true);

        // check if preset is type of AutoGigConfig or CustomPresetConfig
        if (preset is AutoGigConfig)
            return ExportPrefixSf2 + exported;
        else if (preset is CustomPresetConfig)
            return ExportPrefixV7 + exported;

        return "Something went wrong while exporting the preset";
    }

    public class FolderExport(string name) {
        public string FolderName { get; set; } = name;
        public List<CustomPresetConfig> Presets { get; set; } = [];
        public List<FolderExport> ChildFolders { get; set; } = [];
    }

    public static string ExportFolder(PresetFolder folder, List<CustomPresetConfig> presets, List<PresetFolder> allFolders) {
        var folderExport = BuildFolderExport(folder, presets, allFolders);

        var exported = CompressString(JsonConvert.SerializeObject(folderExport, NewExportSettings), true);

        return ExportPrefixFolderV2 + exported;
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
        if (!import.StartsWith(ExportPrefixFolder) && !import.StartsWith(ExportPrefixFolderV2))
            return null;

        try {
            var json = ConfigurationJsonMigrator.MigrateImportedFolderExport(DecompressString(import));
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

        if (import.StartsWith(ExportPrefixSf) || import.StartsWith(ExportPrefixSf2))
            return DeserializePresetImport<AutoGigConfig>(json);

        json = ConfigurationJsonMigrator.MigrateImportedPreset(json);
        return DeserializePresetImport<CustomPresetConfig>(json);
    }

    [NonSerialized] public const string ExportPrefixV2 = "AH_";
    [NonSerialized] public const string ExportPrefixV3 = "AH3_";
    [NonSerialized] public const string ExportPrefixV4 = "AH4_";
    [NonSerialized] public const string ExportPrefixV6 = "AH6_";
    [NonSerialized] public const string ExportPrefixV7 = "AH7_";
    [NonSerialized] public const string ExportPrefixSf = "AHSF1_";
    [NonSerialized] public const string ExportPrefixSf2 = "AHSF2_";
    [NonSerialized] public const string ExportPrefixFolder = "AHFOLDER_";
    [NonSerialized] public const string ExportPrefixFolderV2 = "AHFOLDER2_";

    [NonSerialized]
    public static readonly IReadOnlyList<string> ExportPrefixes =
    [
        ExportPrefixV2,
        ExportPrefixV3,
        ExportPrefixV4,
        ExportPrefixV6,
        ExportPrefixV7,
        ExportPrefixSf,
        ExportPrefixSf2,
        ExportPrefixFolder,
        ExportPrefixFolderV2
    ];

    [NonSerialized]
    private static readonly IReadOnlyList<string> BroccoliExportPrefixes =
    [
        ExportPrefixV7,
        ExportPrefixSf2,
        ExportPrefixFolderV2
    ];

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
        if (!ExportPrefixes.Any(s.StartsWith))
            throw new ApplicationException(UIStrings.DecompressString_Invalid_Import);

        var prefix = ExportPrefixes.First(s.StartsWith);
        var data = Convert.FromBase64String(s[prefix.Length..].Trim());

        using var ms = new MemoryStream(data);
        using Stream decompressor = BroccoliExportPrefixes.Contains(prefix)
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
