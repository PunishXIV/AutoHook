using Dalamud.Configuration;
using Newtonsoft.Json;
using System.ComponentModel;
using System.IO.Compression;
using System.IO;
using AutoHook.Configurations.old_config;
using AutoHook.Spearfishing;

namespace AutoHook.Configurations;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 5;
    public string CurrentLanguage { get; set; } = @"en";

    public bool HideLocButtonn = true;

    [DefaultValue(true)] public bool PluginEnabled = true;

    public FishingPresets HookPresets = new();

    public SpearFishingPresets AutoGigConfig = new();

    public bool ShowDebugConsole = false;

    [DefaultValue(true)] public bool ShowChatLogs = true;

    public int DelayBetweenCastsMin = 600;
    public int DelayBetweenCastsMax = 1000;

    public int DelayBetweenHookMin = 100;
    public int DelayBetweenHookMax = 200;

    public int DelayBeforeCancelMin = 1500;
    public int DelayBeforeCancelMax = 2000;

    [DefaultValue(true)] public bool ShowStatus = true;
    public bool ShowPresetsAsSidebar = false;

    public bool HideTabDescription = false;

    public bool SwapToButtons = false;
    public int SwapType;

    [DefaultValue(true)] public bool DontHideOptionsDisabled = true;

    [DefaultValue(true)] public bool ResetAfkTimer = true;

    [DefaultValue(false)] public bool AutoStartFishing = false;

    // old config
    public List<BaitPresetConfig> BaitPresetList = [];

    public void Save()
    {
        Svc.PluginInterface!.SavePluginConfig(this);
    }

    public void UpdateVersion()
    {
        if (Version == 1)
        {
            Version = 2;
        }

        if (Version == 2)
        {
            try
            {
                foreach (var preset in BaitPresetList)
                {
                    var newPreset = ConvertOldPreset(preset);
                    if (newPreset != null)
                        HookPresets.CustomPresets.Add(newPreset);
                }

                Version = 3;
            }
            catch (Exception e)
            {
                Service.PrintDebug(@$"[Configuration] {e.Message}");
            }
        }

        if (Version == 3)
        {
            Service.PrintDebug(@$"[Configuration] Updating to v4");

            Save();
            Version = 4;
        }

        if (Version == 4)
        {
            Service.PrintDebug(@$"[Configuration] Updating to v5");

            foreach (var gig in AutoGigConfig.Presets)
            {
                Service.PrintDebug($"Renaming {gig.PresetName} to {gig.Name}");
                gig.PresetName = gig.Name;
            }

            HookPresets.DefaultPreset.PresetName = Service.GlobalPresetName;

            Save();
            Version = 5;
        }
    }

    private static void SetFieldNewClass(HookConfig newOne, BaitConfig old)
    {
        var oldType = old.GetType();
        var newType = newOne.GetType();

        var oldFields = oldType.GetFields();
        var newFields = newType.GetFields();

        foreach (var sourceField in oldFields)
        {
            var targetField =
                newFields.FirstOrDefault(f => f.Name == sourceField.Name && f.FieldType == sourceField.FieldType);
            if (targetField != null)
            {
                var value = sourceField.GetValue(old);
                targetField.SetValue(newOne, value);
            }
        }
    }

    public void Initiate()
    {
        if (HookPresets.DefaultPreset.ListOfBaits.Count != 0)
            return;

        var bait = new BaitFishClass(UIStrings.All_Baits, 0);
        var mooch = new BaitFishClass(UIStrings.All_Mooches, 0);

        HookPresets.DefaultPreset.ListOfBaits.Add(new HookConfig(bait));
        HookPresets.DefaultPreset.ListOfMooch.Add(new HookConfig(mooch));
    }

    public static Configuration Load()
    {
        try
        {
            if (Svc.PluginInterface.GetPluginConfig() is Configuration config)
            {
                config.Initiate();
                config.UpdateVersion();
                config.Save();
                return config;
            }

            config = new Configuration();
            config.Initiate();
            config.Save();
            return config;
        }
        catch (Exception e)
        {
            Svc.Log.Error(@$"[Configuration] {e.Message}");
            throw;
        }
    }

    public static void ResetConfig()
    {
    }

    // Got the export/import function from the UnknownX7's ReAction repo
    /*public static string ExportPreset(CustomPresetConfig preset)
    {
        return CompressString(JsonConvert.SerializeObject(preset,
            new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore }));
    }*/

    public static string ExportPreset(BasePresetConfig preset)
    {
        var exported = CompressString(JsonConvert.SerializeObject(preset,
            new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore }));

        // check if preset is type of AutoGigConfig or CustomPresetConfig
        if (preset is AutoGigConfig)
            return ExportPrefixSf + exported;
        else if (preset is CustomPresetConfig)
            return ExportPrefixV4 + exported;

        return "Something went wrong while exporting the preset";
    }

    public class FolderExport(string name)
    {
        public string FolderName { get; set; } = name;
        public List<CustomPresetConfig> Presets { get; set; } = [];
    }

    public static string ExportFolder(PresetFolder folder, List<CustomPresetConfig> presets)
    {
        var folderExport = new FolderExport(folder.FolderName);

        foreach (var presetId in folder.PresetIds)
        {
            var preset = presets.FirstOrDefault(p => p.UniqueId == presetId);
            if (preset != null)
            {
                folderExport.Presets.Add(preset);
            }
        }

        var exported = CompressString(JsonConvert.SerializeObject(folderExport,
            new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore }));

        return ExportPrefixFolder + exported;
    }

    public static (PresetFolder Folder, List<CustomPresetConfig> Presets)? ImportFolder(string import)
    {
        if (!import.StartsWith(ExportPrefixFolder))
            return null;

        try
        {
            var folderData = JsonConvert.DeserializeObject<FolderExport>(DecompressString(import),
                new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });

            if (folderData == null)
                return null;

            var folder = new PresetFolder(folderData.FolderName);

            // Generate new GUIDs for all presets to avoid conflicts
            foreach (var preset in folderData.Presets)
            {
                preset.UniqueId = Guid.NewGuid();
                folder.AddPreset(preset.UniqueId);
            }

            return (folder, folderData.Presets);
        }
        catch (Exception e)
        {
            Svc.Log.Error($"Failed to import folder: {e.Message}");
            return null;
        }
    }

    public static BasePresetConfig? ImportPreset(string import)
    {
        if (import.StartsWith(ExportPrefixV2))
        {
            var old = JsonConvert.DeserializeObject<BaitPresetConfig>(DecompressString(import),
                new JsonSerializerSettings() { ObjectCreationHandling = ObjectCreationHandling.Replace });
            return ConvertOldPreset(old);
        }

        if (import.StartsWith(ExportPrefixV3))
        {
            var old = JsonConvert.DeserializeObject<OldPresetConfig>(DecompressString(import),
                new JsonSerializerSettings() { ObjectCreationHandling = ObjectCreationHandling.Replace });

            return ConvertOldPresetV3(old);
        }

        if (import.StartsWith(ExportPrefixSf))
        {
            var autogig = JsonConvert.DeserializeObject<AutoGigConfig>(DecompressString(import),
                new JsonSerializerSettings() { ObjectCreationHandling = ObjectCreationHandling.Replace });

            return autogig;
        }

        var importActionStack = JsonConvert.DeserializeObject<CustomPresetConfig>(DecompressString(import),
            new JsonSerializerSettings() { ObjectCreationHandling = ObjectCreationHandling.Replace });
        return importActionStack;
    }

    [NonSerialized] private const string ExportPrefixV2 = "AH_";
    [NonSerialized] private const string ExportPrefixV3 = "AH3_";
    [NonSerialized] private const string ExportPrefixV4 = "AH4_";
    [NonSerialized] private const string ExportPrefixSf = "AHSF1_";
    [NonSerialized] private const string ExportPrefixFolder = "AHFOLDER_";

    [NonSerialized]
    private static readonly List<string> ExportPrefixes =
    [
        ExportPrefixV2, ExportPrefixV3, ExportPrefixV4, ExportPrefixSf, ExportPrefixFolder
    ];

    public static string CompressString(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        using var ms = new MemoryStream();
        using (var gs = new GZipStream(ms, CompressionMode.Compress))
            gs.Write(bytes, 0, bytes.Length);

        return Convert.ToBase64String(ms.ToArray());
    }

    public static string DecompressString(string s)
    {
        if (!ExportPrefixes.Any(s.StartsWith))
            throw new ApplicationException(UIStrings.DecompressString_Invalid_Import);

        var prefix = ExportPrefixes.First(s.StartsWith);
        var data = Convert.FromBase64String(s[prefix.Length..]);
        var lengthBuffer = new byte[4];
        Array.Copy(data, data.Length - 4, lengthBuffer, 0, 4);
        var uncompressedSize = BitConverter.ToInt32(lengthBuffer, 0);

        var buffer = new byte[uncompressedSize];
        using (var ms = new MemoryStream(data))
        {
            using var gzip = new GZipStream(ms, CompressionMode.Decompress);
            gzip.ReadExactly(buffer, 0, uncompressedSize);
        }

        return Encoding.UTF8.GetString(buffer);
    }

    public static string DecompressBase64(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            using var compressedStream = new MemoryStream(bytes);
            using var zipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var resultStream = new MemoryStream();
            zipStream.CopyTo(resultStream);
            bytes = resultStream.ToArray();
            return Encoding.UTF8.GetString(bytes, 1, bytes.Length - 1);
        }
        catch (Exception e)
        {
            Svc.Log.Error(@$"Failed to DecompressBase64: {e.Message}");
            return "";
        }
    }

    private static CustomPresetConfig? ConvertOldPreset(BaitPresetConfig? preset)
    {
        if (preset == null)
            return null;

        var filteredBaits = new List<HookConfig>();
        var filteredMooch = new List<HookConfig>();
        foreach (var old in preset.ListOfBaits)
        {
            var matchingBait = GameRes.Baits.FirstOrDefault(b => b.Name == old.BaitName);
            var matchingFish = GameRes.Fishes.FirstOrDefault(f => f.Name == old.BaitName);

            if (matchingBait != null)
            {
                var newOne = new HookConfig(matchingBait);
                SetFieldNewClass(newOne, old);
                filteredBaits.Add(newOne);
            }
            else if (matchingFish != null)
            {
                var newOne = new HookConfig(matchingFish);
                SetFieldNewClass(newOne, old);
                filteredMooch.Add(newOne);
            }
        }

        CustomPresetConfig newPreset = new(@$"[Old Version] {preset.PresetName}")
        {
            ListOfBaits = filteredBaits,
            ListOfMooch = filteredMooch
        };
        return newPreset;
    }

    private static CustomPresetConfig? ConvertOldPresetV3(OldPresetConfig? old)
    {
        if (old == null)
            return null;

        var newPreset = new CustomPresetConfig(old.PresetName);

        Service.PrintDebug($"Converting v3 to v4: {old.PresetName}");
        foreach (var bait in old.ListOfBaits)
        {
            bait.ConvertV3ToV4();

            var newBait = new HookConfig(bait.BaitFish)
            {
                Enabled = bait.Enabled,
                NormalHook = bait.NormalHook,
                IntuitionHook = bait.IntuitionHook
            };
            newBait.IntuitionHook.UseCustomStatusHook = bait.UseCustomIntuitionHook;

            newPreset.AddItem(newBait);
        }

        foreach (var mooch in old.ListOfMooch)
        {
            mooch.ConvertV3ToV4();
            var newMooch = new HookConfig(mooch.BaitFish)
            {
                Enabled = mooch.Enabled,
                NormalHook = mooch.NormalHook,
                IntuitionHook = mooch.IntuitionHook
            };
            newMooch.IntuitionHook.UseCustomStatusHook = mooch.UseCustomIntuitionHook;

            newPreset.AddItem(newMooch);
        }

        newPreset.ListOfFish = old.ListOfFish;
        newPreset.ExtraCfg = old.ExtraCfg;
        newPreset.AutoCastsCfg = old.AutoCastsCfg;

        return newPreset;
    }
}