using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using System.Numerics;

namespace AutoHook.Ui;

public class TabFishingPresets : BaseTab {
    public override bool Enabled => true;
    public override string TabName => UIStrings.FishingPresets;

    public override OpenWindow Type => OpenWindow.FishingPreset;

    private static readonly FishingPresets _basePreset = Configuration.C.HookPresets;

    private const string PresetPayload = "AH_PRESET";
    private const string FolderPayload = "AH_FOLDER";

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

    private static string GetAnonymousPresetDisplayName(string presetName)
        => presetName.StartsWith(CustomPresetConfig.AnonymousPresetPrefix, StringComparison.Ordinal) ? presetName[CustomPresetConfig.AnonymousPresetPrefix.Length..] : presetName;

    private bool SearchActive => !string.IsNullOrWhiteSpace(_searchFilter);

    private bool MatchesSearch(string name) => !SearchActive || name.Contains(_searchFilter.Trim(), StringComparison.InvariantCultureIgnoreCase);

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

        if (promptingForFolderName)
            DrawCreateFolderPopup();

        if (renameFolderId != null)
            DrawRenameFolderPopup();

        using var list = ImRaii.ListBox("preset_list", ImGui.GetContentRegionAvail());
        if (!list)
            return;

        DrawGlobalPresetItem();

        var anonPresets = _basePreset.CustomPresets.Where(p => p.IsAnonymous && (MatchesSearch(p.PresetName) || MatchesSearch(GetAnonymousPresetDisplayName(p.PresetName)))).ToList();
        if (anonPresets.Count > 0)
            DrawAnonymousPresetsSection(anonPresets);

        ImGui.Separator();

        // top-level folders; children are drawn inside their parents
        var presetById = SearchActive ? _basePreset.CustomPresets.ToDictionary(p => p.UniqueId) : null;
        for (var i = 0; i < _basePreset.Folders.Count; i++) {
            var folder = _basePreset.Folders[i];
            if (folder.ParentFolderId.HasValue)
                continue;
            if (presetById != null && !MatchesSearch(folder.FolderName)
                && !_basePreset.PresetIdsInTree(folder).Any(id => presetById.TryGetValue(id, out var p) && !p.IsAnonymous && MatchesSearch(p.PresetName)))
                continue;

            DrawFolder(folder);
        }

