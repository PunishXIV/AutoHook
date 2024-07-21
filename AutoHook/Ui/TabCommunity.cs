using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AutoHook.Classes;
using AutoHook.Configurations;
using AutoHook.Enums;
using AutoHook.Fishing;
using AutoHook.Resources.Localization;
using AutoHook.Spearfishing;
using AutoHook.Utils;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using ECommons.Throttlers;
using ImGuiNET;

namespace AutoHook.Ui;

public class TabCommunity : BaseTab
{
    public override string TabName { get; } = UIStrings.CommunityPresets;
    public override bool Enabled { get; } = true;
    public override OpenWindow Type { get; } = OpenWindow.Community;

    private static SpearFishingPresets _gigPreset = Service.Configuration.AutoGigConfig;
    private static FishingPresets _fishingPreset = Service.Configuration.HookPresets;

    public override void DrawHeader()
    {
    }

    public override void Draw()
    {
        ImGui.TextColored(ImGuiColors.DalamudYellow,
            UIStrings.CommunityDescription);
        using (ImRaii.Group())
        {
            using (var disabled = ImRaii.Disabled(EzThrottler.GetRemainingTime("WikiUpdate") > 0))
            {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.CloudDownloadAlt, UIStrings.GetWikiPresets))
                    WikiPresets.ListWikiPages();
            }

            if (ImGui.Selectable(UIStrings.ClickOpenWiki))
                OpenWiki();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(UIStrings.NewAccountWarning);

            if (ImGui.CollapsingHeader(UIStrings.Fishing, ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var (key, value) in WikiPresets.Presets.Where(preset => preset.Value.Count != 0))
                {
                    ImGui.Indent();
                    DrawHeaderList(key, value.Cast<BasePresetConfig>().ToList());
                    ImGui.Unindent();
                }
            }

            ImGui.Separator();

            if (ImGui.CollapsingHeader(UIStrings.Spearfishing, ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var (key, value) in WikiPresets.PresetsSf.Where(preset => preset.Value.Count != 0))
                {
                    ImGui.Indent();
                    DrawHeaderList(key, value.Cast<BasePresetConfig>().ToList());
                    ImGui.Unindent();
                }
            }
        }
    }

    private void DrawHeaderList(string tab, List<BasePresetConfig> list)
    {
        if (ImGui.CollapsingHeader($"{tab}, Total: {list.Count}"))
        {
            ImGui.Indent();
            foreach (var item in list)
            {
                var color = ImGuiColors.DalamudWhite;
                // check if the preset is fishing or autogig and if already in the list
                if (item is CustomPresetConfig customPreset)
                {
                    if (_fishingPreset.PresetList.Any(p => p.PresetName == customPreset.PresetName))
                        color = ImGuiColors.ParsedGreen;
                }
                else if (item is AutoGigConfig gigPreset)
                {
                    if (_gigPreset.Presets.Any(p => p.PresetName == gigPreset.PresetName))
                        color = ImGuiColors.ParsedGreen;
                }

                using (var a = ImRaii.PushColor(ImGuiCol.Text, color))
                {
                    ImGui.Selectable($"- {item.PresetName}");
                }

                ImportPreset(item);
            }

            ImGui.Unindent();
        }
    }

    public static void ImportPreset(BasePresetConfig preset)
    {
        if (!ImGui.BeginPopupContextItem(@$"PresetOptions###{preset.PresetName}"))
            return;

        var name = preset.PresetName;
        if (preset.PresetName.StartsWith(@"[Old Version]"))
            ImGui.TextColored(ImGuiColors.ParsedOrange, UIStrings.Old_Preset_Warning);
        else
            ImGui.TextWrapped(UIStrings.ImportThisPreset);

        if (ImGui.InputText(UIStrings.PresetName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
            preset.RenamePreset(name);

        if (ImGui.Button(UIStrings.Import))
        {
            if (preset is CustomPresetConfig customPreset)
                _fishingPreset.AddNewPreset(customPreset);
            else if (preset is AutoGigConfig gigPreset)
                _gigPreset.AddNewPreset(gigPreset);

            Notify.Success(UIStrings.PresetImported);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGui.Button(UIStrings.DrawImportExport_Cancel))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private static void OpenWiki()
    {
        var url = "https://github.com/PunishXIV/AutoHook/wiki";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}