using AutoHook.Conditions;
using AutoHook.Conditions.Definitions;
using AutoHook.Configurations.Legacy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using static AutoHook.Conditions.ConditionRegistry;

namespace AutoHook.Configurations;

/// <summary>
/// Migrates raw JSON up to the latest cfg version before deserializing into <see cref="Configuration"/>
/// </summary>
public static class ConfigurationJsonMigrator {
    public static string MigrateToLatest(string json, string? configDirectory = null) {
        JObject root;
        try {
            root = JObject.Parse(json);
        }
        catch {
            Svc.Log.Warning("Failed to parse config during migration. Using defaults.");
            return json;
        }

        var version = (int?)(root["Version"] ?? 1) ?? 1;
        if (version >= Configuration.LatestVersion)
            return json;

        // v2 -> v3: convert BaitPresetList into HookPresets.CustomPresets
        if (version < 3) {
            MigrateV2ToV3Json(root);
            root["Version"] = 3;
            version = 3;
        }

        // For v3–v4 configs, reuse the existing object-based migrations up to v5.
        if (version < 5) {
            var migratedTo5 = RunRuntimeMigrationsUpTo5(root);
            try {
                root = JObject.Parse(migratedTo5);
            }
            catch {
                return migratedTo5;
            }
            version = (int?)(root["Version"] ?? 5) ?? 5;
        }

        // v5 -> v6: JSON-based, converting all trigger based bools into ConditionSets
        if (version < 6) {
            if (!string.IsNullOrEmpty(configDirectory))
                WriteV5Backup(configDirectory, root);
            MigrateV6(root);
            root["Version"] = 6;
            version = 6;
        }

        // v6 -> v7: swimbait count thresholds, spareful hand swimbait limits, surface slap / identical cast enabled
        if (version < Configuration.LatestVersion) {
            MigrateV7(root);
            root["Version"] = Configuration.LatestVersion;
        }

        return root.ToString(Formatting.None);
    }

    private static void WriteV5Backup(string configDirectory, JObject root) {
        try {
            var path = Path.Combine(configDirectory, "autohook_v5_backup.json");
            if (File.Exists(path)) {
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                path = Path.Combine(configDirectory, $"autohook_v5_backup_{stamp}.json");
            }

            File.WriteAllText(path, root.ToString(Formatting.Indented), Encoding.UTF8);
            Svc.Log.Information(@$"[Configuration] Wrote v5 backup before v6 migration to {path}");
        }
        catch (Exception e) {
            Svc.Log.Warning(@$"[Configuration] Failed to write v5 backup before v6 migration: {e.Message}");
        }
    }

    /// <summary>
    /// Migrates a single exported preset JSON (AH4/AH6/etc.) to the latest preset schema before deserialization.
    /// </summary>
    public static string MigrateImportedPreset(string json) {
        try {
            if (JToken.Parse(json) is not JObject preset)
                return json;

            MigrateImportedPresetObject(preset);
            return preset.ToString(Formatting.None);
        }
        catch {
            return json;
        }
    }

    public static string MigrateImportedFolderExport(string json) {
        try {
            if (JToken.Parse(json) is not JObject root)
                return json;

            MigrateImportedFolderExportObject(root);
            return root.ToString(Formatting.None);
        }
        catch {
            return json;
        }
    }

    private static void MigrateImportedFolderExportObject(JObject folderExport) {
        foreach (var token in EnumerateArray(folderExport["Presets"])) {
            if (token is JObject presetObj)
                MigrateImportedPresetObject(presetObj);
        }

        foreach (var token in EnumerateArray(folderExport["ChildFolders"])) {
            if (token is JObject childObj)
                MigrateImportedFolderExportObject(childObj);
        }
    }

    private static void MigrateImportedPresetObject(JObject preset) {
        MigratePresetExtra(preset);
        MigratePresetConditions(preset);
        MigratePresetSwimbaitCountThreshold(preset);
        MigratePresetSparefulHandSwimbaitLimits(preset);
        MigratePresetFishCaughtActionEnabled(preset);
    }

    private static void MigrateV2ToV3Json(JObject root) {
        if (root["BaitPresetList"] is not JArray baitPresetList || baitPresetList.Count == 0) {
            root.Remove("BaitPresetList");
            return;
        }

        if (root["HookPresets"] is not JObject hookPresets) {
            hookPresets = [];
            root["HookPresets"] = hookPresets;
        }

        if (hookPresets["CustomPresets"] is not JArray customPresets) {
            customPresets = [];
            hookPresets["CustomPresets"] = customPresets;
        }

        var settings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
        foreach (var token in baitPresetList) {
            if (token is not JObject oldObj) continue;
            try {
                var old = oldObj.ToObject<BaitPresetConfig>(JsonSerializer.Create(settings));
                var converted = LegacyPresetMapper.ConvertOldPreset(old);
                if (converted != null)
                    customPresets.Add(JObject.FromObject(converted, JsonSerializer.Create(settings)));
            }
            catch {
                Svc.Log.Warning("Failed to migrate a bait preset during v2->v3 migration. Skipping.");
            }
        }

        root.Remove("BaitPresetList");
    }