        var presetsInFolders = _basePreset.Folders.SelectMany(f => f.PresetIds).ToHashSet();
        var customPresets = _basePreset.CustomPresets;
        for (var i = 0; i < customPresets.Count; i++) {
            var preset = customPresets[i];
            if (!presetsInFolders.Contains(preset.UniqueId) && !preset.IsAnonymous && MatchesSearch(preset.PresetName))
                DrawPresetRow(preset, draggable: true);
        }
    }

    private void DrawGlobalPresetItem() {
        if (!MatchesSearch(UIStrings.GlobalPreset))
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

    private void DrawAnonymousPresetsSection(List<CustomPresetConfig> anonPresets) {
        var headerFlags = ImGuiTreeNodeFlags.None;
        if (SearchActive || anonPresets.Any(p => _basePreset.SelectedGuid == p.UniqueId.ToString()))
            headerFlags = ImGuiTreeNodeFlags.DefaultOpen;

        if (!ImGui.CollapsingHeader(string.Format(UIStrings.AnonymousPresets_Header, anonPresets.Count), headerFlags))
            return;

        using var indent = ImRaii.PushIndent(10.Scaled());
        foreach (var preset in anonPresets)
            DrawPresetRow(preset, draggable: false);
    }

    private void DrawPresetRow(CustomPresetConfig preset, bool draggable) {
        using var id = ImRaii.PushId(preset.UniqueId.ToString());
        var selected = _basePreset.SelectedGuid == preset.UniqueId.ToString();
        var color = selected ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudWhite;
        using (ImRaii.PushColor(ImGuiCol.Text, color)) {
            if (ImGui.Selectable((selected ? "> " : "") + preset.PresetName,
                    displayed?.UniqueId == preset.UniqueId,
                    ImGuiSelectableFlags.AllowDoubleClick)) {
                displayed = preset;

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
                    _basePreset.SelectedPreset = selected ? null : preset;
                    Configuration.Save();
                }
            }
        }

        if (draggable) {
            DragSource(PresetPayload, preset.UniqueId, $"{UIStrings.Moving_} {preset.PresetName}");
            if (ImGui.BeginDragDropTarget()) {
                if (AcceptDrop(PresetPayload, out var presetId))
                    _basePreset.MovePresetTo(presetId, preset);
                if (AcceptDrop(FolderPayload, out var folderId) && _basePreset.FindFolder(folderId) is { } moving)
                    _basePreset.MoveFolderBeside(moving, preset);
                ImGui.EndDragDropTarget();
            }
        }

        ImGui.TooltipOnHover(UIStrings.RightClickOptions);
        DrawPresetContext(preset);
    }

    private static void DragSource(string type, Guid id, string label) {
        if (!ImGui.BeginDragDropSource())
            return;
        ImGuiDragDrop.SetDragDropPayload(type, id);
        ImGui.Text(label);
        ImGui.EndDragDropSource();
    }

    private static bool AcceptDrop(string type, out Guid id)
        => ImGuiDragDrop.AcceptDragDropPayload(type, out id) && ImGui.IsMouseReleased(ImGuiMouseButton.Left);

    private bool FolderContainsSelectedPreset(PresetFolder folder) {
        if (_basePreset.SelectedPreset is not { } selected || _basePreset.GetFolderContainingPreset(selected.UniqueId) is not { } containing)
            return false;
        return containing == folder || _basePreset.IsDescendantOf(folder, containing);
    }

    private void DrawFolder(PresetFolder folder) {
        bool isOpen;
        using (var id = ImRaii.PushId($"folder_{folder.UniqueId}")) {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange, FolderContainsSelectedPreset(folder))) {
                var itemCount = folder.PresetIds.Count + _basePreset.ChildFolders(folder).Count();
                isOpen = ImGui.TreeNodeEx($"{folder.FolderName} ({itemCount})",
                    ImGuiTreeNodeFlags.AllowItemOverlap |
                    ImGuiTreeNodeFlags.SpanAvailWidth |
                    (folder.IsExpanded ? ImGuiTreeNodeFlags.DefaultOpen : 0));
            }

            if (ImGui.BeginDragDropTarget()) {
                if (AcceptDrop(PresetPayload, out var presetId))
                    _basePreset.MovePresetToFolder(presetId, folder);
                if (AcceptDrop(FolderPayload, out var folderId) && _basePreset.FindFolder(folderId) is { } moving)
                    _basePreset.MoveFolderOnto(moving, folder);
                ImGui.EndDragDropTarget();
            }

            DragSource(FolderPayload, folder.UniqueId, $"{UIStrings.MovingFolder_} {folder.FolderName}");
            DrawFolderContextMenu(folder);

            if (isOpen != folder.IsExpanded) {
                folder.IsExpanded = isOpen;
                Configuration.Save();
            }
        }

        if (!isOpen)
            return;

        foreach (var child in _basePreset.ChildFolders(folder).ToList())
            DrawFolder(child);

        using (ImRaii.PushIndent(10.Scaled())) {
            foreach (var presetId in folder.PresetIds) {
                if (_basePreset.CustomPresets.Find(p => p.UniqueId == presetId) is { IsAnonymous: false } preset && MatchesSearch(preset.PresetName))
                    DrawPresetRow(preset, draggable: true);
            }
        }

        ImGui.TreePop();
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

        var exportTarget = displayed == _basePreset.DefaultPreset ? null : displayed;
        using (ImRaii.Disabled(exportTarget == null)) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileExport) && exportTarget != null) {
                ImGui.SetClipboardText(Configuration.ExportPreset(exportTarget));
                Notify.Success(UIStrings.PresetExportedToTheClipboard);
            }
        }
        ImGui.TooltipOnHover(UIStrings.ExportPresetToClipboard);

        ImGui.SameLine(0, 3.Scaled());
        DrawCombinedImport();
        ImGui.SameLine(0, 3.Scaled());
        DrawUtil.DrawDeletePresetButton(_basePreset, displayed == _basePreset.DefaultPreset ? null : displayed);

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

    // root's contents are drawn flat; nested folders get a tree node each
    private void DrawImportFolderContents(PresetFolder folder, List<PresetFolder> allFolders, List<CustomPresetConfig> allPresets) {
        foreach (var child in allFolders.Where(f => f.ParentFolderId == folder.UniqueId).ToList()) {
            var childCount = child.PresetIds.Count + allFolders.Count(f => f.ParentFolderId == child.UniqueId);
            using var tree = ImRaii.TreeNode($"{child.FolderName} ({childCount})###import-folder-{child.UniqueId}",
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.FramePadding);
            if (tree)
                DrawImportFolderContents(child, allFolders, allPresets);
        }

        foreach (var preset in allPresets.Where(p => folder.PresetIds.Contains(p.UniqueId)).ToList())
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
                            DrawImportFolderContents(folder, folders, presets);
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
                    _basePreset.AddNewFolder(newFolderName, _parentFolderForNewFolder);
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
                    if (_basePreset.FindFolder(renameFolderId.Value) is { } folder) {
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

        if (ImGui.Selectable(UIStrings.MakeACopy, false))
            _basePreset.CopyFolder(folder);

        if (ImGui.Selectable(UIStrings.ExportFolderClipboard, false)) {
            var exportData = Configuration.ExportFolder(folder, _basePreset.CustomPresets, _basePreset.Folders);
            ImGui.SetClipboardText(exportData);
            Notify.Success(UIStrings.FolderExported);
        }

        using (ImRaii.Disabled(!ImGui.GetIO().KeyShift)) {
            if (ImGui.Selectable(UIStrings.Delete, false, ImGuiSelectableFlags.DontClosePopups))
                _basePreset.RemoveFolderWithContents(folder.UniqueId);
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(UIStrings.HoldShiftToDelete);
    }

    public static void DrawPresetContext(BasePresetConfig preset) {
        if (preset == null)
            return;

        var isActive = _basePreset.SelectedGuid == preset.UniqueId.ToString();
        DrawUtil.DrawPresetContextMenu(_basePreset, preset, isActive,
            toggleActive: () => _basePreset.SelectedPreset = isActive ? null : (CustomPresetConfig)preset,
            onDeleted: () => displayed = _basePreset.SelectedPreset ?? _basePreset.DefaultPreset);
    }
}
