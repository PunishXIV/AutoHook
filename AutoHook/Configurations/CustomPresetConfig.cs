using AutoHook.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Newtonsoft.Json;

namespace AutoHook.Configurations;

public class CustomPresetConfig : BasePresetConfig {
    public const string AnonymousPresetPrefix = "anon_";

    [JsonIgnore]
    public bool IsAnonymous => PresetName.StartsWith(AnonymousPresetPrefix, StringComparison.Ordinal);
    public string GetAnonymousName() => AnonymousPresetPrefix + PresetName;

    public static Dictionary<string, string> BuildAnonymousNameMap(IEnumerable<CustomPresetConfig> presets)
        => presets.ToDictionary(p => p.PresetName, p => p.GetAnonymousName());

    public static void RemapPresetSwapReferences(IEnumerable<CustomPresetConfig> presets, IReadOnlyDictionary<string, string> nameMap) {
        foreach (var preset in presets)
            RemapPresetSwapReferences(preset, nameMap);
    }

    public static void RemapPresetSwapReferences(CustomPresetConfig preset, IReadOnlyDictionary<string, string> nameMap) {
        foreach (var fish in preset.ListOfFish) {
            if (fish.PresetToSwap != "-" && nameMap.TryGetValue(fish.PresetToSwap, out var newName))
                fish.PresetToSwap = newName;
        }

        foreach (var trig in preset.ExtraCfg.Triggers) {
            if (trig.PresetToSwap != "-" && nameMap.TryGetValue(trig.PresetToSwap, out var newName))
                trig.PresetToSwap = newName;
        }
    }

    public static CustomPresetConfig? CreateAutoPreset(uint baitId, uint itemId, bool stopAfterCaughtOnce) {
        var bait = new BaitFishClass((int)baitId);
        if (baitId == 0 || bait.BaitType == BaitType.Unknown)
            return null;

        if (stopAfterCaughtOnce && itemId == 0)
            return null;

        if (itemId > 0) {
            var fish = new BaitFishClass((int)itemId);
            if (fish.BaitType != BaitType.Mooch)
                return null;
        }

        var presetName = itemId > 0 ? $"{new BaitFishClass((int)itemId).Name} ({bait.Name})" : bait.Name;
        var preset = new CustomPresetConfig(presetName);
        preset.AddItem(new HookConfig((int)baitId));

        preset.ExtraCfg.Enabled = true;
        preset.ExtraCfg.ForceBaitSwap = true;
        preset.ExtraCfg.ForcedBaitId = (int)baitId;

        if (itemId > 0) {
            var fishConfig = new FishConfig((int)itemId);
            if (stopAfterCaughtOnce)
                fishConfig.StopAfterCaughtLimit.Value = (true, 1);
            preset.AddItem(fishConfig);
        }

        return preset;
    }

    public List<HookConfig> ListOfBaits { get; set; } = [];
    public List<HookConfig> ListOfMooch { get; set; } = [];
    public List<FishConfig> ListOfFish { get; set; } = [];

    public AutoCastsConfig AutoCastsCfg = new();

    public ExtraConfig ExtraCfg = new();

    public List<NamedConditionConfig> NamedConditions { get; set; } = [];

    public CustomPresetConfig(string name) {
        PresetName = name;
    }

    public override void AddItem(BaseOption item) {
        //check if the item is HookConfig (then check BaitFishClass BaitType), or FishConfig 
        if (item is HookConfig hookConfig) {
            if (hookConfig.BaitFish.BaitType == BaitType.Bait)
                ListOfBaits.Add(hookConfig);
            else if (hookConfig.BaitFish.BaitType == BaitType.Mooch)
                ListOfMooch.Add(hookConfig);
        }
        else if (item is FishConfig fishConfig)
            ListOfFish.Add(fishConfig);

        Service.Save();
    }

    public void ReplaceBaitConfig(HookConfig hookConfig) {
        var existing = ListOfBaits.FirstOrDefault(hook => hook.BaitFish.Id == hookConfig.BaitFish.Id);
        if (existing != null) {
            ListOfBaits.Remove(existing);
        }

        ListOfBaits.Add(hookConfig);

        Service.Save();
    }

