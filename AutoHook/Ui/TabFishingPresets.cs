using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using Newtonsoft.Json;

namespace AutoHook.Ui;

public class TabFishingPresets : BaseTab
{
    public override bool Enabled => true;
    public override string TabName => UIStrings.FishingPresets;

    public override OpenWindow Type => OpenWindow.FishingPreset;

    private static readonly FishingPresets _basePreset = Service.Configuration.HookPresets;

    public static bool OpenPresetGen;
    private readonly PresetCreator PresetCreator = new();

    private string newFolderName = string.Empty;
    private bool promptingForFolderName = false;

    private string renameFolderName = string.Empty;
    private Guid? renameFolderId = null;

    private BasePresetConfig? _tempImportPreset = null;
    private (PresetFolder Folder, List<CustomPresetConfig> Presets)? _tempImportFolder = null;
    private string _tempImportName = string.Empty;
    private bool _isImportingFolder = false;

    private Dictionary<Guid, bool> _selectedPresetsForImport = [];
    private Dictionary<Guid, string> _presetImportNames = [];
    private Guid? _renamePresetId = null;

    private string _searchFilter = string.Empty;

    public override void DrawHeader()
    {
        DrawTabDescription(UIStrings.TabPresets_DrawHeader_NewTabDescription);

        if (OpenPresetGen)
            DrawPresetGenTab();
    }

    private void DrawPresetGenTab()
    {
        ImGui.PushID(@"PresetGen");
        ImGui.SetNextItemWidth(500);
        if (ImGui.Begin(UIStrings.PresetGen, ref OpenPresetGen, ImGuiWindowFlags.AlwaysUseWindowPadding))
            PresetCreator.DrawPresetGenerator();

        ImGui.End();
        ImGui.PopID();
    }

    public override void Draw()
    {
        try
        {
            DrawList();
        }
        catch (Exception e)
        {
            Svc.Log.Error(e.Message);
        }
    }

    private static BasePresetConfig? displayed = _basePreset.SelectedPreset ?? _basePreset.DefaultPreset;

