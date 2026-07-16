using AutoHook.Conditions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using System.Numerics;

namespace AutoHook.Utils;

public static class DrawUtil {
    public static void NumericDisplay(string label, int value) {
        ImGui.Text(label);
        ImGui.SameLine();
        ImGui.Text($"{value}");
    }

    public static void NumericDisplay(string label, string formattedString) {
        ImGui.Text(label);
        ImGui.SameLine();
        ImGui.Text(formattedString);
    }

    public static void NumericDisplay(string label, int value, Vector4 color) {
        ImGui.Text(label);
        ImGui.SameLine();
        ImGui.TextColored(color, $"{value}");
    }

    public static bool EditFloatField(string label, ref float refValue, string helpText = "",
        bool hoverHelpText = false) {
        return EditFloatField(label, 85, ref refValue, helpText, hoverHelpText);
    }

    public static bool EditFloatField(string label, float fieldWidth, ref float refValue, string helpText = "",
        bool hoverHelpText = false) {
        using var id = ImRaii.PushId(label);
        TextV(label);

        ImGui.SameLine();

        ImGui.PushItemWidth(fieldWidth.Scale());
        var clicked = ImGui.InputFloat($"##{label}###", ref refValue, .1f, 0, @"%.1f%");
        ImGui.PopItemWidth();

        if (helpText != string.Empty) {
            if (hoverHelpText)
                ImGui.TooltipOnHover(helpText);
            else
                ImGuiComponents.HelpMarker(helpText);
        }

        return clicked;
    }

    public static bool EditNumberField(string label, ref int refValue, string helpText = "", int steps = 0) {
        float fieldWidth = 30;

        if (steps > 0)
            fieldWidth = 85;

        return EditNumberField(label, fieldWidth, ref refValue, helpText, steps);
    }

    public static bool EditNumberField(string label, float fieldWidth, ref int refValue, string helpText = "",
        int steps = 0) {
        TextV(label);

        ImGui.SameLine();

        ImGui.PushItemWidth(fieldWidth.Scale());
        var clicked = ImGui.InputInt($"##{label}###", ref refValue, steps, 0);
        ImGui.PopItemWidth();

        if (helpText != string.Empty) {
            ImGuiComponents.HelpMarker(helpText);
        }

        return clicked;
    }

    public static void TextV(string s) {
        ImGui.AlignTextToFramePadding();
        ImGui.Text(s);
    }

    public static void Info(string text) {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextDisabled(FontAwesomeIcon.QuestionCircle.ToIconString());

        HoveredTooltip(text);
    }

    public static void HoveredTooltip(string text) => ImGui.TooltipOnHover(text);

    public static bool SubCheckbox(string label, ref bool refValue, string helpText = "", bool hoverHelpText = false) {
        TextV($" └");
        ImGui.SameLine();
        return Checkbox(label, ref refValue, helpText, hoverHelpText);
    }

    public static bool Checkbox(string label, ref bool refValue, string helpText = "", bool hoverHelpText = false) {
        var clicked = false;

        if (ImGui.Checkbox($"{label}", ref refValue)) {
            clicked = true;
            Service.Save();
        }

        if (helpText != string.Empty) {
            if (hoverHelpText)
                ImGui.TooltipOnHover(helpText);
            else
                ImGuiComponents.HelpMarker(helpText);
        }

        return clicked;
    }