    private static string RunRuntimeMigrationsUpTo5(JObject root) {
        Configuration? config;
        try {
            config = root.ToObject<Configuration>(JsonSerializer.Create(new JsonSerializerSettings {
                ObjectCreationHandling = ObjectCreationHandling.Replace
            }));
        }
        catch {
            config = null;
        }

        if (config == null)
            return root.ToString(Formatting.None);

        config.Initiate();
        Configuration.RunMigrationsUpTo(config, 5);

        return JsonConvert.SerializeObject(config, new JsonSerializerSettings {
            Formatting = Formatting.None,
            DefaultValueHandling = DefaultValueHandling.Include
        });
    }

    private static void MigrateV6(JObject root) {
        if (root["HookPresets"] is not JObject hookPresets)
            return;

        static void MigratePreset(JObject? preset) {
            if (preset == null) return;
            MigratePresetExtra(preset);
            MigratePresetConditions(preset);
        }

        MigratePreset(hookPresets["DefaultPreset"] as JObject);

        if (hookPresets["CustomPresets"] is JArray customPresets) {
            foreach (var token in customPresets) {
                if (token is JObject presetObj)
                    MigratePreset(presetObj);
            }
        }
    }

    private static void MigrateV7(JObject root) {
        MigrateSwimbaitCountThresholdToConditions(root);
        MigrateSparefulHandSwimbaitLimits(root);
        MigrateFishCaughtActionEnabledPresets(root);
    }

    private static void MigratePresetConditions(JObject preset) {
        foreach (var hook in EnumerateArray(preset["ListOfBaits"]).Concat(EnumerateArray(preset["ListOfMooch"]))) {
            if (hook is JObject hookObj) {
                MigrateHookConfigJson(hookObj);
                MigrateHookSwimbaitJson(hookObj);
            }
        }
        foreach (var token in EnumerateArray(preset["ListOfFish"])) {
            if (token is JObject fishObj) {
                MigrateFishConfigJson(fishObj);
                MigrateFishConfigStopSwapJson(fishObj);
            }
        }
        if (preset["AutoCastsCfg"] is JObject autoCasts) {
            MigrateAutoCordialJson(autoCasts["CastCordial"] as JObject);
            MigrateAutoCastsTimeWindowJson(autoCasts);
            MigrateAutoCastsLegacyJson(autoCasts);
        }
    }

    private static void MigrateAutoCastsLegacyJson(JObject autoCasts) {
        MigrateCastMoochJson(autoCasts["CastMooch"] as JObject);
        MigrateCastFishEyesJson(autoCasts["CastFishEyes"] as JObject);
        MigrateCastChumJson(autoCasts["CastChum"] as JObject);
        MigrateCastPrizeCatchJson(autoCasts["CastPrizeCatch"] as JObject);
        MigrateCastPatienceJson(autoCasts["CastPatience"] as JObject);
        MigrateCastThaliaksFavorJson(autoCasts["CastThaliaksFavor"] as JObject);
        MigrateCastMakeShiftBaitJson(autoCasts["CastMakeShiftBait"] as JObject);
        MigrateCastLineJson(autoCasts["CastLine"] as JObject);
        MigrateCastBigGameJson(autoCasts["CastBigGame"] as JObject);
        MigrateCastMultihookJson(autoCasts["CastMultihook"] as JObject);
    }

    private static void SetConditionSetIfEmpty(JObject? obj, ConditionSet set) {
        if (obj == null) return;
        var existing = obj["ConditionSet"] as JObject;
        if (existing?["g"] is JArray groups && groups.Count > 0) return;
        obj["ConditionSet"] = JToken.FromObject(set);
    }

    private static Condition ActionOnCd(uint actionId)
        => new() { TypeId = Registry.GetId<ActionCooldownCD>(), Params = new Dictionary<string, object> { ["id"] = (long)actionId, ["type"] = (long)0, ["sec"] = (long)0, ["op"] = ">" } };

    private static Condition ItemOnCd(uint itemId)
        => new() { TypeId = Registry.GetId<ActionCooldownCD>(), Params = new Dictionary<string, object> { ["id"] = (long)itemId, ["type"] = (long)1, ["sec"] = (long)0, ["op"] = ">" } };

    private static ConditionSet Single(Condition c)
        => new() { CombineMode = ConditionCombineMode.All, Groups = [new ConditionGroup { CombineMode = ConditionCombineMode.All, Conditions = [c] }] };

    private static void MigrateCastMoochJson(JObject? obj) {
        if (obj == null || (bool?)obj["OnlyMoochIntuition"] != true) return;
        SetConditionSetIfEmpty(obj, Configuration.ConditionSetBuilder.SingleFlag<IntuitionActiveCD>());
    }

    private static void MigrateCastFishEyesJson(JObject? obj) {
        if (obj == null || (bool?)obj["OnlyWhenMakeShiftUp"] != true) return;
        SetConditionSetIfEmpty(obj, Configuration.ConditionSetBuilder.SingleStatus(IDs.Status.MakeshiftBait));
    }

    private static void MigrateCastChumJson(JObject? obj) {
        if (obj == null) return;
        var onlyIntuition = (bool?)obj["_onlyUseWithIntuition"] == true;
        var exceeds = (int?)(obj["_useWhenIntuitionExceeds"] ?? 0) ?? 0;
        if (!onlyIntuition && exceeds <= 0) return;
        var conditions = new List<Condition>();
        if (onlyIntuition) conditions.Add(Configuration.ConditionSetBuilder.SingleFlag<IntuitionActiveCD>().Groups[0].Conditions[0]);
        if (exceeds > 0) conditions.Add(Configuration.ConditionSetBuilder.SingleStatusStacks(IDs.Status.FishersIntuition, exceeds).Groups[0].Conditions[0]);
        if (conditions.Count == 0) return;
        var set = new ConditionSet { CombineMode = ConditionCombineMode.All, Groups = [new ConditionGroup { CombineMode = ConditionCombineMode.All, Conditions = conditions }] };
        SetConditionSetIfEmpty(obj, set);
    }

