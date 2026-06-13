using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using System.Numerics;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;

namespace AutoHook.Utils;

public static class DrawUtil
{
    public static void NumericDisplay(string label, int value)
    {
        ImGui.Text(label);
        ImGui.SameLine();
        ImGui.Text($"{value}");
    }

    public static void NumericDisplay(string label, string formattedString)
    {
        ImGui.Text(label);
        ImGui.SameLine();
        ImGui.Text(formattedString);
    }

    public static void NumericDisplay(string label, int value, Vector4 color)
    {
        ImGui.Text(label);
        ImGui.SameLine();
        ImGui.TextColored(color, $"{value}");
    }

    public static bool EditFloatField(string label, ref float refValue, string helpText = "",
        bool hoverHelpText = false)
    {
        return EditFloatField(label, 85, ref refValue, helpText, hoverHelpText);
    }

    public static bool EditFloatField(string label, float fieldWidth, ref float refValue, string helpText = "",
        bool hoverHelpText = false)
    {
        ImGui.PushID(label);
        TextV(label);

        ImGui.SameLine();

        ImGui.PushItemWidth(fieldWidth * ImGuiHelpers.GlobalScale);
        var clicked = ImGui.InputFloat($"##{label}###", ref refValue, .1f, 0, @"%.1f%");
        ImGui.PopItemWidth();

        if (helpText != string.Empty)
        {
            if (hoverHelpText)
            {
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(helpText);
            }
            else
                ImGuiComponents.HelpMarker(helpText);
        }

        ImGui.PopID();

        return clicked;
    }

    public static bool EditNumberField(string label, ref int refValue, string helpText = "", int steps = 0)
    {
        float fieldWidth = 30;

        if (steps > 0)
            fieldWidth = 85;

        return EditNumberField(label, fieldWidth, ref refValue, helpText, steps);
    }

    public static bool EditNumberField(string label, float fieldWidth, ref int refValue, string helpText = "",
        int steps = 0)
    {
        TextV(label);

        ImGui.SameLine();

        ImGui.PushItemWidth(fieldWidth * ImGuiHelpers.GlobalScale);
        var clicked = ImGui.InputInt($"##{label}###", ref refValue, steps, 0);
        ImGui.PopItemWidth();

        if (helpText != string.Empty)
        {
            ImGuiComponents.HelpMarker(helpText);
        }

        return clicked;
    }

