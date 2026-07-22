using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using Newtonsoft.Json;
using System.Numerics;

namespace AutoHook.Ui;

public class TabFishingPresets : BaseTab {
    public override bool Enabled => true;
    public override string TabName => UIStrings.FishingPresets;

    public override OpenWindow Type => OpenWindow.FishingPreset;

    private static readonly FishingPresets _basePreset = Service.Configuration.HookPresets;

    public static bool OpenPresetGen;
    private readonly PresetCreator PresetCreator = new();

    private string newFolderName = string.Empty;
    private bool promptingForFolderName = false;
    private Guid? _parentFolderForNewFolder = null;

    private string renameFolderName = string.Empty;
    private Guid? renameFolderId = null;

    private BasePresetConfig? _tempImportPreset = null;
    private (PresetFolder Folder, List<PresetFolder> Folders, List<CustomPresetConfig> Presets)? _tempImportFolder = null;
    private string _tempImportName = string.Empty;
    private bool _isImportingFolder = false;

    private Dictionary<Guid, bool> _selectedPresetsForImport = [];
    private Dictionary<Guid, string> _presetImportNames = [];
    private Guid? _renamePresetId = null;

    private string _searchFilter = string.Empty;

    public override void DrawHeader() {
        DrawTabDescription(UIStrings.TabPresets_DrawHeader_NewTabDescription);

        if (OpenPresetGen)
            DrawPresetGenTab();
    }

    private void DrawPresetGenTab() {
        using var id = ImRaii.PushId(@"PresetGen");
        ImGui.SetNextWindowSize(new Vector2(420.Scaled(), 0), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(360.Scaled(), 180.Scaled()), new Vector2(480.Scaled(), 720.Scaled()));
        if (ImGui.Begin(UIStrings.PresetGen, ref OpenPresetGen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.AlwaysUseWindowPadding))
            PresetCreator.DrawPresetGenerator();

        ImGui.End();
    }

    public override void Draw() {
        try {
            DrawList();
        }
        catch (Exception e) {
            Svc.Log.Error(e, "[TabFishingPresets] Draw failed.");
        }
    }

    private static BasePresetConfig? displayed = _basePreset.SelectedPreset ?? _basePreset.DefaultPreset;

    private static bool IsAnonymousPreset(CustomPresetConfig preset)
        => preset.IsAnonymous;

    private static string GetAnonymousPresetDisplayName(string presetName)
        => presetName.StartsWith(CustomPresetConfig.AnonymousPresetPrefix, StringComparison.Ordinal) ? presetName[CustomPresetConfig.AnonymousPresetPrefix.Length..] : presetName;

