using AutoHook.Spearfishing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using ECommons.Throttlers;
using System.Diagnostics;

namespace AutoHook.Ui;

public class TabCommunity : BaseTab {
    public override string TabName { get; } = UIStrings.CommunityPresets;
    public override bool Enabled { get; } = true;
    public override OpenWindow Type { get; } = OpenWindow.Community;

    private static readonly SpearFishingPresets _gigPreset = Service.Configuration.AutoGigConfig;
    private static readonly FishingPresets _fishingPreset = Service.Configuration.HookPresets;

    // Keep per-category folder names while popups are open
    private readonly Dictionary<string, string> _importAllFolderNames = [];

    private string _searchFilter = string.Empty;
    private bool SearchActive => !string.IsNullOrWhiteSpace(_searchFilter);
    private string SearchFilter => _searchFilter.Trim();
    private bool MatchesSearch(string text) => !SearchActive || text.Contains(SearchFilter, StringComparison.InvariantCultureIgnoreCase);

    public override void DrawHeader() { }

    public override void Draw() {
        ImGui.TextColored(ImGuiColors.DalamudYellow, UIStrings.CommunityDescription);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##CommunityPresetSearch", UIStrings.Search_Hint, ref _searchFilter, 128);

        using (ImRaii.Group()) {
            using (var disabled = ImRaii.Disabled(EzThrottler.GetRemainingTime("WikiUpdate") > 0)) {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.CloudDownloadAlt, UIStrings.GetWikiPresets))
                    _ = WikiPresets.ListWikiPages();
            }

            if (ImGui.Selectable(UIStrings.ClickOpenWiki))
                OpenWiki();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(UIStrings.NewAccountWarning);

            if (ImGui.CollapsingHeader(UIStrings.Fishing, ImGuiTreeNodeFlags.DefaultOpen)) {
                foreach (var (key, value) in WikiPresets.Presets.Where(preset => preset.Value.Count != 0)) {
                    var list = value.Where(x => x.Folder == null).SelectMany(x => x.Presets).Cast<BasePresetConfig>().ToList();
                    var foldered = value.Where(x => x.Folder != null).Select(x => x.Folder!).ToList();
                    var (filteredList, filteredFoldered) = FilterPresets(key, list, foldered);
                    if (SearchActive && filteredList.Count == 0 && filteredFoldered is not { Count: > 0 })
                        continue;

                    using (ImRaii.PushIndent())
                        DrawHeaderList(key, filteredList, filteredFoldered);
                }
            }

            ImGui.Separator();