    public static void TextV(string s)
    {
        var cur = ImGui.GetCursorPos();
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0);
        ImGui.Button("");
        ImGui.PopStyleVar();
        ImGui.SameLine();
        ImGui.SetCursorPos(cur);
        ImGui.TextUnformatted(s);
    }

    public static void Info(string text)
    {
        var cur = ImGui.GetCursorPos();
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0);
        ImGui.Button("");
        ImGui.PopStyleVar();
        ImGui.SameLine(0, 1);
        ImGui.SetCursorPos(cur);
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextDisabled(FontAwesomeIcon.QuestionCircle.ToIconString());
        ImGui.PopFont();

        HoveredTooltip(text);
    }

    public static void HoveredTooltip(string text)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    public static bool SubCheckbox(string label, ref bool refValue, string helpText = "", bool hoverHelpText = false)
    {
        TextV($" └");
        ImGui.SameLine();
        return Checkbox(label, ref refValue, helpText, hoverHelpText);
    }

    public static bool Checkbox(string label, ref bool refValue, string helpText = "", bool hoverHelpText = false)
    {
        bool clicked = false;

        if (ImGui.Checkbox($"{label}", ref refValue))
        {
            clicked = true;
            Service.Save();
        }

        if (helpText != string.Empty)
        {
            if (hoverHelpText)
            {
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(helpText);
            }
            else
                ImGuiComponents.HelpMarker(helpText);
        }

        return clicked;
    }

    public static void DrawWordWrappedString(string message)
    {
        var words = message.Split(' ');

        var windowWidth = ImGui.GetContentRegionAvail().X;
        var cumulativeSize = 0.0f;
        var padding = 2.0f;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2.0f, 0.0f));

        foreach (var word in words)
        {
            var wordWidth = ImGui.CalcTextSize(word).X;

            if (cumulativeSize == 0)
            {
                ImGui.Text(word);
                cumulativeSize += wordWidth + padding;
            }
            else if ((cumulativeSize + wordWidth) < windowWidth)
            {
                ImGui.SameLine();
                ImGui.Text(word);
                cumulativeSize += wordWidth + padding;
            }
            else if ((cumulativeSize + wordWidth) >= windowWidth)
            {
                ImGui.Text(word);
                cumulativeSize = wordWidth + padding;
            }
        }

        ImGui.PopStyleVar();
    }

    private static string _filterText = "";

    public static void DrawComboSelector<T>(
        List<T> itemList,
        Func<T, string> getItemName,
        string selectedItem,
        Action<T> onSelect)
    {
        ImGui.SetNextItemWidth(220 * ImGuiHelpers.GlobalScale);

        if (ImGui.BeginCombo("###search", selectedItem ?? "Error"))
        {
            ImGui.SetNextItemWidth(190 * ImGuiHelpers.GlobalScale);

            ImGui.InputTextWithHint("", UIStrings.Search_Hint, ref _filterText, 100);

            ImGui.Separator();

            using (var child = ImRaii.Child($"###ComboSelector", new Vector2(0, 100 * ImGuiHelpers.GlobalScale), false))
            {
                foreach (var (item, index) in itemList.WithIndex())
                {
                    var itemName = getItemName(item) ?? $"Error, Try renaming";

                    if (_filterText.Length != 0 && !itemName.ToLower().Contains(_filterText.ToLower()))
                        continue;
                    using var _ = ImRaii.PushId($"{itemName}###{index}");
                    if (ImGui.Selectable(itemName, false))
                    {
                        ImGui.CloseCurrentPopup();
                        onSelect(item);
                        _filterText = "";
                        Service.Save();
                    }
                }
            }

            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(selectedItem);
        }
    }

    public static void DrawComboSelectorPreset(BasePreset presetList)
    {
        ImGui.SetNextItemWidth(220 * ImGuiHelpers.GlobalScale);

        var selectedPreset = presetList.SelectedPreset;
        if (ImGui.BeginCombo("###search", selectedPreset?.PresetName ?? UIStrings.Disabled))
        {
            ImGui.SetNextItemWidth(210 * ImGuiHelpers.GlobalScale);

            ImGui.InputTextWithHint("", UIStrings.Search_Hint, ref _filterText, 100);

            ImGui.Separator();

            using (var child = ImRaii.Child("###ComboPreset", new Vector2(0, 100 * ImGuiHelpers.GlobalScale), false))
            {
                if (ImGui.Selectable(UIStrings.Disabled, presetList.SelectedPreset == null))
                {
                    Service.Save();
                    presetList.SelectedPreset = null;
                    ImGui.CloseCurrentPopup();
                }

                foreach (var item in presetList.PresetList)
                {
                    using var id = ImRaii.PushId(item.UniqueId.ToString());
                    var itemName = item.PresetName ?? $"Error, Try renaming";

                    if (_filterText.Length != 0 && !itemName.ToLower().Contains(_filterText.ToLower()))
                        continue;

                    var color = selectedPreset?.PresetName == itemName
                        ? ImGuiColors.DalamudYellow
                        : ImGuiColors.DalamudWhite;

                    using (var a = ImRaii.PushColor(ImGuiCol.Text, color))
                    {
                        if (ImGui.Selectable(itemName, false))
                        {
                            presetList.SelectedGuid = item.UniqueId.ToString();
                            _filterText = "";
                            Service.Save();
                            ImGui.CloseCurrentPopup();
                        }
                    }
                }
            }

            ImGui.EndCombo();
        }
        else if (selectedPreset != null)
        {
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(UIStrings.RightClickToRename);

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                ImGui.OpenPopup(@$"PresetRenameName");

            DrawRenamePreset(selectedPreset);
        }
    }

    public static void DrawRenamePreset(BasePresetConfig selectedPreset)
    {
        if (ImGui.BeginPopup(@$"PresetRenameName"))
        {
            ImGui.Text(UIStrings.EnterToConfirm);
            var name = selectedPreset.PresetName ?? "Rename";
            if (ImGui.InputText(UIStrings.PresetName, ref name, 64,
                    ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
            {
                selectedPreset.RenamePreset(name);
                Service.Save();
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.Button(UIStrings.Close))
            {
                Service.Save();
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    public static void DrawAddNewPresetButton(BasePreset presetConfig)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        var buttonSize = ImGui.CalcTextSize(FontAwesomeIcon.Plus.ToIconString()) + ImGui.GetStyle().FramePadding * 2;
        if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString(), buttonSize))
        {
            try
            {
                Service.Save();
                presetConfig.AddNewPreset(@$"{UIStrings.NewPreset} {DateTime.Now}");
                Service.Save();
            }
            catch (Exception e)
            {
                Svc.Log.Error(e.ToString());
            }
        }

        ImGui.PopFont();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(UIStrings.AddNewPreset);
    }

    private static BasePresetConfig? _tempImport;

    public static void DrawImportExport(BasePreset basePreset)
    {
        try
        {
            using (ImRaii.Disabled(basePreset.SelectedPreset == null))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.FileExport))
                {
                    ImGui.SetClipboardText(Configuration.ExportPreset(basePreset.SelectedPreset!));

                    Notify.Success(UIStrings.PresetExportedToTheClipboard);
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(UIStrings.ExportPresetToClipboard);

                ImGui.SameLine();
            }
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport))
            {
                _tempImport = Configuration.ImportPreset(ImGui.GetClipboardText());
                if (_tempImport != null)
                    ImGui.OpenPopup(@"import_new_preset");
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(UIStrings.ImportPresetFromClipboard);

            using var popup = ImRaii.Popup("import_new_preset");

            if (popup.Success && _tempImport != null)
            {
                var name = _tempImport.PresetName;

                if (_tempImport.PresetName.StartsWith(@"[Old Version]"))
                    ImGui.TextColored(ImGuiColors.ParsedOrange, UIStrings.Old_Preset_Warning);
                else
                    ImGui.TextWrapped(UIStrings.ImportThisPreset);

                if (ImGui.InputText(UIStrings.PresetName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
                    _tempImport.RenamePreset(name);

                if (ImGui.Button(UIStrings.Import))
                {
                    Service.Save();
                    basePreset.AddNewPreset(_tempImport);
                    _tempImport = null;
                    Service.Save();
                }

                ImGui.SameLine();

                if (ImGui.Button(UIStrings.DrawImportExport_Cancel))
                {
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        catch (Exception e)
        {
            Svc.Log.Error(e.ToString());
            Notify.Error(e.Message);
        }
    }

    public static void DrawImportPreset(BasePreset hookPresets)
    {
        try
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport))
            {
                _tempImport = Configuration.ImportPreset(ImGui.GetClipboardText());
                if (_tempImport != null)
                    ImGui.OpenPopup(@"import_new_preset");
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(UIStrings.ImportPresetFromClipboard);

            using var popup = ImRaii.Popup("import_new_preset");
            if (popup.Success && _tempImport != null)
            {
                var name = _tempImport.PresetName;

                if (_tempImport.PresetName.StartsWith(@"[Old Version]"))
                    ImGui.TextColored(ImGuiColors.ParsedOrange, UIStrings.Old_Preset_Warning);
                else
                    ImGui.TextWrapped(UIStrings.ImportThisPreset);

                if (ImGui.InputText(UIStrings.PresetName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
                    _tempImport.RenamePreset(name);

                if (ImGui.Button(UIStrings.Import))
                {
                    Service.Save();
                    hookPresets.AddNewPreset(_tempImport);
                    hookPresets.SelectedPreset = _tempImport;
                    _tempImport = null;
                    Service.Save();
                }

                ImGui.SameLine();

                if (ImGui.Button(UIStrings.DrawImportExport_Cancel))
                {
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        catch (Exception e)
        {
            Svc.Log.Error(e.ToString());
            Notify.Error(e.Message);
        }
    }

    public static void DrawDeletePresetButton(BasePreset itemList)
    {
        var selectedPreset = itemList.SelectedPreset;
        using (ImRaii.Disabled(!ImGui.GetIO().KeyShift || selectedPreset == null))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
            {
                itemList.RemovePreset(selectedPreset?.UniqueId ?? Guid.Empty);
                Service.Save();
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(UIStrings.HoldShiftToDelete);

    }

    public static void DrawCheckboxTree(string treeName, ref bool enable, Action action, string helpText = "")
    {
        ImGui.PushID(treeName);
        if (ImGui.Checkbox($"###checkbox{treeName}", ref enable))
        {
            if (enable) ImGui.SetNextItemOpen(true);
            Service.Save();
        }

        if (helpText != string.Empty)
        {
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(helpText);
        }

        ImGui.SameLine(0, 3);
        if (Service.Configuration.SwapToButtons)
        {
            switch (Service.Configuration.SwapType)
            {
                case 0:
                    DrawButtonPopupType0(treeName, action, helpText);
                    break;
                case 1:
                    DrawButtonPopupType1(treeName, action, helpText);
                    break;
            }
        }
        else
        {
            var x = ImGui.GetCursorPosX();
            if (ImGui.TreeNodeEx(treeName, ImGuiTreeNodeFlags.FramePadding))
            {
                ImGui.SetCursorPosX(x);
                TextV($" └");
                ImGui.SameLine();

                x = ImGui.GetCursorPosX();
                if (ImGui.IsItemHovered() && helpText != string.Empty)
                    ImGui.SetTooltip(helpText);

                ImGui.SetCursorPosX(x);
                ImGui.BeginGroup();
                action();
                ImGui.Separator();
                ImGui.EndGroup();

                ImGui.TreePop();
            }
        }

        ImGui.PopID();
    }

    public static void DrawTreeNodeEx(string treeName, Action action, string helpText = "")
    {
        ImGui.PushID(treeName);

        if (Service.Configuration.SwapToButtons)
        {
            switch (Service.Configuration.SwapType)
            {
                case 0:
                    DrawButtonPopupType0(treeName, action, helpText);
                    break;
                case 1:
                    DrawButtonPopupType1(treeName, action, helpText);
                    break;
            }
        }
        else
        {
            var x = ImGui.GetCursorPosX();
            if (ImGui.TreeNodeEx(treeName, ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.AllowItemOverlap))
            {
                if (ImGui.IsItemHovered() && helpText != string.Empty)
                    ImGui.SetTooltip(helpText);

                ImGui.SetCursorPosX(x);
                ImGui.BeginGroup();
                TextV($" └");
                ImGui.SameLine();
                action();
                ImGui.Separator();
                ImGui.EndGroup();
                ImGui.TreePop();
            }
            else if (ImGui.IsItemHovered() && helpText != string.Empty)
                ImGui.SetTooltip(helpText);
        }

        ImGui.PopID();
    }

    public static void DrawButtonPopupType0(string popupName, Action action, string helpText = "")
    {
        ImGui.PushID(popupName);

        int indexOfId = popupName.IndexOf('#');
        if (indexOfId != -1)
        {
            popupName = popupName[..indexOfId];
        }

        TextV(popupName);
        ImGui.SameLine();
        if (ImGui.Button(UIStrings.Configure))
        {
            ImGui.OpenPopup(popupName);
        }

        if (ImGui.IsItemHovered() && helpText != string.Empty)
            ImGui.SetTooltip(helpText);

        if (ImGui.BeginPopup(popupName, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.Tooltip))
        {
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            ImGui.GetForegroundDrawList()
                .AddRect(windowPos, windowPos + windowSize, ImGui.GetColorU32(ImGuiCol.Separator));

            action();
            ImGui.EndPopup();
        }

        ImGui.PopID();
    }

    public static void DrawButtonPopupType1(string popupName, Action action, string helpText = "")
    {
        ImGui.PushID(popupName);
        if (ImGui.Button(popupName))
        {
            ImGui.OpenPopup(popupName);
        }

        if (ImGui.IsItemHovered() && helpText != string.Empty)
            ImGui.SetTooltip(helpText);

        if (ImGui.BeginPopup(popupName, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.Tooltip))
        {
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            ImGui.GetForegroundDrawList()
                .AddRect(windowPos, windowPos + windowSize, ImGui.GetColorU32(ImGuiCol.Separator));

            action();
            ImGui.EndPopup();
        }

        ImGui.PopID();
    }

    public static void SpacingSeparator()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }
}