    private void DrawList() {
        using var table = ImRaii.Table($"###PresetTable", 2, ImGuiTableFlags.Resizable);
        if (!table)
            return;

        ImGui.TableSetupColumn($"###OptionColumn", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn($"###PresetColumn", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableNextColumn();
        using (var left = ImRaii.Child($"###OptionSide"))
            DrawPresetOptions(displayed);

        ImGui.TableNextColumn();
        using var right = ImRaii.Child($"###PresetSide");
        DrawPresetButtons();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##PresetSearch", UIStrings.Search_Hint, ref _searchFilter, 128);

        if (promptingForFolderName) {
            DrawCreateFolderPopup();
        }

        if (renameFolderId != null) {
            DrawRenameFolderPopup();
        }

        using var list = ImRaii.ListBox("preset_list", ImGui.GetContentRegionAvail());
        if (!list)
            return;

        var searchActive = !string.IsNullOrWhiteSpace(_searchFilter);
        var searchFilter = searchActive ? _searchFilter.Trim() : string.Empty;
        bool MatchesSearch(string name) => !searchActive || name.Contains(searchFilter, StringComparison.InvariantCultureIgnoreCase);

        var presetsInFolders = BuildPresetsInFoldersSet();
        Dictionary<Guid, CustomPresetConfig>? presetById = searchActive
            ? _basePreset.CustomPresets.ToDictionary(p => p.UniqueId)
            : null;

        DrawGlobalPresetItem(searchActive, MatchesSearch);

        var anonPresets = _basePreset.CustomPresets.Where(IsAnonymousPreset).ToList();
        if (searchActive) {
            anonPresets = [.. anonPresets.Where(p => MatchesSearch(p.PresetName) || MatchesSearch(GetAnonymousPresetDisplayName(p.PresetName)))];
        }

        if (anonPresets.Count > 0)
            DrawAnonymousPresetsSection(anonPresets, searchActive);

        ImGui.Separator();

        // Draw folders
        for (var folderIndex = 0; folderIndex < _basePreset.Folders.Count; folderIndex++) {
            var folder = _basePreset.Folders[folderIndex];

            // Only draw top-level folders here; child folders are drawn within their parents
            if (folder.ParentFolderId.HasValue)
                continue;
            if (searchActive) {
                var folderNameMatches = MatchesSearch(folder.FolderName);
                var anyPresetMatches = GetAllPresetIdsInFolderTree(folder).Any(id => {
                    if (presetById == null || !presetById.TryGetValue(id, out var p))
                        return false;
                    if (IsAnonymousPreset(p))
                        return false;
                    return MatchesSearch(p.PresetName);
                });
                if (!folderNameMatches && !anyPresetMatches)
                    continue;
            }

            DrawFolder(folder, folderIndex);
        }

        // Draw non-folder presets
        var customPresets = _basePreset.CustomPresets;
        for (var i = 0; i < customPresets.Count; i++) {
            var preset = customPresets[i];

            if (presetsInFolders.Contains(preset.UniqueId))
                continue;

            if (IsAnonymousPreset(preset))
                continue;

            if (searchActive && !MatchesSearch(preset.PresetName))
                continue;

            DrawItem(preset, i);
        }
    }

    private void DrawGlobalPresetItem(bool searchActive, Func<string, bool> matchesSearch) {
        if (searchActive && !matchesSearch(UIStrings.GlobalPreset))
            return;

        DrawUtil.Info(UIStrings.GlobalPresetHelpText);
        ImGui.SameLine(0, 4.Scaled());

        var globalActive = string.IsNullOrEmpty(_basePreset.SelectedGuid);
        var color = globalActive ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudWhite;
        using (ImRaii.PushColor(ImGuiCol.Text, color)) {
            if (ImGui.Selectable((globalActive ? "> " : "") + UIStrings.GlobalPreset,
                    displayed?.PresetName == _basePreset.DefaultPreset.PresetName,
                    ImGuiSelectableFlags.AllowDoubleClick)) {
                displayed = _basePreset.DefaultPreset;

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
                    _basePreset.SelectedPreset = null;
                    Service.Save();
                }
            }
        }
    }

    private void DrawAnonymousPresetsSection(List<CustomPresetConfig> anonPresets, bool searchActive) {
        var headerFlags = ImGuiTreeNodeFlags.None;
        if (searchActive || anonPresets.Any(p => _basePreset.SelectedGuid == p.UniqueId.ToString()))
            headerFlags = ImGuiTreeNodeFlags.DefaultOpen;

        if (!ImGui.CollapsingHeader(string.Format(UIStrings.AnonymousPresets_Header, anonPresets.Count), headerFlags))
            return;

        using var indent = ImRaii.PushIndent(10.Scaled());
        foreach (var preset in anonPresets)
            DrawAnonymousItem(preset);
    }

    private void DrawAnonymousItem(CustomPresetConfig preset) {
        using var id = ImRaii.PushId(preset.UniqueId.ToString());
        var selected = _basePreset.SelectedGuid == preset.UniqueId.ToString();
        var color = selected ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudWhite;
        using (var a = ImRaii.PushColor(ImGuiCol.Text, color)) {
            if (ImGui.Selectable((selected ? "> " : "") + preset.PresetName,
                    displayed?.UniqueId == preset.UniqueId,
                    ImGuiSelectableFlags.AllowDoubleClick)) {
                displayed = preset;

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
                    _basePreset.SelectedPreset = selected ? null : preset;
                    Service.Save();
                }
            }
        }

        ImGui.TooltipOnHover(UIStrings.RightClickOptions);

        DrawPresetContext(preset);
    }

    private HashSet<Guid> BuildPresetsInFoldersSet() {
        var result = new HashSet<Guid>();
        foreach (var folder in _basePreset.Folders) {
            foreach (var presetId in folder.PresetIds)
                result.Add(presetId);
        }

        return result;
    }

    private IEnumerable<Guid> GetAllPresetIdsInFolderTree(PresetFolder rootFolder) {
        var result = new List<Guid>();
        Collect(rootFolder, result);
        return result;

        static void Collect(PresetFolder folder, List<Guid> acc) {
            acc.AddRange(folder.PresetIds);

            for (var i = 0; i < _basePreset.Folders.Count; i++) {
                var child = _basePreset.Folders[i];
                if (child.ParentFolderId == folder.UniqueId) {
                    Collect(child, acc);
                }
            }
        }
    }

