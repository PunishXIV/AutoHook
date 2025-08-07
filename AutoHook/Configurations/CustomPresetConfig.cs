using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using AutoHook.Classes;
using AutoHook.Enums;
using AutoHook.Fishing;
using AutoHook.Resources.Localization;
using AutoHook.Ui;
using AutoHook.Utils;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;

namespace AutoHook.Configurations;

public class CustomPresetConfig : BasePresetConfig
{
    public List<HookConfig> ListOfBaits { get; set; } = new();
    public List<HookConfig> ListOfMooch { get; set; } = new();
    public List<FishConfig> ListOfFish { get; set; } = new();

    public AutoCastsConfig AutoCastsCfg = new();

    public ExtraConfig ExtraCfg = new();

    public CustomPresetConfig(string name)
    {
        PresetName = name;
    }

    public override void AddItem(BaseOption item)
    {
        //check if the item is HookConfig (then check BaitFishClass BaitType), or FishConfig 
        if (item is HookConfig hookConfig)
        {
            if (hookConfig.BaitFish.BaitType == BaitType.Bait)
                ListOfBaits.Add(hookConfig);
            else if (hookConfig.BaitFish.BaitType == BaitType.Mooch)
                ListOfMooch.Add(hookConfig);
        }
        else if (item is FishConfig fishConfig)
            ListOfFish.Add(fishConfig);

        Service.Save();
    }

    public void ReplaceBaitConfig(HookConfig hookConfig)
    {
        var existing = ListOfBaits.FirstOrDefault(hook => hook.BaitFish.Id == hookConfig.BaitFish.Id);
        if (existing != null)
        {
            ListOfBaits.Remove(existing);
        }

        ListOfBaits.Add(hookConfig);

        Service.Save();
    }

    public void ReplaceMoochConfig(HookConfig moochConfig)
    {
        var existing = ListOfMooch.FirstOrDefault(hook => hook.BaitFish.Id == moochConfig.BaitFish.Id);
        if (existing != null)
        {
            ListOfMooch.Remove(existing);
        }

        ListOfMooch.Add(moochConfig);

        Service.Save();
    }

    public HookConfig? GetCfgById(int id, bool isMooching)
    {
        if (isMooching)
        {
            var mooch = ListOfMooch.FirstOrDefault(hook => hook.BaitFish.Id == id);
            return mooch ?? ListOfMooch.FirstOrDefault(hook => hook.BaitFish.Id == GameRes.AllMoochesId);
        }

        var bait = ListOfBaits.FirstOrDefault(hook => hook.BaitFish.Id == id);
        return bait ?? ListOfBaits.FirstOrDefault(hook => hook.BaitFish.Id == GameRes.AllBaitsId);
    }

    public FishConfig? GetFishById(int id)
    {
        return ListOfFish.FirstOrDefault(fish => fish.Fish.Id == id);
    }

    public override void RemoveItem(Guid value)
    {
        ListOfBaits.RemoveAll(x => x.UniqueId == value);
        ListOfMooch.RemoveAll(x => x.UniqueId == value);
        ListOfFish.RemoveAll(x => x.UniqueId == value);
        Service.Save();
    }

    public bool HasBaitOrMooch(uint id)
    {
        return ListOfBaits.Any(hook => hook.BaitFish.Id == id || hook.BaitFish.Id == GameRes.AllBaitsId) ||
               ListOfMooch.Any(hook => hook.BaitFish.Id == id || hook.BaitFish.Id == GameRes.AllMoochesId);
    }

    public void ResetCounter()
    {
        foreach (var item in ListOfBaits)
        {
            FishingManager.FishingHelper.RemoveId(item.UniqueId);
        }

        foreach (var item in ListOfMooch)
        {
            FishingManager.FishingHelper.RemoveId(item.UniqueId);
        }

        foreach (var item in ListOfFish)
        {
            FishingManager.FishingHelper.RemoveId(item.UniqueId);
        }
    }

    public override bool Equals(object? obj)
    {
        return obj is CustomPresetConfig settings &&
               UniqueId == settings.UniqueId;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(UniqueId);
    }

    [JsonIgnore] public bool IsGlobal => PresetName == Service.GlobalPresetName;

    public override void DrawOptions()
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X / 2 -
                            ImGui.CalcTextSize(PresetName).X / 2);
        ImGui.TextColored(ImGuiColors.DalamudOrange, $" {PresetName}");

        using var mainTab = ImRaii.TabBar(@"TabBarsPreset", ImGuiTabBarFlags.NoTooltip);
        if (!mainTab)
            return;

        using (var tabHook = ImRaii.TabItem(UIStrings.Hooking))
        {
            DrawUtil.HoveredTooltip(UIStrings.BaitTabHelpText);
            if (tabHook)
                SubTabBaitMooch.DrawHookTab(this);
        }

        using (var tabFish = ImRaii.TabItem(UIStrings.FishCaught))
        {
            DrawUtil.HoveredTooltip(UIStrings.FishCaughtHelp);
            if (tabFish)
                SubTabFish.DrawFishTab(this);
        }

        using (var tabExtra = ImRaii.TabItem(UIStrings.ExtraOptions))
        {
            DrawUtil.HoveredTooltip(UIStrings.ExtraOptionsHelp);
            if (tabExtra)
                SubTabExtra.DrawExtraTab(this);
        }

        using (var tabAutoCast = ImRaii.TabItem(UIStrings.Auto_Casts))
        {
            DrawUtil.HoveredTooltip(UIStrings.AutoCastsHelp);
            if (tabAutoCast)
                SubTabAutoCast.DrawAutoCastTab(this);
        }
    }
}