    private void DrawList()
    {
        using (var table = ImRaii.Table($"###PresetTable", 2, ImGuiTableFlags.Resizable))
        {
            if (!table)
                return;

            ImGui.TableSetupColumn($"###OptionColumn", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableNextColumn();
            using (var left = ImRaii.Child($"###OptionSide"))
                DrawPresetOptions(displayed);

            ImGui.TableSetupColumn($"###PresetColumn", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableNextColumn();
            using (var right = ImRaii.Child($"###PresetSide"))
            {
                DrawPresetButtons();

                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                ImGui.InputTextWithHint("##PresetSearch", UIStrings.Search_Hint, ref _searchFilter, 128);

                if (promptingForFolderName)
                {
                    DrawCreateFolderPopup();
                }

                if (renameFolderId != null)
                {
                    DrawRenameFolderPopup();
                }

                using var list = ImRaii.ListBox("preset_list", ImGui.GetContentRegionAvail());
                if (!list)
                    return;

                var searchActive = !string.IsNullOrWhiteSpace(_searchFilter);
                bool MatchesSearch(string name) => !searchActive || name.Contains(_searchFilter.Trim().ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase);

                DrawUtil.Info(UIStrings.GlobalPresetHelpText);
                ImGui.SameLine(0, 4);
                if ((!searchActive || MatchesSearch(UIStrings.GlobalPreset)) && ImGui.Selectable(UIStrings.GlobalPreset, displayed?.PresetName == _basePreset.DefaultPreset.PresetName, ImGuiSelectableFlags.AllowDoubleClick))
                    displayed = _basePreset.DefaultPreset;

                ImGui.Separator();

                // Draw folders
                for (var folderIndex = 0; folderIndex < _basePreset.Folders.Count; folderIndex++)
                {
                    var folder = _basePreset.Folders[folderIndex];
                    if (searchActive)
                    {
                        var folderNameMatches = MatchesSearch(folder.FolderName);
                        var anyPresetMatches = folder.PresetIds.Any(id =>
                        {
                            var p = _basePreset.CustomPresets.FirstOrDefault(c => c.UniqueId == id);
                            return p != null && MatchesSearch(p.PresetName);
                        });
                        if (!folderNameMatches && !anyPresetMatches)
                            continue;
                    }

                    DrawFolder(folder, folderIndex);
                }

                // Draw non-folder presets
                for (var i = 0; i < _basePreset.PresetList.Count; i++)
                {
                    var preset = _basePreset.PresetList[i];

                    // Skip presets that are inside a folder
                    if (_basePreset.IsPresetInAnyFolder(preset.UniqueId))
                        continue;

                    if (searchActive && !MatchesSearch(preset.PresetName))
                        continue;

                    if (preset is CustomPresetConfig customPreset)
                        DrawItem(customPreset, i);
                }
            }
        }
    }

    private void DrawFolder(PresetFolder folder, int folderIndex)
    {
        bool isOpen;
        using (var id = ImRaii.PushId($"folder_{folder.UniqueId}"))
        {
            var icon = folder.IsExpanded ? FontAwesomeIcon.FolderOpen : FontAwesomeIcon.Folder;

            // Check if this folder contains the selected preset
            bool containsSelectedPreset = false;
            if (_basePreset.SelectedPreset != null)
            {
                containsSelectedPreset = folder.PresetIds.Contains(_basePreset.SelectedPreset.UniqueId);
            }

            // Use orange color for folders containing the selected preset
            if (containsSelectedPreset)
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);

            // Display folder name with item count
            string displayName = $"{folder.FolderName} ({folder.PresetIds.Count})";

            // Draw folder with tree node
            isOpen = ImGui.TreeNodeEx(displayName,
                ImGuiTreeNodeFlags.AllowItemOverlap |
                ImGuiTreeNodeFlags.SpanAvailWidth |
                (folder.IsExpanded ? ImGuiTreeNodeFlags.DefaultOpen : 0));

            if (containsSelectedPreset)
                ImGui.PopStyleColor();

            // Handle drag and drop onto folder
            if (ImGui.BeginDragDropTarget())
            {
                // Accept preset drops from outside folders
                if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_ORDER", out int itemIndex))
                {
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        var preset = _basePreset.PresetList[itemIndex];
                        folder.AddPreset(preset.UniqueId);
                        Service.Save();
                    }
                }

                // Accept preset drops from inside folders
                if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_IN_FOLDER", out Guid presetId))
                {
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        // First, find which folder this preset is coming from
                        PresetFolder? sourceFolder = null;
                        foreach (var otherFolder in _basePreset.Folders)
                        {
                            if (otherFolder.PresetIds.Contains(presetId))
                            {
                                sourceFolder = otherFolder;
                                break;
                            }
                        }

                        // Now handle the move
                        if (sourceFolder != null && sourceFolder.UniqueId != folder.UniqueId)
                        {
                            // Remove from source folder
                            var sourcePresetIds = new List<Guid>(sourceFolder.PresetIds);
                            sourcePresetIds.Remove(presetId);
                            sourceFolder.PresetIds = sourcePresetIds;

                            // Add to target folder if not already there
                            if (!folder.PresetIds.Contains(presetId))
                            {
                                folder.AddPreset(presetId);
                            }

                            Service.Save();
                        }
                        else if (sourceFolder == null)
                        {
                            // If not found in any folder (shouldn't happen, but just in case)
                            folder.AddPreset(presetId);
                            Service.Save();
                        }
                    }
                }

                ImGui.EndDragDropTarget();
            }

            // Folder drag source
            if (ImGui.BeginDragDropSource())
            {
                ImGuiDragDrop.SetDragDropPayload("FOLDER_ORDER", folderIndex);
                ImGui.Text($"{UIStrings.MovingFolder_} {folder.FolderName}");

                ImGui.EndDragDropSource();
            }

            // Handle folder reordering
            if (ImGui.BeginDragDropTarget())
            {
                if (ImGuiDragDrop.AcceptDragDropPayload("FOLDER_ORDER", out int sourceFolderIndex))
                {
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && sourceFolderIndex != folderIndex)
                    {
                        // Swap folders
                        var temp = _basePreset.Folders[sourceFolderIndex];
                        _basePreset.Folders.RemoveAt(sourceFolderIndex);
                        _basePreset.Folders.Insert(folderIndex, temp);
                        Service.Save();
                    }
                }

                ImGui.EndDragDropTarget();
            }