    private void CopyFolderTree(PresetFolder source, Guid? parentFolderId, bool prefixName) {
        var newFolder = new PresetFolder(prefixName ? $"Copy_{source.FolderName}" : source.FolderName) {
            ParentFolderId = parentFolderId,
            IsExpanded = source.IsExpanded
        };

        foreach (var presetId in source.PresetIds) {
            var originalPreset = _basePreset.CustomPresets.FirstOrDefault(p => p.UniqueId == presetId);
            if (originalPreset == null)
                continue;

            var json = JsonConvert.SerializeObject(originalPreset);
            var presetCopy = JsonConvert.DeserializeObject<CustomPresetConfig>(json);
            presetCopy!.UniqueId = Guid.NewGuid();
            presetCopy.PresetName = originalPreset.PresetName;

            _basePreset.CustomPresets.Add(presetCopy);
            newFolder.AddPreset(presetCopy.UniqueId);
        }

        _basePreset.Folders.Add(newFolder);

        foreach (var childFolder in _basePreset.Folders.Where(f => f.ParentFolderId == source.UniqueId))
            CopyFolderTree(childFolder, newFolder.UniqueId, prefixName: false);
    }

    private static bool FolderTreeHasSelectedPresets(PresetFolder folder, List<PresetFolder> allFolders, HashSet<Guid> selectedPresetIds) {
        if (folder.PresetIds.Any(selectedPresetIds.Contains))
            return true;

        return allFolders
            .Where(f => f.ParentFolderId == folder.UniqueId)
            .Any(child => FolderTreeHasSelectedPresets(child, allFolders, selectedPresetIds));
    }

    private bool IsFolderDescendantOf(PresetFolder potentialAncestor, PresetFolder candidate) {
        var currentParentId = candidate.ParentFolderId;
        while (currentParentId.HasValue) {
            if (currentParentId.Value == potentialAncestor.UniqueId)
                return true;

            var parent = _basePreset.Folders.FirstOrDefault(f => f.UniqueId == currentParentId.Value);
            if (parent == null)
                break;

            currentParentId = parent.ParentFolderId;
        }

        return false;
    }

    private int GetFolderImmediateItemCount(PresetFolder folder) {
        var directChildFolderCount = _basePreset.Folders.Count(f => f.ParentFolderId == folder.UniqueId);
        return folder.PresetIds.Count + directChildFolderCount;
    }

    private void DrawFolder(PresetFolder folder, int folderIndex) {
        bool isOpen;
        using (var id = ImRaii.PushId($"folder_{folder.UniqueId}")) {
            var icon = folder.IsExpanded ? FontAwesomeIcon.FolderOpen : FontAwesomeIcon.Folder;

            // Check if this folder contains the selected preset
            var containsSelectedPreset = false;
            if (_basePreset.SelectedPreset != null) {
                containsSelectedPreset = folder.PresetIds.Contains(_basePreset.SelectedPreset.UniqueId);
            }

            // Use orange color for folders containing the selected preset
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange, containsSelectedPreset)) {
                // Display folder name with item count
                var displayName = $"{folder.FolderName} ({GetFolderImmediateItemCount(folder)})";

                // Draw folder with tree node
                isOpen = ImGui.TreeNodeEx(displayName,
                    ImGuiTreeNodeFlags.AllowItemOverlap |
                    ImGuiTreeNodeFlags.SpanAvailWidth |
                    (folder.IsExpanded ? ImGuiTreeNodeFlags.DefaultOpen : 0));
            }