            if (ImGui.CollapsingHeader(UIStrings.Spearfishing, ImGuiTreeNodeFlags.DefaultOpen)) {
                foreach (var (key, value) in WikiPresets.PresetsSf.Where(preset => preset.Value.Count != 0)) {
                    var list = value.Cast<BasePresetConfig>().ToList();
                    if (SearchActive) {
                        if (!MatchesSearch(key))
                            list = [.. list.Where(p => MatchesSearch(p.PresetName))];
                        if (list.Count == 0)
                            continue;
                    }

                    using (ImRaii.PushIndent())
                        DrawHeaderList(key, list);
                }
            }
        }
    }

    private (List<BasePresetConfig> list, List<WikiFolderExport>? foldered) FilterPresets(string category, List<BasePresetConfig> list, List<WikiFolderExport>? foldered) {
        if (!SearchActive)
            return (list, foldered);

        if (MatchesSearch(category))
            return (list, foldered);

        var filteredList = list.Where(p => MatchesSearch(p.PresetName)).ToList();
        List<WikiFolderExport>? filteredFoldered = null;

        if (foldered != null) {
            foreach (var bundle in foldered) {
                if (MatchesSearch(bundle.Root.FolderName) ||
                    bundle.Folders.Any(f => MatchesSearch(f.FolderName)) ||
                    bundle.Presets.Any(p => MatchesSearch(p.PresetName))) {
                    filteredFoldered ??= [];
                    filteredFoldered.Add(bundle);
                }
            }
        }

        return (filteredList, filteredFoldered);
    }

    private static int GetWikiCategoryTotal(List<BasePresetConfig> list, List<WikiFolderExport>? folderedPresets) {
        var total = list.Count;
        if (folderedPresets == null)
            return total;

        foreach (var bundle in folderedPresets)
            total += PresetImport.CountFolderTreeItems(bundle.Root, bundle.Folders);

        return total;
    }

    private static bool IsFishingPresetList(List<BasePresetConfig> list, List<WikiFolderExport>? folderedPresets) {
        if (list.Count > 0)
            return list[0] is CustomPresetConfig;
        return folderedPresets?.FirstOrDefault()?.Presets.FirstOrDefault() is not null;
    }

    private static PresetImportOptions CommunityOptions(Guid? attachRootToParentId = null) => new() {
        SkipDuplicateNames = true,
        AttachRootToParentId = attachRootToParentId
    };

    private void ImportAllFishingCategory(string tab, List<BasePresetConfig> list, List<WikiFolderExport>? folderedPresets) {
        var totalImported = 0;
        var totalSkipped = 0;
        var totalFolders = 0;
        var hasSubfolders = folderedPresets is { Count: > 0 };
        var fishingPresets = list.OfType<CustomPresetConfig>().ToList();

        if (!hasSubfolders) {
            if (fishingPresets.Count == 0) {
                Notify.Info("No new presets to import.");
                return;
            }

            var folderName = _importAllFolderNames.TryGetValue(tab, out var n) && !string.IsNullOrWhiteSpace(n) ? n : tab;
            var result = PresetImport.ImportPresetsIntoNewFolder(_fishingPreset, folderName, fishingPresets, CommunityOptions());
            if (result.ImportedPresets > 0) {
                Service.Save();
                Notify.Success($"Imported {result.ImportedPresets} preset(s) into folder '{folderName}'{(result.SkippedPresets > 0 ? $", skipped {result.SkippedPresets} duplicate(s)" : string.Empty)}.");
            }
            else {
                Notify.Info("No new presets to import.");
            }

            return;
        }

        var parentFolderName = _importAllFolderNames.TryGetValue(tab, out var name) && !string.IsNullOrWhiteSpace(name) ? name : tab;
        PresetFolder? parentFolder = null;

        if (fishingPresets.Count > 0) {
            var result = PresetImport.ImportPresetsIntoNewFolder(_fishingPreset, parentFolderName, fishingPresets, CommunityOptions());
            totalImported += result.ImportedPresets;
            totalSkipped += result.SkippedPresets;
            totalFolders += result.FoldersAdded;

            if (result.ImportedPresets > 0)
                parentFolder = result.CreatedRootFolder;
        }

        foreach (var bundle in folderedPresets!) {
            parentFolder ??= new PresetFolder(parentFolderName);

            var result = PresetImport.ImportFolderTree(_fishingPreset, bundle.Root, bundle.Folders, bundle.Presets, CommunityOptions(attachRootToParentId: parentFolder.UniqueId));
            totalSkipped += result.SkippedPresets;
            if (result.ImportedPresets == 0)
                continue;

            if (!_fishingPreset.Folders.Contains(parentFolder)) {
                _fishingPreset.Folders.Add(parentFolder);
                totalFolders++;
            }

            totalImported += result.ImportedPresets;
            totalFolders += result.FoldersAdded;
        }

        if (totalImported == 0) {
            Notify.Info("No new presets to import.");
            return;
        }

        Service.Save();
        Notify.Success($"Imported {totalImported} preset(s) into {totalFolders} folder(s){(totalSkipped > 0 ? $", skipped {totalSkipped} duplicate(s)" : string.Empty)}.");
    }

    private void DrawHeaderList(string tab, List<BasePresetConfig> list, List<WikiFolderExport>? folderedPresets = null) {
        var total = GetWikiCategoryTotal(list, folderedPresets);
        var headerFlags = SearchActive ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        if (ImGui.CollapsingHeader($"{tab}, Total: {total}", headerFlags)) {
            using (ImRaii.PushIndent()) {
                // Import-all with confirmation (and folder creation for fishing presets)
                if (ImGui.Button($"Import all###{tab}")) {
                    if (!_importAllFolderNames.ContainsKey(tab))
                        _importAllFolderNames[tab] = tab;
                    ImGui.OpenPopup($"ImportAll###{tab}");
                }

                // Popup content
                using (var popup = ImRaii.Popup($"ImportAll###{tab}")) {
                    if (popup.Success) {
                        var isFishing = IsFishingPresetList(list, folderedPresets);

                        ImGui.TextWrapped($"Import {total} item(s) from '{tab}'?");

                        if (isFishing && (list.Count > 0 || folderedPresets is { Count: > 0 })) {
                            var name = _importAllFolderNames[tab];
                            if (ImGui.InputText(UIStrings.FolderName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
                                _importAllFolderNames[tab] = name;
                        }

                        // Import / Cancel buttons
                        if (ImGui.Button(UIStrings.Import)) {
                            if (isFishing) {
                                ImportAllFishingCategory(tab, list, folderedPresets);
                                ImGui.CloseCurrentPopup();
                            }
                            else {
                                ImportAllSpearfishingPresets(list);
                                ImGui.CloseCurrentPopup();
                            }
                        }

                        ImGui.SameLine();

                        if (ImGui.Button(UIStrings.DrawImportExport_Cancel)) {
                            ImGui.CloseCurrentPopup();
                        }
                    }
                }

                if (folderedPresets != null) {
                    foreach (var bundle in folderedPresets)
                        DrawWikiFolderExport(tab, bundle, headerFlags);
                }

                foreach (var item in list)
                    DrawPresetSelectable(item);
            }
        }
    }

    private void DrawWikiFolderExport(string tab, WikiFolderExport bundle, ImGuiTreeNodeFlags headerFlags) {
        var presetCount = bundle.Presets.Count;
        var childFolderCount = bundle.Folders.Count(f => f.ParentFolderId == bundle.Root.UniqueId);
        var totalLabel = childFolderCount > 0 ? $"{presetCount} presets, {childFolderCount} folders" : $"{presetCount}";
        if (!ImGui.CollapsingHeader($"{bundle.Root.FolderName}, Total: {totalLabel}###wiki-folder-{bundle.Root.UniqueId}", headerFlags))
            return;

        using (ImRaii.PushIndent()) {
            var popupId = $"ImportAll###{tab}-{bundle.Root.UniqueId}";
            if (ImGui.Button($"Import all###{tab}-{bundle.Root.UniqueId}"))
                ImGui.OpenPopup(popupId);

            ImGui.SameLine();
            ImGui.TextDisabled("Imports this folder and its subfolders");

            DrawWikiFolderNode(bundle.Root, bundle);

            using var folderPopup = ImRaii.Popup(popupId);
            if (!folderPopup)
                return;

            ImGui.TextWrapped($"Import {presetCount} preset(s) from '{tab} -> {bundle.Root.FolderName}'?");

            var name = bundle.Root.FolderName;
            ImGui.InputText(UIStrings.FolderName, ref name, 64, ImGuiInputTextFlags.ReadOnly);

            if (ImGui.Button(UIStrings.Import)) {
                var result = PresetImport.ImportFolderTree(
                    _fishingPreset,
                    bundle.Root,
                    bundle.Folders,
                    bundle.Presets,
                    CommunityOptions());

                if (result.ImportedPresets > 0) {
                    Service.Save();
                    Notify.Success($"Imported {result.ImportedPresets} preset(s) into {result.FoldersAdded} folder(s){(result.SkippedPresets > 0 ? $", skipped {result.SkippedPresets} duplicate(s)" : string.Empty)}.");
                }
                else {
                    Notify.Info("No new presets to import.");
                }

                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button(UIStrings.DrawImportExport_Cancel))
                ImGui.CloseCurrentPopup();
        }
    }

    private void DrawWikiFolderNode(PresetFolder folder, WikiFolderExport bundle) {
        var children = bundle.Folders.Where(f => f.ParentFolderId == folder.UniqueId).ToList();
        var directPresets = bundle.Presets.Where(p => folder.PresetIds.Contains(p.UniqueId)).Cast<BasePresetConfig>().ToList();

        // Root is already shown as the collapsing header; only nest children under it
        if (folder.UniqueId != bundle.Root.UniqueId) {
            var flags = SearchActive ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            using var tree = ImRaii.TreeNode($"{folder.FolderName} ({directPresets.Count + children.Count})###wiki-node-{folder.UniqueId}", flags);
            if (!tree)
                return;

            using (ImRaii.PushIndent()) {
                foreach (var child in children)
                    DrawWikiFolderNode(child, bundle);

                foreach (var preset in directPresets)
                    DrawPresetSelectable(preset);
            }

            return;
        }

        foreach (var child in children)
            DrawWikiFolderNode(child, bundle);

        foreach (var preset in directPresets)
            DrawPresetSelectable(preset);
    }

    private void DrawPresetSelectable(BasePresetConfig item) {
        var color = ImGuiColors.DalamudWhite;
        if (item is CustomPresetConfig customPreset) {
            if (_fishingPreset.PresetList.Any(p => p.PresetName == customPreset.PresetName))
                color = ImGuiColors.ParsedGreen;
        }
        else if (item is AutoGigConfig gigPreset) {
            if (_gigPreset.Presets.Any(p => p.PresetName == gigPreset.PresetName))
                color = ImGuiColors.ParsedGreen;
        }

        using (var a = ImRaii.PushColor(ImGuiCol.Text, color)) {
            ImGui.Selectable($"- {item.PresetName}");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                ImGui.OpenPopup($"PresetOptions###{item.PresetName}");
        }

        ImportPreset(item);
    }

    private static void ImportAllSpearfishingPresets(List<BasePresetConfig> list) {
        var imported = 0;
        var skipped = 0;

        foreach (var preset in list) {
            if (preset is CustomPresetConfig custom) {
                if (_fishingPreset.PresetList.Any(p => p.PresetName == custom.PresetName)) {
                    skipped++;
                    continue;
                }
                _fishingPreset.AddNewPreset(custom);
                imported++;
            }
            else if (preset is AutoGigConfig gig) {
                if (_gigPreset.Presets.Any(p => p.PresetName == gig.PresetName)) {
                    skipped++;
                    continue;
                }
                _gigPreset.AddNewPreset(gig);
                imported++;
            }
        }

        if (imported > 0)
            Notify.Success($"Imported {imported} preset(s){(skipped > 0 ? $", skipped {skipped} duplicate(s)" : string.Empty)}.");
        else
            Notify.Info("No new presets to import.");
    }

    public static void ImportPreset(BasePresetConfig preset) {
        using var ctx = ImRaii.ContextPopupItem(@$"PresetOptions###{preset.PresetName}");
        if (!ctx.Success) return;

        var name = preset.PresetName;
        if (preset.PresetName.StartsWith(@"[Old Version]"))
            ImGui.TextColored(ImGuiColors.ParsedOrange, UIStrings.Old_Preset_Warning);
        else
            ImGui.TextWrapped(UIStrings.ImportThisPreset);

        if (ImGui.InputText(UIStrings.PresetName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
            preset.RenamePreset(name);

        if (ImGui.Button(UIStrings.Import)) {
            if (preset is CustomPresetConfig customPreset) {
                var result = PresetImport.ImportPresets(_fishingPreset, [customPreset], CommunityOptions());
                if (result.ImportedPresets == 0) {
                    Notify.Info("No new presets to import.");
                    ImGui.CloseCurrentPopup();
                    return;
                }

                Service.Save();
            }
            else if (preset is AutoGigConfig gigPreset) {
                _gigPreset.AddNewPreset(gigPreset);
            }

            Notify.Success(UIStrings.PresetImported);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGui.Button(UIStrings.DrawImportExport_Cancel))
            ImGui.CloseCurrentPopup();
    }

    private static void OpenWiki() {
        var url = "https://github.com/PunishXIV/AutoHook/wiki";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}