    private static void MigrateCastPrizeCatchJson(JObject? obj) {
        if (obj == null) return;
        var conditions = new List<Condition>();
        if ((bool?)obj["UseWhenMoochIIOnCD"] == true) conditions.Add(ActionOnCd(IDs.Actions.Mooch2));
        if ((bool?)obj["UseOnlyWithIdenticalCast"] == true) conditions.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.IdenticalCast));
        if ((bool?)obj["UseOnlyWithActiveSlap"] == true) conditions.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.SurfaceSlap));
        if (conditions.Count == 0) return;
        var set = new ConditionSet { CombineMode = ConditionCombineMode.All, Groups = [new ConditionGroup { CombineMode = ConditionCombineMode.All, Conditions = conditions }] };
        SetConditionSetIfEmpty(obj, set);
    }

    private static void MigrateCastPatienceJson(JObject? obj) {
        if (obj == null || (bool?)obj["UseOnlyWhenMoochIIOnCD"] != true) return;
        SetConditionSetIfEmpty(obj, Single(ActionOnCd(IDs.Actions.Mooch2)));
    }

    private static void MigrateCastThaliaksFavorJson(JObject? obj) {
        if (obj == null || (bool?)obj["UseWhenCordialCD"] != true) return;
        SetConditionSetIfEmpty(obj, Single(ItemOnCd(IDs.Item.Cordial)));
    }

    private static void MigrateCastMakeShiftBaitJson(JObject? obj) {
        if (obj == null) return;
        var conditions = new List<Condition>();
        if ((bool?)obj["_onlyUseWithIntuition"] == true) conditions.Add(Configuration.ConditionSetBuilder.SingleFlag<IntuitionActiveCD>().Groups[0].Conditions[0]);
        if ((bool?)obj["OnlyWhenMoochNotUp"] == true) conditions.Add(Configuration.ConditionSetBuilder.SingleFlag<MoochAvailableCD>(inverse: true).Groups[0].Conditions[0]);
        if ((bool?)obj["UseOnlyWhenMoochIIOnCD"] == true) conditions.Add(ActionOnCd(IDs.Actions.Mooch2));
        if (conditions.Count == 0) return;
        var set = new ConditionSet { CombineMode = ConditionCombineMode.All, Groups = [new ConditionGroup { CombineMode = ConditionCombineMode.All, Conditions = conditions }] };
        SetConditionSetIfEmpty(obj, set);
    }

    private static void MigrateCastLineJson(JObject? obj) {
        if (obj == null) return;
        var conditions = new List<Condition>();
        if ((bool?)obj["OnlyCastWithFishEyes"] == true) conditions.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.FishEyes));
        if ((bool?)obj["OnlyCastLarge"] == true) conditions.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.PrizeCatch));
        if (conditions.Count == 0) return;
        var set = new ConditionSet { CombineMode = ConditionCombineMode.All, Groups = [new ConditionGroup { CombineMode = ConditionCombineMode.All, Conditions = conditions }] };
        SetConditionSetIfEmpty(obj, set);
    }

    private static void MigrateCastBigGameJson(JObject? obj) {
        if (obj == null) return;
        var conditions = new List<Condition>();
        if ((bool?)obj["WithIdenticalC"] == true) conditions.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.IdenticalCast));
        if ((bool?)obj["WithSlap"] == true) conditions.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.SurfaceSlap));
        if (conditions.Count == 0) return;
        var set = new ConditionSet { CombineMode = ConditionCombineMode.All, Groups = [new ConditionGroup { CombineMode = ConditionCombineMode.All, Conditions = conditions }] };
        SetConditionSetIfEmpty(obj, set);
    }

    private static void MigrateCastMultihookJson(JObject? obj) {
        if (obj == null || (bool?)obj["OnlyUseWhenIdenticalCastActive"] != true) return;
        SetConditionSetIfEmpty(obj, Configuration.ConditionSetBuilder.SingleStatus(IDs.Status.IdenticalCast));
    }

    private static IEnumerable<JToken> EnumerateArray(JToken? token) {
        return token is not JArray arr ? [] : arr.OfType<JToken>();
    }

    private static readonly string[] BiteKeys = [
        "TripleWeak", "TripleStrong", "TripleLegendary",
        "DoubleWeak", "DoubleStrong", "DoubleLegendary",
        "PatienceWeak", "PatienceStrong", "PatienceLegendary"
    ];

    private static void MigrateHookConfigJson(JObject hook) {
        MigrateHookStopJson(hook);
        MigrateHooksetJson(hook["NormalHook"] as JObject);
        MigrateHooksetJson(hook["IntuitionHook"] as JObject);
    }

    private static void MigrateHookStopJson(JObject hook) {
        if (hook["StopConditionSet"] is JObject existing && existing["g"] is JArray groups && groups.Count > 0)
            return;

        foreach (var key in new[] { "NormalHook", "IntuitionHook" }) {
            if (hook[key] is not JObject hookset || (bool?)hookset["StopAfterCaught"] != true)
                continue;

            var limit = (int?)(hookset["StopAfterCaughtLimit"] ?? 1) ?? 1;
            if (limit < 1) limit = 1;

            if (hookset["StopFishingStep"] != null)
                hook["StopFishingStep"] = hookset["StopFishingStep"];
            if (hookset["StopAfterResetCount"] != null)
                hook["StopAfterResetCount"] = hookset["StopAfterResetCount"];

            var hookGuid = hook["UniqueId"]?.ToString();
            Guid guid = Guid.Empty;
            if (!string.IsNullOrEmpty(hookGuid))
                Guid.TryParse(hookGuid, out guid);

            hook["StopConditionSet"] = JToken.FromObject(Configuration.ConditionSetBuilder.SingleHookCount(guid, limit));
            return;
        }
    }

    private static void MigrateHooksetJson(JObject? hookset) {
        if (hookset == null) return;
        foreach (var key in BiteKeys) {
            if (hookset[key] is JObject biteObj)
                MigrateBiteConfigJson(biteObj);
        }
        if (hookset["CastLures"] is JObject luresObj)
            MigrateLuresJson(luresObj);
    }

    private static void MigrateBiteConfigJson(JObject bite) {
        var existing = bite["ConditionSet"] as JObject;
        if (existing?["g"] is JArray groups && groups.Count > 0)
            return;

        var conditions = new List<Condition>();
        if ((bool?)bite["OnlyWhenActiveSlap"] == true)
            conditions.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.SurfaceSlap));
        if ((bool?)bite["OnlyWhenNotActiveSlap"] == true)
            conditions.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.SurfaceSlap, inverse: true));

        if ((bool?)bite["OnlyWhenActiveIdentical"] == true)
            conditions.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.IdenticalCast));
        if ((bool?)bite["OnlyWhenNotActiveIdentical"] == true)
            conditions.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.IdenticalCast, inverse: true));

        if ((bool?)bite["PrizeCatchReq"] == true)
            conditions.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.PrizeCatch));
        if ((bool?)bite["PrizeCatchNotReq"] == true)
            conditions.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.PrizeCatch, inverse: true));

        if ((bool?)bite["OnlyWhenActiveMultihook"] == true)
            conditions.Add(new Condition { TypeId = Registry.GetId<MultihookAvailableCD>(), Params = [] });

        if ((bool?)bite["HookTimerEnabled"] == true) {
            var min = (double?)bite["MinHookTimer"] ?? 0;
            var max = (double?)bite["MaxHookTimer"] ?? 0;
            var timer = Configuration.ConditionSetBuilder.Range<BiteTimerCD>(min, max);
            if (timer != null) conditions.Add(timer);
        }
        if ((bool?)bite["ChumTimerEnabled"] == true) {
            var cMin = (double?)bite["ChumMinHookTimer"] ?? 0;
            var cMax = (double?)bite["ChumMaxHookTimer"] ?? 0;
            var chum = Configuration.ConditionSetBuilder.Range<ChumTimerCD>(cMin, cMax);
            if (chum != null) conditions.Add(chum);
        }

        if (conditions.Count == 0) return;
        var set = new ConditionSet { CombineMode = ConditionCombineMode.All, Groups = [new ConditionGroup { CombineMode = ConditionCombineMode.All, Conditions = conditions }] };
        bite["ConditionSet"] = JToken.FromObject(set);
    }

    private static void MigrateLuresJson(JObject lures) {
        var existing = lures["ConditionSet"] as JObject;
        if (existing?["g"] is JArray groups && groups.Count > 0)
            return;
        var group = new List<Condition>();
        if ((bool?)lures["OnlyWhenActiveSlap"] == true)
            group.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.SurfaceSlap));
        if ((bool?)lures["OnlyWhenNotActiveSlap"] == true)
            group.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.SurfaceSlap, inverse: true));
        if ((bool?)lures["OnlyWhenActiveIdentical"] == true)
            group.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.IdenticalCast));
        if ((bool?)lures["OnlyWhenNotActiveIdentical"] == true)
            group.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.IdenticalCast, inverse: true));
        if ((bool?)lures["OnlyCastLarge"] == true)
            group.Add(Configuration.ConditionSetBuilder.StatusActive(IDs.Status.PrizeCatch));
        if (group.Count == 0) return;
        var set = new ConditionSet { CombineMode = ConditionCombineMode.All, Groups = [new ConditionGroup { CombineMode = ConditionCombineMode.All, Conditions = group }] };
        lures["ConditionSet"] = JToken.FromObject(set);
    }

    private static void MigrateHookSwimbaitJson(JObject hook) {
        var useSwimbait = (bool?)hook["UseSwimbait"] == true;
        var threshold = Math.Clamp((int?)(hook["SwimbaitCountThreshold"] ?? 1) ?? 1, 1, 3);
        var onlyWhenNoMooch = (bool?)hook["OnlyUseWhenNoMoochAvailable"] != false;
        if (!useSwimbait) return;
        var hookFishId = (int?)(hook["BaitFish"]?["Id"] ?? 0) ?? 0;
        var normal = hook["SwimbaitNormal"] as JObject ?? [];
        var intuition = hook["SwimbaitIntuition"] as JObject ?? [];
        normal["UseSwimbait"] = true;
        intuition["UseSwimbait"] = true;
        if (onlyWhenNoMooch) {
            var set = Configuration.ConditionSetBuilder.SingleFlag<MoochAvailableCD>(inverse: true);
            normal["ConditionSet"] = JToken.FromObject(set);
            intuition["ConditionSet"] = JToken.FromObject(set);
        }
        hook["SwimbaitNormal"] = normal;
        hook["SwimbaitIntuition"] = intuition;
        MigrateSwimbaitCfgCountThreshold(normal, hookFishId, threshold);
        MigrateSwimbaitCfgCountThreshold(intuition, hookFishId, threshold);
    }

    private static void MigrateSwimbaitCountThresholdToConditions(JObject root) {
        if (root["HookPresets"] is not JObject hookPresets)
            return;

        MigratePresetSwimbaitCountThreshold(hookPresets["DefaultPreset"] as JObject);
        foreach (var token in EnumerateArray(hookPresets["CustomPresets"])) {
            if (token is JObject presetObj)
                MigratePresetSwimbaitCountThreshold(presetObj);
        }
    }

    private static void MigratePresetSwimbaitCountThreshold(JObject? preset) {
        if (preset == null) return;
        foreach (var hook in EnumerateArray(preset["ListOfBaits"]).Concat(EnumerateArray(preset["ListOfMooch"]))) {
            if (hook is not JObject hookObj) continue;
            var hookFishId = (int?)(hookObj["BaitFish"]?["Id"] ?? 0) ?? 0;
            MigrateHookSwimbaitCountThreshold(hookObj, hookFishId);
        }
    }

    private static void MigrateHookSwimbaitCountThreshold(JObject hook, int hookFishId) {
        MigrateSwimbaitCfgCountThreshold(hook["SwimbaitNormal"] as JObject, hookFishId);
        MigrateSwimbaitCfgCountThreshold(hook["SwimbaitIntuition"] as JObject, hookFishId);
    }

    private static void MigrateSwimbaitCfgCountThreshold(JObject? swimbait, int hookFishId, int? legacyThreshold = null) {
        if (swimbait == null || (bool?)swimbait["UseSwimbait"] != true)
            return;

        var hadExplicitThreshold = swimbait["CountThreshold"] is JValue;
        var threshold = legacyThreshold ?? Math.Clamp((int?)swimbait["CountThreshold"] ?? 1, 1, 3);
        if (hadExplicitThreshold)
            swimbait.Remove("CountThreshold");

        if (!hadExplicitThreshold && legacyThreshold == null && ConditionSetHasSwimbaitCount(swimbait["ConditionSet"] as JObject))
            return;

        if (!hadExplicitThreshold && legacyThreshold == null && !ConditionSetHasAnyCondition(swimbait["ConditionSet"] as JObject))
            threshold = 1;

        if (ConditionSetHasSwimbaitCountAtLeast(swimbait["ConditionSet"] as JObject, threshold))
            return;

        MergeSwimbaitCountCondition(swimbait, hookFishId, threshold);
    }

    private static void MergeSwimbaitCountCondition(JObject swimbait, int hookFishId, int threshold) {
        var fishId = hookFishId != GameRes.AllMoochesId ? hookFishId : 0;
        var countCond = Configuration.ConditionSetBuilder.SwimbaitCount(threshold, ">=", fishId);
        var setObj = swimbait["ConditionSet"] as JObject;
        if (setObj?["g"] is JArray { Count: > 0 } groups && groups[0] is JObject firstGroup) {
            if (firstGroup["c"] is not JArray conditions)
                firstGroup["c"] = conditions = [];
            conditions.Add(JToken.FromObject(countCond));
            return;
        }

        var set = new ConditionSet {
            CombineMode = ConditionCombineMode.All,
            Groups =
            [
                new ConditionGroup
                {
                    CombineMode = ConditionCombineMode.All,
                    Conditions = [countCond],
                }
            ]
        };
        swimbait["ConditionSet"] = JToken.FromObject(set);
    }

    private static bool ConditionSetHasAnyCondition(JObject? setObj)
        => setObj?["g"] is JArray groups && groups.OfType<JObject>().Any(g => g["c"] is JArray { Count: > 0 });

    private static bool ConditionSetHasSwimbaitCount(JObject? setObj) {
        var typeId = Registry.GetId<SwimbaitCountCD>();
        return ConditionSetHasConditionType(setObj, typeId);
    }

    private static bool ConditionSetHasSwimbaitCountAtLeast(JObject? setObj, int threshold) {
        var typeId = Registry.GetId<SwimbaitCountCD>();
        if (setObj?["g"] is not JArray groups) return false;
        foreach (var group in groups.OfType<JObject>()) {
            if (group["c"] is not JArray conditions) continue;
            foreach (var cond in conditions.OfType<JObject>()) {
                if ((string?)cond["t"] != typeId) continue;
                if (cond["p"] is not JObject p) continue;
                var op = (string?)p["op"] ?? ((bool?)p["above"] != false ? ">=" : "<=");
                if (op is not ">=" and not ">") continue;
                if ((int?)(p["val"] ?? 0) >= threshold)
                    return true;
            }
        }
        return false;
    }

    private static bool ConditionSetHasConditionType(JObject? setObj, string typeId) {
        if (setObj?["g"] is not JArray groups) return false;
        return groups.OfType<JObject>()
            .SelectMany(g => g["c"] is JArray c ? c.OfType<JObject>() : [])
            .Any(cond => (string?)cond["t"] == typeId);
    }

    private static void MigrateSparefulHandSwimbaitLimits(JObject root) {
        if (root["HookPresets"] is not JObject hookPresets)
            return;

        MigratePresetSparefulHandSwimbaitLimits(hookPresets["DefaultPreset"] as JObject);
        foreach (var token in EnumerateArray(hookPresets["CustomPresets"])) {
            if (token is JObject presetObj)
                MigratePresetSparefulHandSwimbaitLimits(presetObj);
        }
    }

    private static void MigratePresetSparefulHandSwimbaitLimits(JObject? preset) {
        if (preset == null) return;
        foreach (var token in EnumerateArray(preset["ListOfFish"])) {
            if (token is JObject fishObj)
                MigrateFishSparefulHandSwimbaitLimit(fishObj);
        }
    }

    private static void MigrateFishSparefulHandSwimbaitLimit(JObject fish) {
        if (fish["SparefulHand"] is not JObject spareful)
            return;

        if (spareful["SwimbaitCountLimit"] is not JValue)
            return;

        var limit = (int?)spareful["SwimbaitCountLimit"] ?? 0;
        spareful.Remove("SwimbaitCountLimit");
        if (limit <= 0)
            return;

        var fishId = (int?)(fish["Fish"]?["Id"] ?? 0) ?? 0;
        if (fishId <= 0)
            return;

        if (ConditionSetHasSwimbaitCountLessThan(spareful["ConditionSet"] as JObject, limit, fishId))
            return;

        MergeSparefulHandSwimbaitCondition(spareful, fishId, limit);
    }

    private static void MergeSparefulHandSwimbaitCondition(JObject spareful, int fishId, int limit) {
        var countCond = Configuration.ConditionSetBuilder.SwimbaitCount(limit, "<", fishId);
        var setObj = spareful["ConditionSet"] as JObject;
        if (setObj?["g"] is JArray { Count: > 0 } groups && groups[0] is JObject firstGroup) {
            if (firstGroup["c"] is not JArray conditions)
                firstGroup["c"] = conditions = [];
            conditions.Add(JToken.FromObject(countCond));
            return;
        }

        spareful["ConditionSet"] = JToken.FromObject(new ConditionSet {
            CombineMode = ConditionCombineMode.All,
            Groups =
            [
                new ConditionGroup
                {
                    CombineMode = ConditionCombineMode.All,
                    Conditions = [countCond],
                }
            ]
        });
    }

    private static bool ConditionSetHasSwimbaitCountLessThan(JObject? setObj, int limit, int fishId) {
        var typeId = Registry.GetId<SwimbaitCountCD>();
        if (setObj?["g"] is not JArray groups) return false;
        foreach (var group in groups.OfType<JObject>()) {
            if (group["c"] is not JArray conditions) continue;
            foreach (var cond in conditions.OfType<JObject>()) {
                if ((string?)cond["t"] != typeId) continue;
                if (cond["p"] is not JObject p) continue;
                var op = (string?)p["op"] ?? "<";
                if (op is not "<" and not "<=") continue;
                if ((int?)(p["val"] ?? 0) != limit) continue;
                var id = (int?)(p["id"] ?? 0) ?? 0;
                if (id == fishId)
                    return true;
            }
        }
        return false;
    }

    private static void MigrateFishConfigJson(JObject fish) {
        if ((bool?)fish["IgnoreOnIntuition"] != true) return;
        var existing = fish["IgnoreConditionSet"] as JObject;
        if (existing?["g"] is JArray groups && groups.Count > 0) return;
        var set = Configuration.ConditionSetBuilder.SingleFlag<IntuitionActiveCD>();
        fish["IgnoreConditionSet"] = JToken.FromObject(set);
    }

    private static void MigrateFishCaughtActionEnabledPresets(JObject root) {
        if (root["HookPresets"] is not JObject hookPresets)
            return;

        MigratePresetFishCaughtActionEnabled(hookPresets["DefaultPreset"] as JObject);
        foreach (var token in EnumerateArray(hookPresets["CustomPresets"])) {
            if (token is JObject presetObj)
                MigratePresetFishCaughtActionEnabled(presetObj);
        }
    }

    private static void MigratePresetFishCaughtActionEnabled(JObject? preset) {
        if (preset == null) return;
        foreach (var token in EnumerateArray(preset["ListOfFish"])) {
            if (token is not JObject fishObj) continue;
            MigrateFishCaughtActionEnabled(fishObj, "SurfaceSlap", "UseSurfaceSlap");
            MigrateFishCaughtActionEnabled(fishObj, "IdenticalCast", "UseIdenticalCast");
        }
    }

    private static void MigrateFishCaughtActionEnabled(JObject fish, string actionProperty, string legacyProperty) {
        if (fish[actionProperty] is not JObject action)
            return;

        if ((bool?)fish[legacyProperty] == true)
            action["Enabled"] = true;

        MigrateActionEnabledFromConditions(action);
    }

    private static void MigrateActionEnabledFromConditions(JObject? action) {
        if (action == null || (bool?)action["Enabled"] == true)
            return;

        if (action["ConditionSet"] is not JObject conditionSet || conditionSet["g"] is not JArray groups)
            return;

        if (groups.Any(g => g is JObject group && group["c"] is JArray conditions && conditions.Count > 0))
            action["Enabled"] = true;
    }

    private static void MigrateFishConfigStopSwapJson(JObject fish) {
        var fishId = (int?)(fish["Fish"]?["Id"] ?? 0) ?? 0;
        if ((bool?)fish["StopAfterCaught"] == true) {
            var limit = (int?)(fish["StopAfterCaughtLimit"] ?? 1) ?? 1;
            if (limit < 1) limit = 1;
            var set = Configuration.ConditionSetBuilder.SingleFishCaughtCount(fishId, limit);
            fish["StopConditionSet"] = JToken.FromObject(set);
        }
        if ((bool?)fish["SwapBait"] == true) {
            var limit = (int?)(fish["SwapBaitCount"] ?? 1) ?? 1;
            var set = Configuration.ConditionSetBuilder.SingleFishCaughtCount(fishId, limit);
            fish["SwapBaitConditionSet"] = JToken.FromObject(set);
        }
        if ((bool?)fish["SwapPresets"] == true) {
            var limit = (int?)(fish["SwapPresetCount"] ?? 1) ?? 1;
            var set = Configuration.ConditionSetBuilder.SingleFishCaughtCount(fishId, limit);
            fish["SwapPresetConditionSet"] = JToken.FromObject(set);
        }
    }

    private static void MigrateAutoCordialJson(JObject? cordial) {
        if (cordial == null || (bool?)cordial["AllowOvercapIC"] != true) return;
        var existing = cordial["OvercapConditionSet"] as JObject;
        if (existing?["g"] is JArray groups && groups.Count > 0) return;
        var set = Configuration.ConditionSetBuilder.SingleStatus(IDs.Status.IdenticalCast);
        cordial["OvercapConditionSet"] = JToken.FromObject(set);
    }

    private static void MigrateAutoCastsTimeWindowJson(JObject autoCasts) {
        if ((bool?)autoCasts["OnlyCastDuringSpecificTime"] != true) return;
        var existing = autoCasts["TimeWindowConditionSet"] as JObject;
        if (existing?["g"] is JArray groups && groups.Count > 0) return;
        var start = (autoCasts["StartTime"]?.ToObject<TimeOnly>()) ?? default;
        var end = (autoCasts["EndTime"]?.ToObject<TimeOnly>()) ?? default;
        var set = Configuration.ConditionSetBuilder.SingleTimeWindow(start, end, invert: false);
        autoCasts["TimeWindowConditionSet"] = JToken.FromObject(set);
    }

    private static void MigratePresetExtra(JObject? preset) {
        if (preset == null)
            return;

        if (preset["ExtraCfg"] is not JObject extra)
            return;

        var triggers = extra["Triggers"] as JArray ?? [];
        extra["Triggers"] = triggers;

        // Intuition gained
        var swapPresetIntuitionGain = (bool?)extra["SwapPresetIntuitionGain"] == true;
        var swapBaitIntuitionGain = (bool?)extra["SwapBaitIntuitionGain"] == true;
        if ((swapPresetIntuitionGain || swapBaitIntuitionGain) && triggers.Count < 16) {
            var set = Configuration.ConditionSetBuilder.SingleFlag<IntuitionActiveCD>();
            var trig = new ExtraTrigger {
                ConditionSet = set,
                SwapPreset = swapPresetIntuitionGain,
                PresetToSwap = (string?)extra["PresetToSwapIntuitionGain"] ?? @"-",
                SwapBait = swapBaitIntuitionGain,
                BaitToSwap = extra["BaitToSwapIntuitionGain"]?.ToObject<BaitFishClass>() ?? new BaitFishClass(),
                StopAction = ExtraStopAction.None,
            };
            triggers.Add(JToken.FromObject(trig));
        }

        // Intuition lost
        var swapPresetIntuitionLost = (bool?)extra["SwapPresetIntuitionLost"] == true;
        var swapBaitIntuitionLost = (bool?)extra["SwapBaitIntuitionLost"] == true;
        var quitOnIntuitionLost = (bool?)extra["QuitOnIntuitionLost"] == true;
        var stopOnIntuitionLost = (bool?)extra["StopOnIntuitionLost"] == true;
        if ((swapPresetIntuitionLost || swapBaitIntuitionLost || quitOnIntuitionLost || stopOnIntuitionLost) && triggers.Count < 16) {
            var set = Configuration.ConditionSetBuilder.SingleFlag<IntuitionActiveCD>(inverse: true);
            var stop = ExtraStopAction.None;
            if (quitOnIntuitionLost)
                stop = ExtraStopAction.QuitFishing;
            else if (stopOnIntuitionLost)
                stop = ExtraStopAction.StopOnly;

            var trig = new ExtraTrigger {
                ConditionSet = set,
                SwapPreset = swapPresetIntuitionLost,
                PresetToSwap = (string?)extra["PresetToSwapIntuitionLost"] ?? @"-",
                SwapBait = swapBaitIntuitionLost,
                BaitToSwap = extra["BaitToSwapIntuitionLost"]?.ToObject<BaitFishClass>() ?? new BaitFishClass(),
                StopAction = stop,
            };
            triggers.Add(JToken.FromObject(trig));
        }

        // Spectral gained
        var swapPresetSpectralGain = (bool?)extra["SwapPresetSpectralCurrentGain"] == true;
        var swapBaitSpectralGain = (bool?)extra["SwapBaitSpectralCurrentGain"] == true;
        if ((swapPresetSpectralGain || swapBaitSpectralGain) && triggers.Count < 16) {
            var set = Configuration.ConditionSetBuilder.SingleFlag<SpectralActiveCD>();
            var trig = new ExtraTrigger {
                ConditionSet = set,
                SwapPreset = swapPresetSpectralGain,
                PresetToSwap = (string?)extra["PresetToSwapSpectralCurrentGain"] ?? @"-",
                SwapBait = swapBaitSpectralGain,
                BaitToSwap = extra["BaitToSwapSpectralCurrentGain"]?.ToObject<BaitFishClass>() ?? new BaitFishClass(),
                StopAction = ExtraStopAction.None,
            };
            triggers.Add(JToken.FromObject(trig));
        }

        // Spectral lost
        var swapPresetSpectralLost = (bool?)extra["SwapPresetSpectralCurrentLost"] == true;
        var swapBaitSpectralLost = (bool?)extra["SwapBaitSpectralCurrentLost"] == true;
        if ((swapPresetSpectralLost || swapBaitSpectralLost) && triggers.Count < 16) {
            var set = Configuration.ConditionSetBuilder.SingleFlag<SpectralActiveCD>(inverse: true);
            var trig = new ExtraTrigger {
                ConditionSet = set,
                SwapPreset = swapPresetSpectralLost,
                PresetToSwap = (string?)extra["PresetToSwapSpectralCurrentLost"] ?? @"-",
                SwapBait = swapBaitSpectralLost,
                BaitToSwap = extra["BaitToSwapSpectralCurrentLost"]?.ToObject<BaitFishClass>() ?? new BaitFishClass(),
                StopAction = ExtraStopAction.None,
            };
            triggers.Add(JToken.FromObject(trig));
        }

        // Angler's Art stacks reached
        var stopAfterAnglersArt = (bool?)extra["StopAfterAnglersArt"] == true;
        var anglerStackQtd = (int?)(extra["AnglerStackQtd"] ?? 0) ?? 0;
        var swapPresetAnglersArt = (bool?)extra["SwapPresetAnglersArt"] == true;
        var swapBaitAnglersArt = (bool?)extra["SwapBaitAnglersArt"] == true;
        if ((swapPresetAnglersArt || swapBaitAnglersArt || stopAfterAnglersArt) && anglerStackQtd > 0 && triggers.Count < 16) {
            var set = Configuration.ConditionSetBuilder.SingleStatusStacks(IDs.Status.AnglersArt, anglerStackQtd);

            var stop = ExtraStopAction.None;
            if (stopAfterAnglersArt) {
                var step = (extra["AnglerStopFishingStep"]?.ToObject<FishingSteps>()) ?? FishingSteps.None;
                stop = step == FishingSteps.Quitting
                    ? ExtraStopAction.QuitFishing
                    : ExtraStopAction.StopOnly;
            }

            var trig = new ExtraTrigger {
                ConditionSet = set,
                SwapPreset = swapPresetAnglersArt,
                PresetToSwap = (string?)extra["PresetToSwapAnglersArt"] ?? @"-",
                SwapBait = swapBaitAnglersArt,
                BaitToSwap = extra["BaitToSwapAnglersArt"]?.ToObject<BaitFishClass>() ?? new BaitFishClass(),
                StopAction = stop,
            };
            triggers.Add(JToken.FromObject(trig));
        }

        // Swimbait fills
        var swimbaitFillsAction = (extra["SwimbaitFillsAction"]?.ToObject<SwimbaitAction>()) ?? SwimbaitAction.None;
        if (swimbaitFillsAction != SwimbaitAction.None && triggers.Count < 16) {
            var set = Configuration.ConditionSetBuilder.SingleSwimbaitCount(3, above: true);

            var stop = swimbaitFillsAction == SwimbaitAction.Stop
                ? ExtraStopAction.StopOnly
                : ExtraStopAction.None;

            var trig = new ExtraTrigger {
                ConditionSet = set,
                SwapPreset = swimbaitFillsAction == SwimbaitAction.SwapPreset,
                PresetToSwap = (string?)extra["PresetToSwapSwimbaitFills"] ?? @"-",
                SwapBait = false,
                StopAction = stop,
            };
            triggers.Add(JToken.FromObject(trig));
        }

        // Swimbait runs out
        var swimbaitRunsOutAction = (extra["SwimbaitRunsOutAction"]?.ToObject<SwimbaitAction>()) ?? SwimbaitAction.None;
        if (swimbaitRunsOutAction != SwimbaitAction.None && triggers.Count < 16) {
            var set = Configuration.ConditionSetBuilder.SingleSwimbaitCount(0, above: false);

            var stop = swimbaitRunsOutAction == SwimbaitAction.Stop
                ? ExtraStopAction.StopOnly
                : ExtraStopAction.None;

            var trig = new ExtraTrigger {
                ConditionSet = set,
                SwapPreset = swimbaitRunsOutAction == SwimbaitAction.SwapPreset,
                PresetToSwap = (string?)extra["PresetToSwapSwimbaitRunsOut"] ?? @"-",
                SwapBait = false,
                StopAction = stop,
            };
            triggers.Add(JToken.FromObject(trig));
        }
    }
}