    public static void DrawWordWrappedString(string message) {
        var words = message.Split(' ');

        var windowWidth = ImGui.GetContentRegionAvail().X;
        var cumulativeSize = 0.0f;
        var padding = 2.0f;

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(2.0f.Scale(), 0.0f))) {
            foreach (var word in words) {
                var wordWidth = ImGui.CalcTextSize(word).X;

                if (cumulativeSize == 0) {
                    ImGui.Text(word);
                    cumulativeSize += wordWidth + padding;
                }
                else if ((cumulativeSize + wordWidth) < windowWidth) {
                    ImGui.SameLine();
                    ImGui.Text(word);
                    cumulativeSize += wordWidth + padding;
                }
                else if ((cumulativeSize + wordWidth) >= windowWidth) {
                    ImGui.Text(word);
                    cumulativeSize = wordWidth + padding;
                }
            }
        }
    }

    private static string _filterText = "";

    public static void DrawComboSelector<T>(List<T> itemList, Func<T, string> getItemName, string selectedItem, Action<T> onSelect) {
        var padding = ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetFrameHeight();
        var width = Math.Max(220.Scaled(), itemList.Select(i => getItemName(i) ?? "").Append(selectedItem).Max(n => ImGui.CalcTextSize(n).X + padding));

        ImGui.SetNextItemWidth(width);

        using (var combo = ImRaii.Combo("###search", selectedItem)) {
            if (combo.Success) {
                ImGui.SetNextItemWidth(width - 30.Scaled());
                ImGui.InputTextWithHint("##filter", UIStrings.Search_Hint, ref _filterText, 100);
                ImGui.Separator();

                using var child = ImRaii.Child($"###ComboSelector", new Vector2(0, 100.Scaled()), false);
                foreach (var (item, index) in itemList.WithIndex()) {
                    var itemName = getItemName(item) ?? $"Error, Try renaming";

                    if (_filterText.Length != 0 && !itemName.Contains(_filterText, StringComparison.CurrentCultureIgnoreCase))
                        continue;
                    using var _ = ImRaii.PushId($"{itemName}###{index}");
                    if (ImGui.Selectable(itemName, false)) {
                        ImGui.CloseCurrentPopup();
                        onSelect(item);
                        _filterText = "";
                        Service.Save();
                    }
                }
            }
        }
        ImGui.TooltipOnHover(selectedItem);
    }

    public static void DrawComboSelectorPreset(BasePreset presetList) {
        ImGui.SetNextItemWidth(220.Scaled());

        var selectedPreset = presetList.SelectedPreset;
        var comboOpen = false;
        using (var combo = ImRaii.Combo("###search", selectedPreset?.PresetName ?? UIStrings.Disabled)) {
            comboOpen = combo.Success;
            if (combo.Success) {
                ImGui.SetNextItemWidth(210.Scaled());
                ImGui.InputTextWithHint("##filter", UIStrings.Search_Hint, ref _filterText, 100);
                ImGui.Separator();

                using var child = ImRaii.Child("###ComboPreset", new Vector2(0, 100.Scaled()), false);
                if (ImGui.Selectable(UIStrings.Disabled, presetList.SelectedPreset == null)) {
                    Service.Save();
                    presetList.SelectedPreset = null;
                    ImGui.CloseCurrentPopup();
                }

                foreach (var item in presetList.PresetList) {
                    using var id = ImRaii.PushId(item.UniqueId.ToString());
                    var itemName = item.PresetName ?? $"Error, Try renaming";

                    if (_filterText.Length != 0 && !itemName.ToLower().Contains(_filterText.ToLower()))
                        continue;

                    var color = selectedPreset?.PresetName == itemName
                        ? ImGuiColors.DalamudYellow
                        : ImGuiColors.DalamudWhite;

                    using var a = ImRaii.PushColor(ImGuiCol.Text, color);
                    if (ImGui.Selectable(itemName, false)) {
                        presetList.SelectedGuid = item.UniqueId.ToString();
                        _filterText = "";
                        Service.Save();
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
        }

        if (!comboOpen && selectedPreset != null) {
            ImGui.TooltipOnHover(UIStrings.RightClickToRename);

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                ImGui.OpenPopup(@$"PresetRenameName");

            DrawRenamePreset(selectedPreset);
        }
    }

    public static void DrawRenamePreset(BasePresetConfig selectedPreset) {
        using var popup = ImRaii.Popup(@$"PresetRenameName");
        if (!popup.Success) return;

        ImGui.Text(UIStrings.EnterToConfirm);
        var name = selectedPreset.PresetName ?? "Rename";
        if (ImGui.InputText(UIStrings.PresetName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue)) {
            selectedPreset.RenamePreset(name);
            Service.Save();
            ImGui.CloseCurrentPopup();
        }

        if (ImGui.Button(UIStrings.Close)) {
            Service.Save();
            ImGui.CloseCurrentPopup();
        }
    }

    public static void DrawAddNewPresetButton(BasePreset presetConfig) {
        using (ImRaii.PushFont(UiBuilder.IconFont)) {
            var buttonSize = ImGui.CalcTextSize(FontAwesomeIcon.Plus.ToIconString()) + ImGui.GetStyle().FramePadding * 2;
            if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString(), buttonSize)) {
                try {
                    Service.Save();
                    presetConfig.AddNewPreset(@$"{UIStrings.NewPreset} {DateTime.Now}");
                    Service.Save();
                }
                catch (Exception e) {
                    Svc.Log.Error(e.ToString());
                }
            }
        }
        ImGui.TooltipOnHover(UIStrings.AddNewPreset);
    }

    private static BasePresetConfig? _tempImport;

    public static void DrawImportExport(BasePreset basePreset) {
        try {
            using (ImRaii.Disabled(basePreset.SelectedPreset == null)) {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.FileExport)) {
                    ImGui.SetClipboardText(Configuration.ExportPreset(basePreset.SelectedPreset!));

                    Notify.Success(UIStrings.PresetExportedToTheClipboard);
                }

                ImGui.TooltipOnHover(UIStrings.ExportPresetToClipboard);

                ImGui.SameLine();
            }
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport)) {
                _tempImport = Configuration.ImportPreset(ImGui.GetClipboardText());
                if (_tempImport != null)
                    ImGui.OpenPopup(@"import_new_preset");
            }

            ImGui.TooltipOnHover(UIStrings.ImportPresetFromClipboard);

            using var popup = ImRaii.Popup("import_new_preset");

            if (popup.Success && _tempImport != null) {
                var name = _tempImport.PresetName;

                if (_tempImport.PresetName.StartsWith(@"[Old Version]"))
                    ImGui.TextColored(ImGuiColors.ParsedOrange, UIStrings.Old_Preset_Warning);
                else
                    ImGui.TextWrapped(UIStrings.ImportThisPreset);

                if (ImGui.InputText(UIStrings.PresetName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
                    _tempImport.RenamePreset(name);

                if (ImGui.Button(UIStrings.Import)) {
                    Service.Save();
                    basePreset.AddNewPreset(_tempImport);
                    _tempImport = null;
                    Service.Save();
                }

                ImGui.SameLine();

                if (ImGui.Button(UIStrings.DrawImportExport_Cancel)) {
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        catch (Exception e) {
            Svc.Log.Error(e.ToString());
            Notify.Error(e.Message);
        }
    }

    public static void DrawImportPreset(BasePreset hookPresets) {
        try {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport)) {
                _tempImport = Configuration.ImportPreset(ImGui.GetClipboardText());
                if (_tempImport != null)
                    ImGui.OpenPopup(@"import_new_preset");
            }

            ImGui.TooltipOnHover(UIStrings.ImportPresetFromClipboard);

            using var popup = ImRaii.Popup("import_new_preset");
            if (popup.Success && _tempImport != null) {
                var name = _tempImport.PresetName;

                if (_tempImport.PresetName.StartsWith(@"[Old Version]"))
                    ImGui.TextColored(ImGuiColors.ParsedOrange, UIStrings.Old_Preset_Warning);
                else
                    ImGui.TextWrapped(UIStrings.ImportThisPreset);

                if (ImGui.InputText(UIStrings.PresetName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
                    _tempImport.RenamePreset(name);

                if (ImGui.Button(UIStrings.Import)) {
                    Service.Save();
                    hookPresets.AddNewPreset(_tempImport);
                    hookPresets.SelectedPreset = _tempImport;
                    _tempImport = null;
                    Service.Save();
                }

                ImGui.SameLine();

                if (ImGui.Button(UIStrings.DrawImportExport_Cancel)) {
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        catch (Exception e) {
            Svc.Log.Error(e.ToString());
            Notify.Error(e.Message);
        }
    }

    public static void DrawDeletePresetButton(BasePreset itemList) {
        var selectedPreset = itemList.SelectedPreset;
        using (ImRaii.Disabled(!ImGui.GetIO().KeyShift || selectedPreset == null)) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash)) {
                itemList.RemovePreset(selectedPreset?.UniqueId ?? Guid.Empty);
                Service.Save();
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(UIStrings.HoldShiftToDelete);

    }

    public static void DrawCheckboxTree(string treeName, ref bool enable, Action? action = null, string helpText = "", bool forceOpen = false, bool highlightLabel = false) {
        using var id = ImRaii.PushId(treeName);
        if (ImGui.Checkbox("###checkbox", ref enable)) {
            if (enable) ImGui.SetNextItemOpen(true);
            Service.Save();
        }

        if (helpText != string.Empty)
            ImGui.TooltipOnHover(helpText);

        ImGui.SameLine(0, 3.Scaled());

        if (Service.Configuration.SwapToButtons) {
            switch (Service.Configuration.SwapType) {
                case 0:
                    DrawButtonPopupType0(treeName, action, helpText, highlightLabel);
                    break;
                case 1:
                    DrawButtonPopupType1(treeName, action, helpText, highlightLabel);
                    break;
            }
        }
        else {
            var x = ImGui.GetCursorPosX();

            if (action == null) {
                using (highlightLabel ? ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen) : null)
                    ImGui.TreeNodeEx(treeName,
                        ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);
                if (!string.IsNullOrEmpty(helpText))
                    ImGui.TooltipOnHover(helpText);
                return;
            }

            if (forceOpen)
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            using (highlightLabel ? ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen) : null)
                if (ImGui.TreeNodeEx(treeName, ImGuiTreeNodeFlags.FramePadding)) {
                    ImGui.SetCursorPosX(x);
                    TextV($" └");
                    ImGui.SameLine();

                    x = ImGui.GetCursorPosX();
                    if (helpText != string.Empty)
                        ImGui.TooltipOnHover(helpText);

                    ImGui.SetCursorPosX(x);
                    using (ImRaii.Group()) {
                        action();
                        ImGui.Separator();
                    }

                    ImGui.TreePop();
                }
        }
    }

    /// <summary>
    /// Draws a checkbox and an inline collapsing header sharing a single label.
    /// The checkbox controls the 'enable' flag; the header expands/collapses the body.
    /// Returns true if 'enable' changed.
    /// </summary>
    public static bool DrawCheckboxHeader(string headerLabel, ref bool enable, ImGuiTreeNodeFlags flags, Action body, string helpText = "", bool forceOpen = false) {
        using var id = ImRaii.PushId("DrawCheckboxHeader");
        var changed = ImGui.Checkbox("###checkbox", ref enable);
        if (!string.IsNullOrEmpty(helpText))
            ImGui.TooltipOnHover(helpText);

        ImGui.SameLine(0, 6.Scaled());
        var x = ImGui.GetCursorPosX();
        if (forceOpen)
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        if (ImGui.CollapsingHeader($"{headerLabel}###hdr", flags)) {
            ImGui.SetCursorPosX(x);
            using (ImRaii.Group()) {
                body();
            }
        }

        return changed;
    }

    public static void DrawTreeNodeEx(string treeName, Action action, string helpText = "") {
        using var id = ImRaii.PushId(treeName);

        if (Service.Configuration.SwapToButtons) {
            switch (Service.Configuration.SwapType) {
                case 0:
                    DrawButtonPopupType0(treeName, action, helpText);
                    break;
                case 1:
                    DrawButtonPopupType1(treeName, action, helpText);
                    break;
            }
        }
        else {
            var x = ImGui.GetCursorPosX();
            if (ImGui.TreeNodeEx(treeName, ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.AllowItemOverlap)) {
                if (helpText != string.Empty)
                    ImGui.TooltipOnHover(helpText);

                ImGui.SetCursorPosX(x);
                using (ImRaii.Group()) {
                    TextV($" └");
                    ImGui.SameLine();
                    action();
                }
                ImGui.TreePop();
            }
            else if (helpText != string.Empty)
                ImGui.TooltipOnHover(helpText);
        }
    }

    public static void DrawButtonPopupType0(string popupName, Action? action, string helpText = "", bool highlightLabel = false) {
        using var id = ImRaii.PushId(popupName);

        var indexOfId = popupName.IndexOf('#');
        if (indexOfId != -1) {
            popupName = popupName[..indexOfId];
        }

        using (highlightLabel ? ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen) : null)
            TextV(popupName);
        ImGui.SameLine();
        if (ImGui.Button(UIStrings.Configure)) {
            ImGui.OpenPopup(popupName);
        }

        if (helpText != string.Empty)
            ImGui.TooltipOnHover(helpText);

        using var popup = ImRaii.Popup(popupName, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.Tooltip);
        if (popup.Success) {
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            ImGui.GetForegroundDrawList().AddRect(windowPos, windowPos + windowSize, ImGui.GetColorU32(ImGuiCol.Separator));

            action?.Invoke();
        }
    }

    public static void DrawButtonPopupType1(string popupName, Action? action, string helpText = "", bool highlightLabel = false) {
        using var id = ImRaii.PushId(popupName);
        using (highlightLabel ? ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen) : null)
            if (ImGui.Button(popupName)) {
                ImGui.OpenPopup(popupName);
            }

        if (helpText != string.Empty)
            ImGui.TooltipOnHover(helpText);

        using var popup = ImRaii.Popup(popupName, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.Tooltip);
        if (popup.Success) {
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            ImGui.GetForegroundDrawList().AddRect(windowPos, windowPos + windowSize, ImGui.GetColorU32(ImGuiCol.Separator));

            action?.Invoke();
        }
    }

    public static void SpacingSeparator() {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    public static void DrawCaughtCountLimitTree<TCD>(
        string label,
        SingleCondition<TCD, (bool Enabled, int Limit)> condition,
        Action drawBody)
        where TCD : class, IConditionDefinition, ISimpleConditionValue<(bool Enabled, int Limit)> {
        DrawEnabledLimitTree(label, condition, (ref int limitLocal) => {
            ImGui.SetNextItemWidth(90.Scaled());
            if (ImGui.InputInt(UIStrings.TimeS, ref limitLocal)) {
                limitLocal = Math.Max(1, limitLocal);
                Service.Save();
            }

            drawBody();
        });
    }

    public delegate void DrawLimitBodyDelegate(ref int limitLocal);

    public static void DrawEnabledLimitTree<TCD>(
        string label,
        SingleCondition<TCD, (bool Enabled, int Limit)> condition,
        DrawLimitBodyDelegate drawBody)
        where TCD : class, IConditionDefinition, ISimpleConditionValue<(bool Enabled, int Limit)> {
        var (enabled, limit) = condition.Value;
        var enabledLocal = enabled;
        var limitLocal = limit;
        DrawCheckboxTree(label, ref enabledLocal, () => drawBody(ref limitLocal));
        if (enabledLocal != enabled || limitLocal != limit) {
            condition.Value = (enabledLocal, limitLocal);
            Service.Save();
        }
    }

    public static void DrawBaitSwapSelector(BaitFishClass bait, Action<BaitFishClass> onSelect)
        => DrawComboSelector(GameRes.Baits, b => $"[#{b.Id}] {b.Name}", bait.Name, onSelect);

    public static void DrawPresetSwapSelector(string presetName, Action<string> onSelect)
        => DrawComboSelector(Service.Configuration.HookPresets.CustomPresets, preset => preset.PresetName, presetName, preset => onSelect(preset.PresetName));

    public static void DrawSurfaceSlapAndIdenticalCast(AutoSurfaceSlap surfaceSlap, AutoIdenticalCast identicalCast) {
        DrawCheckboxTree(UIStrings.UseSurfaceSlap, ref surfaceSlap.Enabled,
            () => surfaceSlap.DrawFishCaughtActionOptions(), surfaceSlap.GetHelpText());

        DrawCheckboxTree(UIStrings.UseIdenticalCast, ref identicalCast.Enabled,
            () => identicalCast.DrawFishCaughtActionOptions(), identicalCast.GetHelpText());
    }
}