            // Right click for context menu
            DrawFolderContextMenu(folder);

            // Update folder expand state
            if (isOpen != folder.IsExpanded)
            {
                folder.IsExpanded = isOpen;
                Service.Save();
            }
        }

        // Draw folder contents if expanded
        if (isOpen)
        {
            foreach (var presetId in folder.PresetIds)
            {
                var preset = _basePreset.CustomPresets.FirstOrDefault(p => p.UniqueId == presetId);
                if (preset != null)
                {
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

    private void DrawItemInFolder(CustomPresetConfig preset, int i, PresetFolder folder)
    {
        using var id = ImRaii.PushId(preset.UniqueId.ToString());
        var selected = _basePreset.SelectedGuid == preset.UniqueId.ToString();
        var color = selected ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudWhite;

        // Indent to show hierarchy
        ImGui.Indent(10);

        using (var a = ImRaii.PushColor(ImGuiCol.Text, color))
        {
            if (ImGui.Selectable((selected ? "> " : "") + preset.PresetName,
                    displayed?.UniqueId == preset.UniqueId,
                    ImGuiSelectableFlags.AllowDoubleClick))
            {
                displayed = preset;

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _basePreset.SelectedPreset = selected ? null : preset;
                    Service.Save();
                }
            }
        }

        ImGui.Unindent(10);

        if (ImGui.BeginDragDropSource())
        {
            // Use a different drag type to identify presets from folders
            ImGuiDragDrop.SetDragDropPayload("PRESET_IN_FOLDER", preset.UniqueId);
            ImGui.Text($"{UIStrings.Moving_} {preset.PresetName}");
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_IN_FOLDER", out Guid presetId))
            {
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    try
                    {
                        // Find where to place in the folder
                        int targetIndex = folder.PresetIds.IndexOf(preset.UniqueId);
                        if (targetIndex >= 0)
                        {
                            // Create a new list to avoid modifying the collection during enumeration
                            var newPresetIds = new List<Guid>(folder.PresetIds);

                            // Find the current index of the preset being moved
                            int currentIndex = newPresetIds.IndexOf(presetId);

                            // Only reorder if the preset is in this folder
                            if (currentIndex >= 0)
                            {
                                // Remove from current position and insert at target position
                                newPresetIds.RemoveAt(currentIndex);
                                newPresetIds.Insert(targetIndex, presetId);

                                // Replace the folder's preset list with our reordered one
                                folder.PresetIds = newPresetIds;
                                Service.Save();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Svc.Log.Error($"Error reordering presets: {ex.Message}");
                    }
                }
            }

            ImGui.EndDragDropTarget();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(UIStrings.RightClickOptions);

        DrawPresetContext(preset);
    }

    private void DrawItem(CustomPresetConfig preset, int i)
    {
        using var id = ImRaii.PushId(preset.UniqueId.ToString());
        var selected = _basePreset.SelectedGuid == preset.UniqueId.ToString();
        var color = selected ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudWhite;
        using (var a = ImRaii.PushColor(ImGuiCol.Text, color))
        {
            if (ImGui.Selectable((selected ? "> " : "") + preset.PresetName,
                    displayed?.UniqueId == preset.UniqueId,
                    ImGuiSelectableFlags.AllowDoubleClick))
            {
                displayed = preset;

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _basePreset.SelectedPreset = selected ? null : preset;
                    Service.Save();
                }
            }
        }

        if (ImGui.BeginDragDropSource())
        {
            ImGuiDragDrop.SetDragDropPayload("PRESET_ORDER", i);
            ImGui.Text($"{UIStrings.Moving_} {preset.PresetName}");
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_ORDER", out int itemIndex))
            {
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    _basePreset.SwapIndex(itemIndex, i);
                }
            }

            // Handle dropping from folders
            if (ImGuiDragDrop.AcceptDragDropPayload("PRESET_IN_FOLDER", out Guid presetId))
            {
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    // Remove from any folder
                    foreach (var folder in _basePreset.Folders)
                    {
                        folder.RemovePreset(presetId);
                    }

                    // Reorder in the main list if needed
                    var draggedPreset = _basePreset.CustomPresets.FirstOrDefault(p => p.UniqueId == presetId);
                    var targetPreset = _basePreset.CustomPresets[i];
                    if (draggedPreset != null && targetPreset != null)
                    {
                        int draggedIndex = _basePreset.CustomPresets.IndexOf(draggedPreset);
                        if (draggedIndex >= 0)
                        {
                            _basePreset.SwapIndex(draggedIndex, i);
                        }
                    }

                    Service.Save();
                }
            }

            ImGui.EndDragDropTarget();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(UIStrings.RightClickOptions);

        DrawPresetContext(preset);
    }

    private void DrawPresetOptions(BasePresetConfig? preset)
    {
        if (preset == null)
            return;

        using var id = ImRaii.PushId("TabBarsPreset");

        preset.DrawOptions();
    }

    private void DrawPresetButtons()
    {
        if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowsSpin))
            OpenPresetGen = !OpenPresetGen;

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(UIStrings.PresetGenerator);

        ImGui.SameLine(0, 3);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.FolderPlus))
            promptingForFolderName = true;

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(UIStrings.CreateFolder);

        ImGui.SameLine(0, 3);
        DrawUtil.DrawAddNewPresetButton(_basePreset);
        ImGui.SameLine(0, 3);
        DrawCombinedImport();
    }

    private void DrawCombinedImport()
    {
        try
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport))
            {
                var clipboardText = ImGui.GetClipboardText();

                // Try folder import first
                _tempImportFolder = Configuration.ImportFolder(clipboardText);
                if (_tempImportFolder.HasValue)
                {
                    _isImportingFolder = true;
                    ImGui.OpenPopup("import_new_preset");
                }
                else
                {
                    // Try preset import
                    _tempImportPreset = Configuration.ImportPreset(clipboardText);
                    if (_tempImportPreset != null)
                    {
                        _isImportingFolder = false;
                        ImGui.OpenPopup("import_new_preset");
                    }
                    else
                    {
                        Notify.Error("Invalid import data");
                    }
                }
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(UIStrings.ImportPresetOrFolder);

            using var popup = ImRaii.Popup("import_new_preset");

            if (popup.Success)
            {
                if (_isImportingFolder && _tempImportFolder.HasValue)
                {
                    // Handle folder import
                    var folder = _tempImportFolder.Value.Folder;
                    var name = folder.FolderName;

                    ImGui.TextWrapped(UIStrings.ImportFolderAndPresets);

                    if (ImGui.InputText(UIStrings.FolderName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
                        folder.FolderName = name;

                    // List of presets with checkboxes using TreeNodeEx
                    if (ImGui.TreeNodeEx($"{UIStrings.Presets_} {_tempImportFolder.Value.Presets.Count}", ImGuiTreeNodeFlags.DefaultOpen))
                    {
                        // Initialize selection states if not done yet
                        if (_selectedPresetsForImport == null || _selectedPresetsForImport.Count != _tempImportFolder.Value.Presets.Count)
                        {
                            _selectedPresetsForImport = [];
                            _presetImportNames = [];

                            foreach (var preset in _tempImportFolder.Value.Presets)
                            {
                                _selectedPresetsForImport[preset.UniqueId] = true; // Selected by default
                                _presetImportNames[preset.UniqueId] = preset.PresetName;
                            }
                        }

                        ImGui.Indent(10);

                        foreach (var preset in _tempImportFolder.Value.Presets)
                        {
                            ImGui.PushID(preset.UniqueId.ToString());

                            // Checkbox for selection
                            bool isSelected = _selectedPresetsForImport[preset.UniqueId];
                            if (ImGui.Checkbox("##selectPreset", ref isSelected))
                            {
                                _selectedPresetsForImport[preset.UniqueId] = isSelected;
                            }

                            ImGui.SameLine();

                            // Check if this preset is being renamed
                            if (_renamePresetId == preset.UniqueId)
                            {
                                // Show input field for renaming
                                ImGui.SetNextItemWidth(200);
                                if (ImGui.InputText("##renameField", ref _tempImportName, 100,
                                    ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
                                {
                                    // Apply rename on Enter
                                    _presetImportNames[preset.UniqueId] = _tempImportName;
                                    _renamePresetId = null;
                                }

                                // Also handle focus loss or clicking elsewhere
                                if (!ImGui.IsItemActive() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                                {
                                    _presetImportNames[preset.UniqueId] = _tempImportName;
                                    _renamePresetId = null;
                                }
                            }
                            else
                            {
                                // Normal display of preset name
                                ImGui.Text(_presetImportNames[preset.UniqueId]);

                                ImGui.SameLine();

                                // Edit button
                                if (ImGuiComponents.IconButton(FontAwesomeIcon.Edit))
                                {
                                    _renamePresetId = preset.UniqueId;
                                    _tempImportName = _presetImportNames[preset.UniqueId];
                                }

                                if (ImGui.IsItemHovered())
                                    ImGui.SetTooltip(UIStrings.RenamePreset);
                            }

                            ImGui.PopID();
                        }

                        ImGui.Unindent(10);
                        ImGui.TreePop();
                    }

                    ImGui.Separator();

                    if (ImGui.Button(UIStrings.Import, new Vector2(120, 0)))
                    {
                        // Count how many presets are actually selected for import
                        int selectedCount = _tempImportFolder.Value.Presets.Count(p => _selectedPresetsForImport[p.UniqueId]);

                        // Create a new folder with the selected count in its name if no presets are selected
                        if (selectedCount == 0)
                        {
                            Notify.Error(UIStrings.NoPresetsSelected);
                            return;
                        }

                        folder.PresetIds = [];
                        // Add only selected presets to the preset list and folder
                        foreach (var preset in _tempImportFolder.Value.Presets)
                        {
                            if (_selectedPresetsForImport[preset.UniqueId])
                            {
                                // Apply the new name if it was changed
                                if (_presetImportNames.TryGetValue(preset.UniqueId, out string? newName))
                                {
                                    preset.PresetName = newName;
                                }

                                _basePreset.CustomPresets.Add(preset);
                                folder.AddPreset(preset.UniqueId);
                            }
                        }

                        // Add the folder
                        _basePreset.Folders.Add(folder);

                        Service.Save();
                        Notify.Success($"Folder imported with {folder.PresetIds.Count} presets");

                        _tempImportFolder = null;
                        _selectedPresetsForImport.Clear();
                        _presetImportNames.Clear();
                        _renamePresetId = null;
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.SameLine();

                    if (ImGui.Button(UIStrings.DrawImportExport_Cancel, new Vector2(120, 0)))
                    {
                        _tempImportFolder = null;
                        _selectedPresetsForImport.Clear();
                        _presetImportNames.Clear();
                        _renamePresetId = null;
                        ImGui.CloseCurrentPopup();
                    }
                }
                else if (!_isImportingFolder && _tempImportPreset != null)
                {
                    // Handle preset import - EXACTLY matching the DrawImportPreset method
                    var name = _tempImportPreset.PresetName;

                    if (_tempImportPreset.PresetName.StartsWith(@"[Old Version]"))
                        ImGui.TextColored(ImGuiColors.ParsedOrange, UIStrings.Old_Preset_Warning);
                    else
                        ImGui.TextWrapped(UIStrings.ImportThisPreset);

                    if (ImGui.InputText(UIStrings.PresetName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
                        _tempImportPreset.RenamePreset(name);

                    if (ImGui.Button(UIStrings.Import, new Vector2(120, 0)))
                    {
                        Service.Save();
                        _basePreset.AddNewPreset(_tempImportPreset);
                        _basePreset.SelectedPreset = (CustomPresetConfig)_tempImportPreset;
                        _tempImportPreset = null;
                        Service.Save();
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.SameLine();

                    if (ImGui.Button(UIStrings.DrawImportExport_Cancel, new Vector2(120, 0)))
                    {
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
        }
        catch (Exception e)
        {
            Svc.Log.Error(e.ToString());
            Notify.Error(e.Message);
        }
    }

    private void DrawCreateFolderPopup()
    {
        ImGui.OpenPopup(UIStrings.CreateNewFolder);

        ImGui.SetNextWindowSize(new Vector2(300, 120));
        if (ImGui.BeginPopupModal(UIStrings.CreateNewFolder, ref promptingForFolderName, ImGuiWindowFlags.NoResize))
        {
            ImGui.Text(UIStrings.FolderNameHint);
            ImGui.Separator();

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputText("##newFolderName", ref newFolderName, 100);

            ImGui.Spacing();

            if (ImGui.Button(UIStrings.Create, new Vector2(120, 0)))
            {
                if (!string.IsNullOrWhiteSpace(newFolderName))
                {
                    _basePreset.AddNewFolder(newFolderName);
                    newFolderName = string.Empty;
                    promptingForFolderName = false;
                }
            }

            ImGui.SameLine();

            if (ImGui.Button(UIStrings.DrawImportExport_Cancel, new Vector2(120, 0)))
            {
                newFolderName = string.Empty;
                promptingForFolderName = false;
            }

            ImGui.EndPopup();
        }
    }

    private void DrawRenameFolderPopup()
    {
        ImGui.OpenPopup(UIStrings.RenameFolder);

        ImGui.SetNextWindowSize(new Vector2(300, 120));
        bool isOpen = true;
        if (ImGui.BeginPopupModal(UIStrings.RenameFolder, ref isOpen, ImGuiWindowFlags.NoResize))
        {
            ImGui.Text(UIStrings.EnterNewFolderName);
            ImGui.Separator();

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputText("##renameFolderName", ref renameFolderName, 100);

            ImGui.Spacing();

            if (ImGui.Button(UIStrings.Rename, new Vector2(120, 0)))
            {
                if (!string.IsNullOrWhiteSpace(renameFolderName) && renameFolderId.HasValue)
                {
                    var folder = _basePreset.Folders.FirstOrDefault(f => f.UniqueId == renameFolderId.Value);
                    if (folder != null)
                    {
                        folder.FolderName = renameFolderName;
                        Service.Save();
                    }
                    renameFolderName = string.Empty;
                    renameFolderId = null;
                }
            }

            ImGui.SameLine();

            if (ImGui.Button(UIStrings.DrawImportExport_Cancel, new Vector2(120, 0)))
            {
                renameFolderName = string.Empty;
                renameFolderId = null;
            }

            if (!isOpen)
            {
                renameFolderName = string.Empty;
                renameFolderId = null;
            }

            ImGui.EndPopup();
        }
    }

    private void DrawFolderContextMenu(PresetFolder folder)
    {
        if (!ImGui.BeginPopupContextItem(folder.UniqueId.ToString()))
            return;

        if (ImGui.Selectable(UIStrings.Rename, false, ImGuiSelectableFlags.DontClosePopups))
        {
            renameFolderId = folder.UniqueId;
            renameFolderName = folder.FolderName;
        }

        if (ImGui.Selectable(UIStrings.MakeACopy, false))
        {

            var newFolder = new PresetFolder($"Copy_{folder.FolderName}");

            // First, collect all presets in the source folder
            var presetsToCopy = new List<CustomPresetConfig>();
            foreach (var presetId in folder.PresetIds)
            {
                var originalPreset = _basePreset.CustomPresets.FirstOrDefault(p => p.UniqueId == presetId);
                if (originalPreset != null)
                {
                    presetsToCopy.Add(originalPreset);
                }
            }

            // Create copies of each preset and add them to the new folder
            foreach (var origPreset in presetsToCopy)
            {
                // Create a completely new copy with new GUID
                var json = JsonConvert.SerializeObject(origPreset);
                var presetCopy = JsonConvert.DeserializeObject<CustomPresetConfig>(json);

                // Generate a new GUID for the copy
                presetCopy!.UniqueId = Guid.NewGuid();
                presetCopy.PresetName = origPreset.PresetName;

                // Add to preset list first
                _basePreset.CustomPresets.Add(presetCopy);

                // Then add to folder
                newFolder.AddPreset(presetCopy.UniqueId);
            }

            // Add the folder to the list
            _basePreset.Folders.Add(newFolder);
            Service.Save();
        }

        if (ImGui.Selectable(UIStrings.ExportFolderClipboard, false))
        {
            var exportData = Configuration.ExportFolder(folder, _basePreset.CustomPresets);
            ImGui.SetClipboardText(exportData);
            Notify.Success(UIStrings.FolderExported);
        }

        bool isEmpty = folder.PresetIds.Count == 0;
        using (var disabled = ImRaii.Disabled(!isEmpty || !ImGui.GetIO().KeyShift))
        {
            if (ImGui.Selectable(UIStrings.Delete, false, ImGuiSelectableFlags.DontClosePopups))
            {
                _basePreset.RemoveFolder(folder.UniqueId);
                Service.Save();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (!isEmpty)
                ImGui.SetTooltip(UIStrings.FolderMostBeEmpty);
            else
                ImGui.SetTooltip(UIStrings.HoldShiftToDelete);
        }

        ImGui.EndPopup();
    }

    public static void DrawPresetContext(BasePresetConfig preset)
    {
        if (preset == null)
            return;

        if (!ImGui.BeginPopupContextItem(@$"PresetOptions###{preset.PresetName}"))
            return;

        var alreadySelected = _basePreset.SelectedPreset?.PresetName == preset.PresetName;
        if (ImGui.Selectable(!alreadySelected ? UIStrings.SetActive : UIStrings.Deselect))
        {
            _basePreset.SelectedPreset = alreadySelected ? null : (CustomPresetConfig)preset;
            Service.Save();
        }

        if (ImGui.Selectable(UIStrings.Rename, false, ImGuiSelectableFlags.DontClosePopups))
        {
            ImGui.OpenPopup(@$"PresetRenameName");
        }

        if (ImGui.Selectable(UIStrings.MakeACopy, false))
        {
            CopyPreset(preset);
        }

        DrawUtil.DrawRenamePreset(preset);

        if (ImGui.Selectable(UIStrings.ExportPresetToClipboard, false))
        {
            ImGui.SetClipboardText(Configuration.ExportPreset(preset));
            Notify.Success(UIStrings.PresetExportedToTheClipboard);
        }

        using (var disabled = ImRaii.Disabled(!ImGui.GetIO().KeyShift))
        {
            if (ImGui.Selectable(UIStrings.Delete, false, ImGuiSelectableFlags.DontClosePopups))
            {
                _basePreset.RemovePreset(preset.UniqueId);
                displayed = null;
                Service.Save();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(UIStrings.HoldShiftToDelete);

        ImGui.EndPopup();
    }

    private static void CopyPreset(BasePresetConfig preset)
    {
        var json = JsonConvert.SerializeObject(preset);
        var copy = JsonConvert.DeserializeObject<CustomPresetConfig>(json);
        copy!.UniqueId = Guid.NewGuid();
        copy.PresetName = $"Copy_{preset.PresetName}";
        _basePreset.AddNewPreset(copy);
        Service.Save();
    }
}