    public void ReplaceMoochConfig(HookConfig moochConfig) {
        var existing = ListOfMooch.FirstOrDefault(hook => hook.BaitFish.Id == moochConfig.BaitFish.Id);
        if (existing != null) {
            ListOfMooch.Remove(existing);
        }

        ListOfMooch.Add(moochConfig);

        Service.Save();
    }

    public HookConfig? GetCfgById(uint id, bool isMooching) {
        if (isMooching) {
            var mooch = ListOfMooch.FirstOrDefault(hook => hook.BaitFish.Id == id);
            return mooch ?? ListOfMooch.FirstOrDefault(hook => hook.BaitFish.Id == GameRes.AllMoochesId);
        }

        var bait = ListOfBaits.FirstOrDefault(hook => hook.BaitFish.Id == id);
        return bait ?? ListOfBaits.FirstOrDefault(hook => hook.BaitFish.Id == GameRes.AllBaitsId);
    }

    public FishConfig? GetFishById(uint id) {
        return ListOfFish.FirstOrDefault(fish => fish.Fish.Id == id);
    }

    public override void RemoveItem(Guid value) {
        ListOfBaits.RemoveAll(x => x.UniqueId == value);
        ListOfMooch.RemoveAll(x => x.UniqueId == value);
        ListOfFish.RemoveAll(x => x.UniqueId == value);
        Service.Save();
    }

    public bool HasBaitOrMooch(uint id) {
        return ListOfBaits.Any(hook => hook.BaitFish.Id == id || hook.BaitFish.Id == GameRes.AllBaitsId) ||
               ListOfMooch.Any(hook => hook.BaitFish.Id == id || hook.BaitFish.Id == GameRes.AllMoochesId);
    }

    public void ResetCounter() {
        foreach (var item in ListOfBaits) {
            FishingManager.FishingHelper.RemoveId(item.UniqueId);
        }

        foreach (var item in ListOfMooch) {
            FishingManager.FishingHelper.RemoveId(item.UniqueId);
        }

        foreach (var item in ListOfFish) {
            FishingManager.FishingHelper.RemoveId(item.UniqueId);
        }
    }

    public void TryResetCounter() {
        if (ExtraCfg is { Enabled: true, ResetCounterPresetSwap: true })
            ResetCounter();
    }

    public override bool Equals(object? obj) {
        return obj is CustomPresetConfig settings &&
               UniqueId == settings.UniqueId;
    }

    public override int GetHashCode() {
        return HashCode.Combine(UniqueId);
    }

    [JsonIgnore] public bool IsGlobal => PresetName == Service.GlobalPresetName;

    public override void DrawOptions() {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X / 2 -
                            ImGui.CalcTextSize(PresetName).X / 2);
        ImGui.TextColored(ImGuiColors.DalamudOrange, $" {PresetName}");

        ConditionUi.EvaluationPreset = this;
        try {
            using var mainTab = ImRaii.TabBar(@"TabBarsPreset", ImGuiTabBarFlags.NoTooltip);
            if (!mainTab)
                return;

            using (var tabHook = ImRaii.TabItem(UIStrings.Hooking)) {
                DrawUtil.HoveredTooltip(UIStrings.BaitTabHelpText);
                if (tabHook)
                    SubTabBaitMooch.DrawHookTab(this);
            }

            using (var tabFish = ImRaii.TabItem(UIStrings.FishCaught)) {
                DrawUtil.HoveredTooltip(UIStrings.FishCaughtHelp);
                if (tabFish)
                    SubTabFish.DrawFishTab(this);
            }

            using (var tabExtra = ImRaii.TabItem(UIStrings.ExtraOptions)) {
                DrawUtil.HoveredTooltip(UIStrings.ExtraOptionsHelp);
                if (tabExtra)
                    SubTabExtra.DrawExtraTab(this);
            }

            using (var tabAutoCast = ImRaii.TabItem(UIStrings.Auto_Casts)) {
                DrawUtil.HoveredTooltip(UIStrings.AutoCastsHelp);
                if (tabAutoCast)
                    SubTabAutoCast.DrawAutoCastTab(this);
            }

            using (var tabConditions = ImRaii.TabItem(UIStrings.Conditions)) {
                DrawUtil.HoveredTooltip(UIStrings.PresetConditions_HelpText);
                if (tabConditions)
                    SubTabConditions.DrawConditionsTab(this);
            }
        }
        finally {
            ConditionUi.EvaluationPreset = null;
        }
    }
}