            // Handle drag and drop onto folder
            if (ImGui.BeginDragDropTarget()) {
                // Accept preset drops from outside folders
                if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_ORDER", out int itemIndex)) {
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                        var preset = _basePreset.CustomPresets[itemIndex];
                        folder.AddPreset(preset.UniqueId);
                        Service.Save();
                    }
                }

                // Accept preset drops from inside folders
                if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_IN_FOLDER", out Guid presetId)) {
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                        // First, find which folder this preset is coming from
                        PresetFolder? sourceFolder = null;
                        foreach (var otherFolder in _basePreset.Folders) {
                            if (otherFolder.PresetIds.Contains(presetId)) {
                                sourceFolder = otherFolder;
                                break;
                            }
                        }

                        // Now handle the move
                        if (sourceFolder != null && sourceFolder.UniqueId != folder.UniqueId) {
                            // Remove from source folder
                            var sourcePresetIds = new List<Guid>(sourceFolder.PresetIds);
                            sourcePresetIds.Remove(presetId);
                            sourceFolder.PresetIds = sourcePresetIds;

                            // Add to target folder if not already there
                            if (!folder.PresetIds.Contains(presetId)) {
                                folder.AddPreset(presetId);
                            }

                            Service.Save();
                        }
                        else if (sourceFolder == null) {
                            // If not found in any folder (shouldn't happen, but just in case)
                            folder.AddPreset(presetId);
                            Service.Save();
                        }
                    }
                }

                ImGui.EndDragDropTarget();
            }

            // Folder drag source
            if (ImGui.BeginDragDropSource()) {
                ImGuiDragDrop.SetDragDropPayload("FOLDER_ORDER", folderIndex);
                ImGui.Text($"{UIStrings.MovingFolder_} {folder.FolderName}");

                ImGui.EndDragDropSource();
            }

            // Handle folder reparenting / reordering by dropping one folder onto another
            if (ImGui.BeginDragDropTarget()) {
                if (ImGuiDragDrop.AcceptDragDropPayload("FOLDER_ORDER", out int sourceFolderIndex)) {
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && sourceFolderIndex != folderIndex) {
                        if (sourceFolderIndex >= 0 && sourceFolderIndex < _basePreset.Folders.Count) {
                            var movingFolder = _basePreset.Folders[sourceFolderIndex];

                            // If both folders share the same parent, treat drop as a reorder within that level
                            if (movingFolder.ParentFolderId == folder.ParentFolderId) {
                                _basePreset.Folders.RemoveAt(sourceFolderIndex);
                                var targetIndex = _basePreset.Folders.IndexOf(folder);
                                if (targetIndex < 0) {
                                    // Fallback: append if target somehow not found
                                    _basePreset.Folders.Add(movingFolder);
                                }
                                else {
                                    _basePreset.Folders.Insert(targetIndex, movingFolder);
                                }
                                Service.Save();
                            }
                            else {
                                // Special-case: if dropping a parent onto its direct child, swap places
                                if (folder.ParentFolderId == movingFolder.UniqueId) {
                                    var oldParent = movingFolder.ParentFolderId;
                                    movingFolder.ParentFolderId = folder.UniqueId;
                                    folder.ParentFolderId = oldParent;
                                    Service.Save();
                                }
                                // Otherwise, prevent creating cycles (cannot parent a folder under its own descendant)
                                else if (!IsFolderDescendantOf(movingFolder, folder)) {
                                    movingFolder.ParentFolderId = folder.UniqueId;
                                    Service.Save();
                                }
                            }
                        }
                    }
                }

                ImGui.EndDragDropTarget();
            }

            // Right click for context menu
            DrawFolderContextMenu(folder);

            // Update folder expand state
            if (isOpen != folder.IsExpanded) {
                folder.IsExpanded = isOpen;
                Service.Save();
            }
        }

        // Draw folder contents if expanded
        if (isOpen) {
            // Draw child folders
            for (var childIndex = 0; childIndex < _basePreset.Folders.Count; childIndex++) {
                var childFolder = _basePreset.Folders[childIndex];
                if (childFolder.ParentFolderId == folder.UniqueId) {
                    DrawFolder(childFolder, childIndex);
                }
            }

            foreach (var presetId in folder.PresetIds) {
                var preset = _basePreset.CustomPresets.FirstOrDefault(p => p.UniqueId == presetId);
                if (preset != null) {
                    if (IsAnonymousPreset(preset))
                        continue;

                    if (!string.IsNullOrWhiteSpace(_searchFilter) &&
                        !preset.PresetName.Contains(_searchFilter.Trim(), StringComparison.InvariantCultureIgnoreCase))
                        continue;

                    var index = _basePreset.CustomPresets.IndexOf(preset);
                    DrawItemInFolder(preset, index, folder);
                }
            }

            ImGui.TreePop();
        }
    }

    private void DrawItemInFolder(CustomPresetConfig preset, int i, PresetFolder folder) {
        using var id = ImRaii.PushId(preset.UniqueId.ToString());
        var selected = _basePreset.SelectedGuid == preset.UniqueId.ToString();
        var color = selected ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudWhite;

        // Indent to show hierarchy
        ImGui.Indent(10.Scaled());

        using (var a = ImRaii.PushColor(ImGuiCol.Text, color)) {
            if (ImGui.Selectable((selected ? "> " : "") + preset.PresetName,
                    displayed?.UniqueId == preset.UniqueId,
                    ImGuiSelectableFlags.AllowDoubleClick)) {
                displayed = preset;

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
                    _basePreset.SelectedPreset = selected ? null : preset;
                    Service.Save();
                }
            }
        }

        ImGui.Unindent(10.Scaled());

        if (ImGui.BeginDragDropSource()) {
            // Use a different drag type to identify presets from folders
            ImGuiDragDrop.SetDragDropPayload("PRESET_IN_FOLDER", preset.UniqueId);
            ImGui.Text($"{UIStrings.Moving_} {preset.PresetName}");
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget()) {
            if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_IN_FOLDER", out Guid presetId)) {
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                    try {
                        // Find where to place in the folder
                        var targetIndex = folder.PresetIds.IndexOf(preset.UniqueId);
                        if (targetIndex >= 0) {
                            // Create a new list to avoid modifying the collection during enumeration
                            var newPresetIds = new List<Guid>(folder.PresetIds);

                            // Find the current index of the preset being moved
                            var currentIndex = newPresetIds.IndexOf(presetId);

                            // Only reorder if the preset is in this folder
                            if (currentIndex >= 0) {
                                // Remove from current position and insert at target position
                                newPresetIds.RemoveAt(currentIndex);
                                newPresetIds.Insert(targetIndex, presetId);

                                // Replace the folder's preset list with our reordered one
                                folder.PresetIds = newPresetIds;
                                Service.Save();
                            }
                        }
                    }
                    catch (Exception ex) {
                        Svc.Log.Error(ex, "[TabFishingPresets] Error reordering presets.");
                    }
                }
            }

            // Allow dropping a folder here to reparent it to this preset's folder level
            if (ImGuiDragDrop.AcceptDragDropPayload("FOLDER_ORDER", out int sourceFolderIndex)) {
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                    if (sourceFolderIndex >= 0 && sourceFolderIndex < _basePreset.Folders.Count) {
                        var movingFolder = _basePreset.Folders[sourceFolderIndex];

                        // Determine target folder level (the folder that contains this preset)
                        var targetFolder = _basePreset.GetFolderContainingPreset(preset.UniqueId);

                        // If the preset is in the moving folder's subtree, ignore to avoid cycles
                        if (targetFolder != null && (targetFolder.UniqueId == movingFolder.UniqueId || IsFolderDescendantOf(movingFolder, targetFolder))) {
                            // do nothing
                        }
                        else {
                            // If preset is in a folder, become sibling at that level; otherwise become root-level
                            movingFolder.ParentFolderId = targetFolder?.UniqueId;
                            Service.Save();
                        }
                    }
                }
            }

            ImGui.EndDragDropTarget();
        }

        ImGui.TooltipOnHover(UIStrings.RightClickOptions);

        DrawPresetContext(preset);
    }

    private void DrawItem(CustomPresetConfig preset, int i) {
        using var id = ImRaii.PushId(preset.UniqueId.ToString());
        var selected = _basePreset.SelectedGuid == preset.UniqueId.ToString();
        var color = selected ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudWhite;
        using (var a = ImRaii.PushColor(ImGuiCol.Text, color)) {
            if (ImGui.Selectable((selected ? "> " : "") + preset.PresetName,
                    displayed?.UniqueId == preset.UniqueId,
                    ImGuiSelectableFlags.AllowDoubleClick)) {
                displayed = preset;

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
                    _basePreset.SelectedPreset = selected ? null : preset;
                    Service.Save();
                }
            }
        }

        if (ImGui.BeginDragDropSource()) {
            ImGuiDragDrop.SetDragDropPayload("PRESET_ORDER", i);
            ImGui.Text($"{UIStrings.Moving_} {preset.PresetName}");
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget()) {
            if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_ORDER", out int itemIndex)) {
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                    _basePreset.SwapIndex(itemIndex, i);
                }
            }

            // Handle dropping from folders
            if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_IN_FOLDER", out Guid presetId)) {
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                    // Remove from any folder
                    foreach (var folder in _basePreset.Folders) {
                        folder.RemovePreset(presetId);
                    }

                    // Reorder in the main list if needed
                    var draggedPreset = _basePreset.CustomPresets.FirstOrDefault(p => p.UniqueId == presetId);
                    var targetPreset = _basePreset.CustomPresets[i];
                    if (draggedPreset != null && targetPreset != null) {
                        var draggedIndex = _basePreset.CustomPresets.IndexOf(draggedPreset);
                        if (draggedIndex >= 0) {
                            _basePreset.SwapIndex(draggedIndex, i);
                        }
                    }

                    Service.Save();
                }
            }

            // Allow dropping a folder here to reparent it to this preset's level
            if (ImGuiDragDrop.AcceptDragDropPayload("FOLDER_ORDER", out int sourceFolderIndex)) {
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                    if (sourceFolderIndex >= 0 && sourceFolderIndex < _basePreset.Folders.Count) {
                        var movingFolder = _basePreset.Folders[sourceFolderIndex];

                        // Determine target folder level (the folder that contains this preset)
                        var targetFolder = _basePreset.GetFolderContainingPreset(preset.UniqueId);

                        // If the preset is in the moving folder's subtree, ignore to avoid cycles
                        if (targetFolder != null && (targetFolder.UniqueId == movingFolder.UniqueId || IsFolderDescendantOf(movingFolder, targetFolder))) {
                            // do nothing
                        }
                        else {
                            // If preset is in a folder, become sibling at that level; otherwise become root-level
                            movingFolder.ParentFolderId = targetFolder?.UniqueId;
                            Service.Save();
                        }
                    }
                }
            }

            ImGui.EndDragDropTarget();
        }

        ImGui.TooltipOnHover(UIStrings.RightClickOptions);

        DrawPresetContext(preset);
    }

    private void DrawPresetOptions(BasePresetConfig? preset) {
        if (preset == null)
            return;

        using var id = ImRaii.PushId("TabBarsPreset");

        preset.DrawOptions();
    }

    private void DrawPresetButtons() {
        if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowsSpin))
            OpenPresetGen = !OpenPresetGen;
        ImGui.TooltipOnHover(UIStrings.PresetGenerator);

        ImGui.SameLine(0, 3.Scaled());
        if (ImGuiComponents.IconButton(FontAwesomeIcon.FolderPlus)) {
            promptingForFolderName = true;
            _parentFolderForNewFolder = null;
        }
        ImGui.TooltipOnHover(UIStrings.CreateFolder);

        ImGui.SameLine(0, 3.Scaled());
        DrawUtil.DrawAddNewPresetButton(_basePreset);
        ImGui.SameLine(0, 3.Scaled());
        DrawCombinedImport();
    }

    private void DrawCombinedImport() {
        try {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport)) {
                var clipboardText = ImGui.GetClipboardText();

                // Try folder import first
                _tempImportFolder = Configuration.ImportFolder(clipboardText);
                if (_tempImportFolder.HasValue) {
                    _isImportingFolder = true;
                    ImGui.OpenPopup("import_new_preset");
                }
                else {
                    // Try preset import
                    _tempImportPreset = Configuration.ImportPreset(clipboardText);
                    if (_tempImportPreset != null) {
                        _isImportingFolder = false;
                        ImGui.OpenPopup("import_new_preset");
                    }
                    else {
                        Notify.Error("Invalid import data");
                    }
                }
            }

            ImGui.TooltipOnHover(UIStrings.ImportPresetOrFolder);

            using var popup = ImRaii.Popup("import_new_preset");

            if (popup.Success) {
                if (_isImportingFolder && _tempImportFolder.HasValue) {
                    // Handle folder import
                    var folder = _tempImportFolder.Value.Folder;
                    var name = folder.FolderName;

                    ImGui.TextWrapped(UIStrings.ImportFolderAndPresets);

                    if (ImGui.InputText(UIStrings.FolderName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
                        folder.FolderName = name;

                    // List of presets with checkboxes using TreeNodeEx
                    if (ImGui.TreeNodeEx($"{UIStrings.Presets_} {_tempImportFolder.Value.Presets.Count}", ImGuiTreeNodeFlags.DefaultOpen)) {
                        // Initialize selection states if not done yet
                        if (_selectedPresetsForImport == null || _selectedPresetsForImport.Count != _tempImportFolder.Value.Presets.Count) {
                            _selectedPresetsForImport = [];
                            _presetImportNames = [];

                            foreach (var preset in _tempImportFolder.Value.Presets) {
                                _selectedPresetsForImport[preset.UniqueId] = true; // Selected by default
                                _presetImportNames[preset.UniqueId] = preset.PresetName;
                            }
                        }

                        ImGui.Indent(10.Scaled());

                        foreach (var preset in _tempImportFolder.Value.Presets) {
                            using var presetId = ImRaii.PushId(preset.UniqueId.ToString());

                            // Checkbox for selection
                            var isSelected = _selectedPresetsForImport[preset.UniqueId];
                            if (ImGui.Checkbox("##selectPreset", ref isSelected)) {
                                _selectedPresetsForImport[preset.UniqueId] = isSelected;
                            }

                            ImGui.SameLine();

                            // Check if this preset is being renamed
                            if (_renamePresetId == preset.UniqueId) {
                                // Show input field for renaming
                                ImGui.SetNextItemWidth(200.Scaled());
                                if (ImGui.InputText("##renameField", ref _tempImportName, 100,
                                    ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll)) {
                                    // Apply rename on Enter
                                    _presetImportNames[preset.UniqueId] = _tempImportName;
                                    _renamePresetId = null;
                                }

                                // Also handle focus loss or clicking elsewhere
                                if (!ImGui.IsItemActive() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                                    _presetImportNames[preset.UniqueId] = _tempImportName;
                                    _renamePresetId = null;
                                }
                            }
                            else {
                                // Normal display of preset name
                                ImGui.Text(_presetImportNames[preset.UniqueId]);

                                ImGui.SameLine();

                                // Edit button
                                if (ImGuiComponents.IconButton(FontAwesomeIcon.Edit)) {
                                    _renamePresetId = preset.UniqueId;
                                    _tempImportName = _presetImportNames[preset.UniqueId];
                                }

                                if (ImGui.IsItemHovered())
                                    ImGui.SetTooltip(UIStrings.RenamePreset);
                            }
                        }

                        ImGui.Unindent(10.Scaled());
                        ImGui.TreePop();
                    }

                    ImGui.Separator();

                    if (ImGui.Button(UIStrings.Import, new Vector2(120.Scaled(), 0))) {
                        // Count how many presets are actually selected for import
                        var selectedCount = _tempImportFolder.Value.Presets.Count(p => _selectedPresetsForImport[p.UniqueId]);

                        // Create a new folder with the selected count in its name if no presets are selected
                        if (selectedCount == 0) {
                            Notify.Error(UIStrings.NoPresetsSelected);
                            return;
                        }

                        var selectedPresetIds = _tempImportFolder.Value.Presets
                            .Where(p => _selectedPresetsForImport[p.UniqueId])
                            .Select(p => p.UniqueId)
                            .ToHashSet();

                        foreach (var preset in _tempImportFolder.Value.Presets) {
                            if (!_selectedPresetsForImport[preset.UniqueId])
                                continue;

                            if (_presetImportNames.TryGetValue(preset.UniqueId, out var newName))
                                preset.PresetName = newName;

                            _basePreset.CustomPresets.Add(preset);
                        }

                        foreach (var importedFolder in _tempImportFolder.Value.Folders) {
                            importedFolder.PresetIds = [.. importedFolder.PresetIds.Where(selectedPresetIds.Contains)];
                        }

                        var foldersToAdd = _tempImportFolder.Value.Folders
                            .Where(f => FolderTreeHasSelectedPresets(f, _tempImportFolder.Value.Folders, selectedPresetIds))
                            .ToList();

                        foreach (var importedFolder in foldersToAdd)
                            _basePreset.Folders.Add(importedFolder);

                        Service.Save();
                        Notify.Success($"Folder imported with {selectedPresetIds.Count} presets");

                        _tempImportFolder = null;
                        _selectedPresetsForImport.Clear();
                        _presetImportNames.Clear();
                        _renamePresetId = null;
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.SameLine();

                    if (ImGui.Button(UIStrings.DrawImportExport_Cancel, new Vector2(120.Scaled(), 0))) {
                        _tempImportFolder = null;
                        _selectedPresetsForImport.Clear();
                        _presetImportNames.Clear();
                        _renamePresetId = null;
                        ImGui.CloseCurrentPopup();
                    }
                }
                else if (!_isImportingFolder && _tempImportPreset != null) {
                    // Handle preset import - EXACTLY matching the DrawImportPreset method
                    var name = _tempImportPreset.PresetName;

                    if (_tempImportPreset.PresetName.StartsWith(@"[Old Version]"))
                        ImGui.TextColored(ImGuiColors.ParsedOrange, UIStrings.Old_Preset_Warning);
                    else
                        ImGui.TextWrapped(UIStrings.ImportThisPreset);

                    if (ImGui.InputText(UIStrings.PresetName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
                        _tempImportPreset.RenamePreset(name);

                    if (ImGui.Button(UIStrings.Import, new Vector2(120.Scaled(), 0))) {
                        Service.Save();
                        _basePreset.AddNewPreset(_tempImportPreset);
                        _basePreset.SelectedPreset = (CustomPresetConfig)_tempImportPreset;
                        _tempImportPreset = null;
                        Service.Save();
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.SameLine();

                    if (ImGui.Button(UIStrings.DrawImportExport_Cancel, new Vector2(120.Scaled(), 0))) {
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
        }
        catch (Exception e) {
            Svc.Log.Error(e.ToString());
            Notify.Error(e.Message);
        }
    }

    private void DrawCreateFolderPopup() {
        ImGui.OpenPopup(UIStrings.CreateNewFolder);

        ImGui.SetNextWindowSize(new Vector2(300.Scaled(), 120.Scaled()));
        using var modal = ImRaii.PopupModal(UIStrings.CreateNewFolder, ref promptingForFolderName, ImGuiWindowFlags.NoResize);
        if (modal.Success) {
            ImGui.Text(UIStrings.FolderNameHint);
            ImGui.Separator();

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputText("##newFolderName", ref newFolderName, 100);

            ImGui.Spacing();

            if (ImGui.Button(UIStrings.Create, new Vector2(120.Scaled(), 0))) {
                if (!string.IsNullOrWhiteSpace(newFolderName)) {
                    if (_parentFolderForNewFolder.HasValue)
                        _basePreset.AddNewFolder(newFolderName, _parentFolderForNewFolder.Value);
                    else
                        _basePreset.AddNewFolder(newFolderName);
                    newFolderName = string.Empty;
                    promptingForFolderName = false;
                    _parentFolderForNewFolder = null;
                }
            }

            ImGui.SameLine();

            if (ImGui.Button(UIStrings.DrawImportExport_Cancel, new Vector2(120.Scaled(), 0))) {
                newFolderName = string.Empty;
                promptingForFolderName = false;
                _parentFolderForNewFolder = null;
            }
        }
    }

    private void DrawRenameFolderPopup() {
        ImGui.OpenPopup(UIStrings.RenameFolder);

        ImGui.SetNextWindowSize(new Vector2(300.Scaled(), 120.Scaled()));
        var isOpen = true;
        using var modal = ImRaii.PopupModal(UIStrings.RenameFolder, ref isOpen, ImGuiWindowFlags.NoResize);
        if (modal.Success) {
            ImGui.Text(UIStrings.EnterNewFolderName);
            ImGui.Separator();

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputText("##renameFolderName", ref renameFolderName, 100);

            ImGui.Spacing();

            if (ImGui.Button(UIStrings.Rename, new Vector2(120.Scaled(), 0))) {
                if (!string.IsNullOrWhiteSpace(renameFolderName) && renameFolderId.HasValue) {
                    var folder = _basePreset.Folders.FirstOrDefault(f => f.UniqueId == renameFolderId.Value);
                    if (folder != null) {
                        folder.FolderName = renameFolderName;
                        Service.Save();
                    }
                    renameFolderName = string.Empty;
                    renameFolderId = null;
                }
            }

            ImGui.SameLine();

            if (ImGui.Button(UIStrings.DrawImportExport_Cancel, new Vector2(120.Scaled(), 0))) {
                renameFolderName = string.Empty;
                renameFolderId = null;
            }

            if (!isOpen) {
                renameFolderName = string.Empty;
                renameFolderId = null;
            }
        }
    }

    private void DrawFolderContextMenu(PresetFolder folder) {
        using var ctx = ImRaii.ContextPopupItem(folder.UniqueId.ToString());
        if (!ctx.Success) return;

        if (ImGui.Selectable(UIStrings.CreateNewFolder, false)) {
            _parentFolderForNewFolder = folder.UniqueId;
            promptingForFolderName = true;
        }

        if (ImGui.Selectable(UIStrings.Rename, false, ImGuiSelectableFlags.DontClosePopups)) {
            renameFolderId = folder.UniqueId;
            renameFolderName = folder.FolderName;
        }

        if (ImGui.Selectable(UIStrings.MakeACopy, false)) {
            CopyFolderTree(folder, folder.ParentFolderId, prefixName: true);
            Service.Save();
        }

        if (ImGui.Selectable(UIStrings.ExportFolderClipboard, false)) {
            var exportData = Configuration.ExportFolder(folder, _basePreset.CustomPresets, _basePreset.Folders);
            ImGui.SetClipboardText(exportData);
            Notify.Success(UIStrings.FolderExported);
        }

        var isEmpty = folder.PresetIds.Count == 0;
        var hasChildFolders = _basePreset.Folders.Any(f => f.ParentFolderId == folder.UniqueId);
        var hasContents = !isEmpty || hasChildFolders;

        using (var disabled = ImRaii.Disabled(!ImGui.GetIO().KeyShift)) {
            if (ImGui.Selectable(UIStrings.Delete, false, ImGuiSelectableFlags.DontClosePopups)) {
                if (hasContents)
                    _basePreset.RemoveFolderWithContents(folder.UniqueId);
                else
                    _basePreset.RemoveFolder(folder.UniqueId);
                Service.Save();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
            ImGui.SetTooltip(UIStrings.HoldShiftToDelete);
        }
    }

    public static void DrawPresetContext(BasePresetConfig preset) {
        if (preset == null)
            return;

        using var ctx = ImRaii.ContextPopupItem(@$"PresetOptions###{preset.PresetName}");
        if (!ctx.Success) return;

        var alreadySelected = _basePreset.SelectedPreset?.PresetName == preset.PresetName;
        if (ImGui.Selectable(!alreadySelected ? UIStrings.SetActive : UIStrings.Deselect)) {
            _basePreset.SelectedPreset = alreadySelected ? null : (CustomPresetConfig)preset;
            Service.Save();
        }

        if (ImGui.Selectable(UIStrings.Rename, false, ImGuiSelectableFlags.DontClosePopups)) {
            ImGui.OpenPopup(@$"PresetRenameName");
        }

        if (ImGui.Selectable(UIStrings.MakeACopy, false)) {
            CopyPreset(preset);
        }

        DrawUtil.DrawRenamePreset(preset);

        if (ImGui.Selectable(UIStrings.ExportPresetToClipboard, false)) {
            ImGui.SetClipboardText(Configuration.ExportPreset(preset));
            Notify.Success(UIStrings.PresetExportedToTheClipboard);
        }

        using (var disabled = ImRaii.Disabled(!ImGui.GetIO().KeyShift)) {
            if (ImGui.Selectable(UIStrings.Delete, false, ImGuiSelectableFlags.DontClosePopups)) {
                _basePreset.RemovePreset(preset.UniqueId);
                displayed = null;
                Service.Save();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(UIStrings.HoldShiftToDelete);
    }

    private static void CopyPreset(BasePresetConfig preset) {
        var json = JsonConvert.SerializeObject(preset);
        var copy = JsonConvert.DeserializeObject<CustomPresetConfig>(json);
        copy!.UniqueId = Guid.NewGuid();
        copy.PresetName = $"Copy_{preset.PresetName}";
        _basePreset.AddNewPreset(copy);
        Service.Save();
    }
}
