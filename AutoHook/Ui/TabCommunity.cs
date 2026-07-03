using AutoHook.Spearfishing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using ECommons.Throttlers;
using Newtonsoft.Json;
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
                    var list = value.Where(x => x.folder == null).SelectMany(x => x.Presets).Cast<BasePresetConfig>().ToList();
                    var foldered = value.Where(x => x.folder != null)
                        .Select(x => new KeyValuePair<PresetFolder, List<BasePresetConfig>>(x.folder!, [.. x.Presets.Cast<BasePresetConfig>()]))
                        .ToDictionary(kv => kv.Key!, kv => kv.Value);
                    var (filteredList, filteredFoldered) = FilterPresets(key, list, foldered);
                    if (SearchActive && filteredList.Count == 0 && filteredFoldered is not { Count: > 0 })
                        continue;

                    ImGui.Indent();
                    DrawHeaderList(key, filteredList, filteredFoldered);
                    ImGui.Unindent();
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

                    ImGui.Indent();
                    DrawHeaderList(key, list);
                    ImGui.Unindent();
                }
            }
        }
    }

    private (List<BasePresetConfig> list, Dictionary<PresetFolder, List<BasePresetConfig>>? foldered) FilterPresets(string category, List<BasePresetConfig> list, Dictionary<PresetFolder, List<BasePresetConfig>>? foldered) {
        if (!SearchActive)
            return (list, foldered);

        if (MatchesSearch(category))
            return (list, foldered);

        var filteredList = list.Where(p => MatchesSearch(p.PresetName)).ToList();
        Dictionary<PresetFolder, List<BasePresetConfig>>? filteredFoldered = null;

        if (foldered != null) {
            foreach (var bundle in foldered) {
                if (MatchesSearch(bundle.Key.FolderName)) {
                    filteredFoldered ??= [];
                    filteredFoldered[bundle.Key] = bundle.Value;
                }
                else {
                    var filtered = bundle.Value.Where(p => MatchesSearch(p.PresetName)).ToList();
                    if (filtered.Count > 0) {
                        filteredFoldered ??= [];
                        filteredFoldered[bundle.Key] = filtered;
                    }
                }
            }
        }

        return (filteredList, filteredFoldered);
    }

    private static int GetWikiCategoryTotal(List<BasePresetConfig> list, Dictionary<PresetFolder, List<BasePresetConfig>>? folderedPresets) {
        var total = list.Count;
        if (folderedPresets == null)
            return total;

        foreach (var bundle in folderedPresets)
            total += 1 + bundle.Value.Count;

        return total;
    }

    private static bool IsFishingPresetList(List<BasePresetConfig> list, Dictionary<PresetFolder, List<BasePresetConfig>>? folderedPresets) {
        if (list.Count > 0)
            return list[0] is CustomPresetConfig;
        return folderedPresets?.Values.FirstOrDefault()?.FirstOrDefault() is CustomPresetConfig;
    }

    private static (int imported, int skipped, List<Guid> guids) CloneAndImportFishingPresets(IEnumerable<BasePresetConfig> presets) {
        var importedGuids = new List<Guid>();
        var imported = 0;
        var skipped = 0;

        foreach (var preset in presets) {
            if (preset is not CustomPresetConfig custom)
                continue;

            if (_fishingPreset.PresetList.Any(p => p.PresetName == custom.PresetName)) {
                skipped++;
                continue;
            }

            var json = JsonConvert.SerializeObject(custom);
            var copy = JsonConvert.DeserializeObject<CustomPresetConfig>(json);
            copy!.UniqueId = Guid.NewGuid();
            _fishingPreset.CustomPresets.Add(copy);
            importedGuids.Add(copy.UniqueId);
            imported++;
        }

        return (imported, skipped, importedGuids);
    }

    private void ImportAllFishingCategory(string tab, List<BasePresetConfig> list, Dictionary<PresetFolder, List<BasePresetConfig>>? folderedPresets) {
        var totalImported = 0;
        var totalSkipped = 0;
        var hasSubfolders = folderedPresets is { Count: > 0 };

        if (!hasSubfolders) {
            if (list.Count == 0) {
                Notify.Info("No new presets to import.");
                return;
            }

            var folderName = _importAllFolderNames.TryGetValue(tab, out var n) && !string.IsNullOrWhiteSpace(n)
                ? n
                : tab;

            var (imported, skipped, guids) = CloneAndImportFishingPresets(list);
            totalImported = imported;
            totalSkipped = skipped;

            if (guids.Count > 0) {
                var newFolder = new PresetFolder(folderName);
                foreach (var id in guids)
                    newFolder.AddPreset(id);
                _fishingPreset.Folders.Add(newFolder);
                Service.Save();
                Notify.Success($"Imported {totalImported} preset(s) into folder '{folderName}'{(totalSkipped > 0 ? $", skipped {totalSkipped} duplicate(s)" : string.Empty)}.");
            }
            else {
                Notify.Info("No new presets to import.");
            }

            return;
        }

        var parentFolderName = _importAllFolderNames.TryGetValue(tab, out var name) && !string.IsNullOrWhiteSpace(name) ? name : tab;
        PresetFolder? parentFolder = null;
        var childFolders = new List<PresetFolder>();

        if (list.Count > 0) {
            var (imported, skipped, guids) = CloneAndImportFishingPresets(list);
            totalImported += imported;
            totalSkipped += skipped;

            if (guids.Count > 0) {
                parentFolder = new PresetFolder(parentFolderName);
                foreach (var id in guids)
                    parentFolder.AddPreset(id);
            }
        }

        foreach (var bundle in folderedPresets!) {
            var (imported, skipped, guids) = CloneAndImportFishingPresets(bundle.Value);
            totalImported += imported;
            totalSkipped += skipped;

            if (guids.Count == 0)
                continue;

            parentFolder ??= new PresetFolder(parentFolderName);

            var childFolder = new PresetFolder(bundle.Key.FolderName) {
                ParentFolderId = parentFolder.UniqueId
            };
            foreach (var id in guids)
                childFolder.AddPreset(id);
            childFolders.Add(childFolder);
        }

        if (parentFolder == null) {
            Notify.Info("No new presets to import.");
            return;
        }

        _fishingPreset.Folders.Add(parentFolder);
        foreach (var childFolder in childFolders)
            _fishingPreset.Folders.Add(childFolder);

        var foldersCreated = 1 + childFolders.Count;
        Service.Save();
        Notify.Success($"Imported {totalImported} preset(s) into {foldersCreated} folder(s){(totalSkipped > 0 ? $", skipped {totalSkipped} duplicate(s)" : string.Empty)}.");
    }

    private void DrawHeaderList(string tab, List<BasePresetConfig> list, Dictionary<PresetFolder, List<BasePresetConfig>>? folderedPresets = null) {
        var total = GetWikiCategoryTotal(list, folderedPresets);
        var headerFlags = SearchActive ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        if (ImGui.CollapsingHeader($"{tab}, Total: {total}", headerFlags)) {
            ImGui.Indent();

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
                foreach (var bundle in folderedPresets) {
                    if (ImGui.CollapsingHeader($"{bundle.Key.FolderName}, Total: {bundle.Value.Count}", headerFlags)) {
                        using (ImRaii.PushIndent()) {
                            // Import-all with confirmation (and folder creation for fishing presets)
                            if (ImGui.Button($"Import all###{tab}-{bundle.Key.FolderName}")) {
                                if (!_importAllFolderNames.ContainsKey(tab))
                                    _importAllFolderNames[tab] = tab;
                                ImGui.OpenPopup($"ImportAll###{tab}-{bundle.Key.FolderName}");
                            }

                            ImGui.SameLine();
                            ImGui.TextDisabled("Imports this folder's presets only");

                            foreach (var item in bundle.Value) {
                                var color = ImGuiColors.DalamudWhite;
                                // check if the preset is fishing or autogig and if already in the list
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
                                    // Also open the import menu on left-click
                                    var popupId = $"PresetOptions###{item.PresetName}";
                                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                                        ImGui.OpenPopup(popupId);
                                }
                                ImportPreset(item);
                            }

                            // AHFOLDER IMPORTS
                            using var folderPopup = ImRaii.Popup($"ImportAll###{tab}-{bundle.Key.FolderName}");
                            if (!folderPopup) continue;

                            var isFishing = bundle.Value.Count > 0 && bundle.Value[0] is CustomPresetConfig;

                            ImGui.TextWrapped($"Import {bundle.Value.Count} preset(s) from '{tab} -> {bundle.Key.FolderName}'?");

                            if (isFishing) {
                                var name = bundle.Key.FolderName;
                                if (ImGui.InputText(UIStrings.FolderName, ref name, 64, ImGuiInputTextFlags.ReadOnly))
                                    _importAllFolderNames[tab] = name;
                            }

                            // Import / Cancel buttons
                            if (ImGui.Button(UIStrings.Import)) {
                                if (isFishing) {
                                    var (imported, skipped, guids) = CloneAndImportFishingPresets(bundle.Value);
                                    if (guids.Count > 0) {
                                        var newFolder = new PresetFolder(bundle.Key.FolderName);
                                        foreach (var id in guids)
                                            newFolder.AddPreset(id);
                                        _fishingPreset.Folders.Add(newFolder);
                                        Service.Save();
                                        Notify.Success($"Imported {imported} preset(s) into folder '{bundle.Key.FolderName}'{(skipped > 0 ? $", skipped {skipped} duplicate(s)" : string.Empty)}.");
                                    }
                                    else {
                                        Notify.Info("No new presets to import.");
                                    }

                                    ImGui.CloseCurrentPopup();
                                }
                            }

                            ImGui.SameLine();

                            if (ImGui.Button(UIStrings.DrawImportExport_Cancel)) {
                                ImGui.CloseCurrentPopup();
                            }
                        }
                    }
                }
            }

            foreach (var item in list) {
                var color = ImGuiColors.DalamudWhite;
                // check if the preset is fishing or autogig and if already in the list
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

                    // Also open the import menu on left-click
                    var popupId = $"PresetOptions###{item.PresetName}";
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                        ImGui.OpenPopup(popupId);
                }

                ImportPreset(item);
            }

            ImGui.Unindent();
        }
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
    }

    private static void OpenWiki() {
        var url = "https://github.com/PunishXIV/AutoHook/wiki";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}
