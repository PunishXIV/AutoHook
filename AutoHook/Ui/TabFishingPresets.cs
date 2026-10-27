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

    private static readonly FishingPresets _basePreset = Configuration.C.HookPresets;

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
            IPluginLog.Get().Error(e, "[TabFishingPresets] Draw failed.");
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
        Configuration.EditingPreset = displayed as CustomPresetConfig;
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
        var presetById = searchActive ? _basePreset.CustomPresets.ToDictionary(p => p.UniqueId) : null;

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
                    return !IsAnonymousPreset(p) && MatchesSearch(p.PresetName);
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
                    Configuration.Save();
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
        DrawPresetRow(preset);
    }

    private void DrawPresetRow(CustomPresetConfig preset, Action? drawDragDrop = null, string? label = null) {
        using var id = ImRaii.PushId(preset.UniqueId.ToString());
        var selected = _basePreset.SelectedGuid == preset.UniqueId.ToString();
        var color = selected ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudWhite;
        using (ImRaii.PushColor(ImGuiCol.Text, color)) {
            if (ImGui.Selectable((selected ? "> " : "") + (label ?? preset.PresetName),
                    displayed?.UniqueId == preset.UniqueId,
                    ImGuiSelectableFlags.AllowDoubleClick)) {
                displayed = preset;

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
                    _basePreset.SelectedPreset = selected ? null : preset;
                    Configuration.Save();
                }
            }
        }

        drawDragDrop?.Invoke();
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

    private List<Guid> GetAllPresetIdsInFolderTree(PresetFolder rootFolder) {
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

    private bool FolderContainsSelectedPreset(PresetFolder folder) {
        var selected = _basePreset.SelectedPreset;
        if (selected == null)
            return false;

        if (folder.PresetIds.Contains(selected.UniqueId))
            return true;

        var containingFolder = _basePreset.Folders.FirstOrDefault(f => f.PresetIds.Contains(selected.UniqueId));
        return containingFolder != null && IsFolderDescendantOf(folder, containingFolder);
    }

    private int GetFolderImmediateItemCount(PresetFolder folder) {
        var directChildFolderCount = _basePreset.Folders.Count(f => f.ParentFolderId == folder.UniqueId);
        return folder.PresetIds.Count + directChildFolderCount;
    }

    private void DrawFolder(PresetFolder folder, int folderIndex) {
        bool isOpen;
        using (var id = ImRaii.PushId($"folder_{folder.UniqueId}")) {
            var icon = folder.IsExpanded ? FontAwesomeIcon.FolderOpen : FontAwesomeIcon.Folder;

            var containsSelectedPreset = FolderContainsSelectedPreset(folder);
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange, containsSelectedPreset)) {
                // Display folder name with item count
                var displayName = $"{folder.FolderName} ({GetFolderImmediateItemCount(folder)})";

                // Draw folder with tree node
                isOpen = ImGui.TreeNodeEx(displayName,
                    ImGuiTreeNodeFlags.AllowItemOverlap |
                    ImGuiTreeNodeFlags.SpanAvailWidth |
                    (folder.IsExpanded ? ImGuiTreeNodeFlags.DefaultOpen : 0));
            }

            // Handle drag and drop onto folder (presets + folder reparent/reorder)
            AcceptDropsOntoFolder(folder, folderIndex);

            if (ImGui.BeginDragDropSource()) {
                ImGuiDragDrop.SetDragDropPayload("FOLDER_ORDER", folderIndex);
                ImGui.Text($"{UIStrings.MovingFolder_} {folder.FolderName}");
                ImGui.EndDragDropSource();
            }

            // Right click for context menu
            DrawFolderContextMenu(folder);

            // Update folder expand state
            if (isOpen != folder.IsExpanded) {
                folder.IsExpanded = isOpen;
                Configuration.Save();
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
        ImGui.Indent(10.Scaled());
        DrawPresetRow(preset, () => DrawFolderPresetDragDrop(preset, folder));
        ImGui.Unindent(10.Scaled());
    }

    private void DrawItem(CustomPresetConfig preset, int i) {
        DrawPresetRow(preset, () => DrawRootPresetDragDrop(preset, i));
    }

    private void AcceptDropsOntoFolder(PresetFolder folder, int folderIndex) {
        if (!ImGui.BeginDragDropTarget())
            return;

        if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_ORDER", out int itemIndex) && ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
            folder.AddPreset(_basePreset.CustomPresets[itemIndex].UniqueId);
            Configuration.Save();
        }

        if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_IN_FOLDER", out Guid presetId) && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            AcceptPresetMoveIntoFolder(presetId, folder);

        if (ImGuiDragDrop.AcceptDragDropPayload("FOLDER_ORDER", out int sourceFolderIndex) && ImGui.IsMouseReleased(ImGuiMouseButton.Left) && sourceFolderIndex != folderIndex) {
            AcceptFolderOntoFolder(sourceFolderIndex, folder);
        }

        ImGui.EndDragDropTarget();
    }

    private void AcceptPresetMoveIntoFolder(Guid presetId, PresetFolder folder) {
        var sourceFolder = _basePreset.Folders.FirstOrDefault(f => f.PresetIds.Contains(presetId));
        if (sourceFolder != null && sourceFolder.UniqueId != folder.UniqueId) {
            var sourcePresetIds = new List<Guid>(sourceFolder.PresetIds);
            sourcePresetIds.Remove(presetId);
            sourceFolder.PresetIds = sourcePresetIds;

            if (!folder.PresetIds.Contains(presetId))
                folder.AddPreset(presetId);

            Configuration.Save();
        }
        else if (sourceFolder == null) {
            folder.AddPreset(presetId);
            Configuration.Save();
        }
    }

    private void AcceptFolderOntoFolder(int sourceFolderIndex, PresetFolder folder) {
        if (sourceFolderIndex < 0 || sourceFolderIndex >= _basePreset.Folders.Count)
            return;

        var movingFolder = _basePreset.Folders[sourceFolderIndex];

        if (movingFolder.ParentFolderId == folder.ParentFolderId) {
            _basePreset.Folders.RemoveAt(sourceFolderIndex);
            var targetIndex = _basePreset.Folders.IndexOf(folder);
            if (targetIndex < 0)
                _basePreset.Folders.Add(movingFolder);
            else
                _basePreset.Folders.Insert(targetIndex, movingFolder);
            Configuration.Save();
            return;
        }

        if (folder.ParentFolderId == movingFolder.UniqueId) {
            var oldParent = movingFolder.ParentFolderId;
            movingFolder.ParentFolderId = folder.UniqueId;
            folder.ParentFolderId = oldParent;
            Configuration.Save();
            return;
        }

        if (!IsFolderDescendantOf(movingFolder, folder)) {
            movingFolder.ParentFolderId = folder.UniqueId;
            Configuration.Save();
        }
    }

    private void AcceptFolderReparentAtPresetLevel(CustomPresetConfig preset) {
        if (!ImGuiDragDrop.AcceptDragDropPayload("FOLDER_ORDER", out int sourceFolderIndex) || !ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            return;

        if (sourceFolderIndex < 0 || sourceFolderIndex >= _basePreset.Folders.Count)
            return;

        var movingFolder = _basePreset.Folders[sourceFolderIndex];
        var targetFolder = _basePreset.GetFolderContainingPreset(preset.UniqueId);

        if (targetFolder != null && (targetFolder.UniqueId == movingFolder.UniqueId || IsFolderDescendantOf(movingFolder, targetFolder)))
            return;

        movingFolder.ParentFolderId = targetFolder?.UniqueId;
        Configuration.Save();
    }

    private void DrawRootPresetDragDrop(CustomPresetConfig preset, int i) {
        if (ImGui.BeginDragDropSource()) {
            ImGuiDragDrop.SetDragDropPayload("PRESET_ORDER", i);
            ImGui.Text($"{UIStrings.Moving_} {preset.PresetName}");
            ImGui.EndDragDropSource();
        }

        if (!ImGui.BeginDragDropTarget())
            return;

        if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_ORDER", out int itemIndex) && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            _basePreset.SwapIndex(itemIndex, i);

        if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_IN_FOLDER", out Guid presetId) && ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
            foreach (var folder in _basePreset.Folders)
                folder.RemovePreset(presetId);

            var draggedPreset = _basePreset.CustomPresets.FirstOrDefault(p => p.UniqueId == presetId);
            if (draggedPreset != null) {
                var draggedIndex = _basePreset.CustomPresets.IndexOf(draggedPreset);
                if (draggedIndex >= 0)
                    _basePreset.SwapIndex(draggedIndex, i);
            }

            Configuration.Save();
        }

        AcceptFolderReparentAtPresetLevel(preset);
        ImGui.EndDragDropTarget();
    }

    private void DrawFolderPresetDragDrop(CustomPresetConfig preset, PresetFolder folder) {
        if (ImGui.BeginDragDropSource()) {
            ImGuiDragDrop.SetDragDropPayload("PRESET_IN_FOLDER", preset.UniqueId);
            ImGui.Text($"{UIStrings.Moving_} {preset.PresetName}");
            ImGui.EndDragDropSource();
        }

        if (!ImGui.BeginDragDropTarget())
            return;

        if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_IN_FOLDER", out Guid presetId) && ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
            try {
                var targetIndex = folder.PresetIds.IndexOf(preset.UniqueId);
                if (targetIndex >= 0) {
                    var newPresetIds = new List<Guid>(folder.PresetIds);
                    var currentIndex = newPresetIds.IndexOf(presetId);
                    if (currentIndex >= 0) {
                        newPresetIds.RemoveAt(currentIndex);
                        newPresetIds.Insert(targetIndex, presetId);
                        folder.PresetIds = newPresetIds;
                        Configuration.Save();
                    }
                }
            }
            catch (Exception ex) {
                IPluginLog.Get().Error(ex, "[TabFishingPresets] Error reordering presets.");
            }
        }

        AcceptFolderReparentAtPresetLevel(preset);
        ImGui.EndDragDropTarget();
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

        using (ImRaii.Disabled(_basePreset.SelectedPreset == null)) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileExport)) {
                ImGui.SetClipboardText(Configuration.ExportPreset(_basePreset.SelectedPreset!));
                Notify.Success(UIStrings.PresetExportedToTheClipboard);
            }
        }
        ImGui.TooltipOnHover(UIStrings.ExportPresetToClipboard);

        ImGui.SameLine(0, 3.Scaled());
        DrawCombinedImport();
        ImGui.SameLine(0, 3.Scaled());
        DrawUtil.DrawDeletePresetButton(_basePreset);

        if (displayed != null && displayed != _basePreset.DefaultPreset && _basePreset.GetPreset(displayed.UniqueId) == null) {
            displayed = _basePreset.SelectedPreset ?? _basePreset.DefaultPreset;
        }
    }

    private void ClearFolderImportSelectionState() {
        _selectedPresetsForImport.Clear();
        _presetImportNames.Clear();
        _renamePresetId = null;
    }

    private void ClearFolderImportState() {
        _tempImportFolder = null;
        ClearFolderImportSelectionState();
    }

    private void EnsureFolderImportSelectionState(IReadOnlyList<CustomPresetConfig> presets) {
        // ImportFolder regens GUIDs so if you rebuild only on count then you get stale keys on a re-import
        var needsInit = _selectedPresetsForImport.Count != presets.Count || presets.Any(p => !_selectedPresetsForImport.ContainsKey(p.UniqueId));
        if (!needsInit)
            return;

        _selectedPresetsForImport = [];
        _presetImportNames = [];
        foreach (var preset in presets) {
            _selectedPresetsForImport[preset.UniqueId] = true;
            _presetImportNames[preset.UniqueId] = preset.PresetName;
        }
    }

    private void DrawImportPresetRow(CustomPresetConfig preset) {
        using var presetId = ImRaii.PushId(preset.UniqueId.ToString());

        if (!_selectedPresetsForImport.TryGetValue(preset.UniqueId, out var isSelected))
            isSelected = true;

        if (ImGui.Checkbox("##selectPreset", ref isSelected))
            _selectedPresetsForImport[preset.UniqueId] = isSelected;

        ImGui.SameLine();

        if (_renamePresetId == preset.UniqueId) {
            ImGui.SetNextItemWidth(200.Scaled());
            if (ImGui.InputText("##renameField", ref _tempImportName, 100,
                    ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll)) {
                _presetImportNames[preset.UniqueId] = _tempImportName;
                _renamePresetId = null;
            }

            if (!ImGui.IsItemActive() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                _presetImportNames[preset.UniqueId] = _tempImportName;
                _renamePresetId = null;
            }
        }
        else {
            var displayName = _presetImportNames.TryGetValue(preset.UniqueId, out var n) ? n : preset.PresetName;
            ImGui.Text(displayName);

            ImGui.SameLine();

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Edit)) {
                _renamePresetId = preset.UniqueId;
                _tempImportName = displayName;
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(UIStrings.RenamePreset);
        }
    }

    private void DrawImportFolderNode(PresetFolder folder, List<PresetFolder> allFolders, List<CustomPresetConfig> allPresets) {
        var children = allFolders.Where(f => f.ParentFolderId == folder.UniqueId).ToList();
        var directPresets = allPresets.Where(p => folder.PresetIds.Contains(p.UniqueId)).ToList();
        var flags = ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.FramePadding;

        using var tree = ImRaii.TreeNode($"{folder.FolderName} ({directPresets.Count + children.Count})###import-folder-{folder.UniqueId}", flags);
        if (!tree)
            return;

        foreach (var child in children)
            DrawImportFolderNode(child, allFolders, allPresets);

        foreach (var preset in directPresets)
            DrawImportPresetRow(preset);
    }

    private void DrawImportFolderTree(PresetFolder root, List<PresetFolder> allFolders, List<CustomPresetConfig> allPresets) {
        var children = allFolders.Where(f => f.ParentFolderId == root.UniqueId).ToList();
        var directPresets = allPresets.Where(p => root.PresetIds.Contains(p.UniqueId)).ToList();

        if (children.Count == 0) {
            foreach (var preset in directPresets)
                DrawImportPresetRow(preset);
            return;
        }

        foreach (var child in children)
            DrawImportFolderNode(child, allFolders, allPresets);

        foreach (var preset in directPresets)
            DrawImportPresetRow(preset);
    }

    private void DrawCombinedImport() {
        try {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport)) {
                var clipboardText = ImGui.GetClipboardText();
                ClearFolderImportSelectionState();
                _tempImportPreset = null;

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

            ImGui.SetNextWindowSize(new Vector2(520.Scaled(), 460.Scaled()), ImGuiCond.FirstUseEver);
            using var popup = ImRaii.Popup("import_new_preset");

            if (popup.Success) {
                if (_isImportingFolder && _tempImportFolder.HasValue) {
                    // Handle folder import
                    var folder = _tempImportFolder.Value.Folder;
                    var folders = _tempImportFolder.Value.Folders;
                    var presets = _tempImportFolder.Value.Presets;
                    var name = folder.FolderName;
                    var childFolderCount = folders.Count(f => f.ParentFolderId == folder.UniqueId);

                    ImGui.TextWrapped(UIStrings.ImportFolderAndPresets);

                    if (ImGui.InputText(UIStrings.FolderName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
                        folder.FolderName = name;

                    ImGui.TextDisabled($"{presets.Count} presets · {folders.Count} folders" + (childFolderCount > 0 ? $" · {childFolderCount} subfolders" : string.Empty));

                    EnsureFolderImportSelectionState(presets);

                    var treeHeight = Math.Max(180.Scaled(), ImGui.GetContentRegionAvail().Y - 52.Scaled());
                    using (var treeChild = ImRaii.Child("##import_folder_tree", new Vector2(0, treeHeight), true)) {
                        if (treeChild)
                            DrawImportFolderTree(folder, folders, presets);
                    }

                    ImGui.Separator();

                    if (ImGui.Button(UIStrings.Import, new Vector2(120.Scaled(), 0))) {
                        var selectedPresetIds = presets
                            .Where(p => _selectedPresetsForImport.TryGetValue(p.UniqueId, out var selected) && selected)
                            .Select(p => p.UniqueId)
                            .ToHashSet();

                        if (selectedPresetIds.Count == 0) {
                            Notify.Error(UIStrings.NoPresetsSelected);
                            return;
                        }

                        foreach (var preset in presets) {
                            if (!selectedPresetIds.Contains(preset.UniqueId))
                                continue;

                            if (_presetImportNames.TryGetValue(preset.UniqueId, out var newName))
                                preset.PresetName = newName;
                        }

                        var result = PresetImport.ImportFolderTree(_basePreset, folder, folders, presets, new PresetImportOptions { SelectedPresetIds = selectedPresetIds });
                        Configuration.Save();
                        Notify.Success($"Folder imported with {result.ImportedPresets} presets");

                        ClearFolderImportState();
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.SameLine();

                    if (ImGui.Button(UIStrings.DrawImportExport_Cancel, new Vector2(120.Scaled(), 0))) {
                        ClearFolderImportState();
                        ImGui.CloseCurrentPopup();
                    }
                }
                else if (!_isImportingFolder && _tempImportPreset != null) {
                    if (DrawUtil.DrawPendingImportPreset(_tempImportPreset, p => {
                        if (p is CustomPresetConfig custom) {
                            PresetImport.ImportPresets(_basePreset, [custom], new PresetImportOptions { SelectFirst = true });
                        }
                        else {
                            _basePreset.AddNewPreset(p);
                        }
                    }, new Vector2(120.Scaled(), 0)))
                        _tempImportPreset = null;
                }
            }
        }
        catch (Exception e) {
            IPluginLog.Get().Error(e.ToString());
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
                        Configuration.Save();
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
            Configuration.Save();
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
                Configuration.Save();
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
            Configuration.Save();
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
                displayed = _basePreset.SelectedPreset ?? _basePreset.DefaultPreset;
                Configuration.Save();
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
        Configuration.Save();
    }
}
