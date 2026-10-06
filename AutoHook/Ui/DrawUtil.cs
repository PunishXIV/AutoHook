using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using System.Numerics;

namespace AutoHook.Ui;

public static class DrawUtil {
    public static bool EditFloatField(string label, ref float refValue, string helpText = "",
        bool hoverHelpText = false) {
        return EditFloatField(label, 85, ref refValue, helpText, hoverHelpText);
    }

    public static bool EditFloatField(string label, float fieldWidth, ref float refValue, string helpText = "",
        bool hoverHelpText = false) {
        using var id = ImRaii.PushId(label);
        TextV(label);

        ImGui.SameLine();

        using (ImRaii.ItemWidth(fieldWidth.Scale())) {
            var clicked = ImGui.InputFloat($"##{label}###", ref refValue, .1f, 0, @"%.1f%");

            if (helpText != string.Empty) {
                if (hoverHelpText)
                    ImGui.TooltipOnHover(helpText);
                else
                    ImGuiComponents.HelpMarker(helpText);
            }

            return clicked;
        }
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

        using (ImRaii.ItemWidth(fieldWidth.Scale())) {
            var clicked = ImGui.InputInt($"##{label}###", ref refValue, steps, 0);

            if (helpText != string.Empty) {
                ImGuiComponents.HelpMarker(helpText);
            }

            return clicked;
        }
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
            Configuration.Save();
        }

        if (helpText != string.Empty) {
            if (hoverHelpText)
                ImGui.TooltipOnHover(helpText);
            else
                ImGuiComponents.HelpMarker(helpText);
        }

        return clicked;
    }

    private static string _filterText = "";

    public static void DrawComboSelector<T>(IReadOnlyList<T> itemList, Func<T, string> getItemName, string selectedItem, Action<T> onSelect) {
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
                        Configuration.Save();
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
                    Configuration.Save();
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
                        Configuration.Save();
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
            Configuration.Save();
            ImGui.CloseCurrentPopup();
        }

        if (ImGui.Button(UIStrings.Close)) {
            Configuration.Save();
            ImGui.CloseCurrentPopup();
        }
    }

    public static void DrawAddNewPresetButton(BasePreset presetConfig) {
        using (ImRaii.PushFont(UiBuilder.IconFont)) {
            var buttonSize = ImGui.CalcTextSize(FontAwesomeIcon.Plus.ToIconString()) + ImGui.GetStyle().FramePadding * 2;
            if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString(), buttonSize)) {
                try {
                    Configuration.Save();
                    presetConfig.AddNewPreset(@$"{UIStrings.NewPreset} {DateTime.Now}");
                    Configuration.Save();
                }
                catch (Exception e) {
                    IPluginLog.Get().Error(e.ToString());
                }
            }
        }
        ImGui.TooltipOnHover(UIStrings.AddNewPreset);
    }

    private static BasePresetConfig? _tempImport;

    // shared rename/warning/import/cancel body. true = finished (imported or cancelled).
    public static bool DrawPendingImportPreset(BasePresetConfig import, Action<BasePresetConfig> onConfirm, Vector2? buttonSize = null) {
        var name = import.PresetName;

        if (import.PresetName.StartsWith(@"[Old Version]"))
            ImGui.TextColored(ImGuiColors.ParsedOrange, UIStrings.Old_Preset_Warning);
        else
            ImGui.TextWrapped(UIStrings.ImportThisPreset);

        if (ImGui.InputText(UIStrings.PresetName, ref name, 64, ImGuiInputTextFlags.AutoSelectAll))
            import.RenamePreset(name);

        var size = buttonSize ?? default;
        if (ImGui.Button(UIStrings.Import, size)) {
            onConfirm(import);
            Configuration.Save();
            ImGui.CloseCurrentPopup();
            return true;
        }

        ImGui.SameLine();

        if (ImGui.Button(UIStrings.DrawImportExport_Cancel, size)) {
            ImGui.CloseCurrentPopup();
            return true;
        }

        return false;
    }

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
                if (DrawPendingImportPreset(_tempImport, basePreset.AddNewPreset))
                    _tempImport = null;
            }
        }
        catch (Exception e) {
            IPluginLog.Get().Error(e.ToString());
            Notify.Error(e.Message);
        }
    }

    public static void DrawDeletePresetButton(BasePreset itemList) {
        var selectedPreset = itemList.SelectedPreset;
        using (ImRaii.Disabled(!ImGui.GetIO().KeyShift || selectedPreset == null)) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash)) {
                itemList.RemovePreset(selectedPreset?.UniqueId ?? Guid.Empty);
                Configuration.Save();
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(UIStrings.HoldShiftToDelete);

    }

    private static IDisposable AlignTreeContentColumn(float contentX) {
        var y = ImGui.GetCursorPosY();
        ImGui.NewLine();
        var lineStartX = ImGui.GetCursorPosX();
        ImGui.SetCursorPos(new Vector2(contentX, y));

        var pad = contentX - lineStartX;
        if (pad > 0.5f)
            ImGui.Indent(pad);

        return new TreeContentColumnScope(pad);
    }

    private readonly struct TreeContentColumnScope(float pad) : IDisposable {
        public void Dispose() {
            if (pad > 0.5f)
                ImGui.Unindent(pad);
        }
    }

    private static void DrawTreeBody(float labelX, Action body, bool trailingSeparator) {
        ImGui.SetCursorPosX(labelX);
        TextV(" └");
        ImGui.SameLine();
        var contentX = ImGui.GetCursorPosX();
        using (AlignTreeContentColumn(contentX)) {
            using (ImRaii.Group()) {
                body();
                if (trailingSeparator)
                    ImGui.Separator();
            }
        }
    }

    public static void DrawCheckboxTree(string treeName, ref bool enable, Action? action = null, string helpText = "", bool forceOpen = false, bool highlightLabel = false, Action? drawLabelExtras = null) {
        using var id = ImRaii.PushId(treeName);
        if (ImGui.Checkbox("###checkbox", ref enable)) {
            if (enable) ImGui.SetNextItemOpen(true);
            Configuration.Save();
        }

        if (helpText != string.Empty)
            ImGui.TooltipOnHover(helpText);

        ImGui.SameLine(0, 3.Scaled());

        var labelX = ImGui.GetCursorPosX();

        if (action == null) {
            using (highlightLabel ? ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen) : null)
                ImGui.TreeNodeEx(treeName,
                    ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);
            if (!string.IsNullOrEmpty(helpText))
                ImGui.TooltipOnHover(helpText);
            drawLabelExtras?.Invoke();
            return;
        }

        if (forceOpen)
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        using (highlightLabel ? ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen) : null)
            if (ImGui.TreeNodeEx(treeName, ImGuiTreeNodeFlags.FramePadding)) {
                drawLabelExtras?.Invoke();
                if (helpText != string.Empty)
                    ImGui.TooltipOnHover(helpText);

                DrawTreeBody(labelX, action, trailingSeparator: true);
                ImGui.TreePop();
            }
            else {
                drawLabelExtras?.Invoke();
            }
    }

    // checkbox + inline collapsing header sharing one label. returns true if enable changed.
    public static bool DrawCheckboxHeader(string headerLabel, ref bool enable, ImGuiTreeNodeFlags flags, Action body, string helpText = "", bool forceOpen = false) {
        using var id = ImRaii.PushId("DrawCheckboxHeader");
        var changed = ImGui.Checkbox("###checkbox", ref enable);
        if (!string.IsNullOrEmpty(helpText))
            ImGui.TooltipOnHover(helpText);

        ImGui.SameLine(0, 3.Scaled());
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

        var labelX = ImGui.GetCursorPosX();
        if (ImGui.TreeNodeEx(treeName, ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.AllowItemOverlap)) {
            if (helpText != string.Empty)
                ImGui.TooltipOnHover(helpText);

            DrawTreeBody(labelX, action, trailingSeparator: false);
            ImGui.TreePop();
        }
        else if (helpText != string.Empty)
            ImGui.TooltipOnHover(helpText);
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
                Configuration.Save();
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
            Configuration.Save();
        }
    }

    public static void DrawBaitSwapSelector(BaitFishClass bait, Action<BaitFishClass> onSelect)
        => DrawComboSelector(FishBaitCatalog.Get().Baits, b => $"[#{b.Id}] {b.Name}", bait.Name, b => onSelect(new BaitFishClass(b)));

    public static void DrawPresetSwapSelector(string presetName, Action<string> onSelect)
        => DrawComboSelector(Configuration.C.HookPresets.CustomPresets, preset => preset.PresetName, presetName, preset => onSelect(preset.PresetName));

    public static void DrawSurfaceSlapAndIdenticalCast(AutoSurfaceSlap surfaceSlap, AutoIdenticalCast identicalCast) {
        DrawCheckboxTree(UIStrings.UseSurfaceSlap, ref surfaceSlap.Enabled,
            () => surfaceSlap.DrawFishCaughtActionOptions(), surfaceSlap.GetHelpText());

        DrawCheckboxTree(UIStrings.UseIdenticalCast, ref identicalCast.Enabled,
            () => identicalCast.DrawFishCaughtActionOptions(), identicalCast.GetHelpText());
    }

    public static void DrawStopAfter<TCD>(string label, SingleCondition<TCD, (bool Enabled, int Limit)> condition, ref FishingSteps stopStep, ref bool resetCount) where TCD : class, IConditionDefinition, ISimpleConditionValue<(bool Enabled, int Limit)> {
        using var _ = ImRaii.PushId(label);
        var step = stopStep;
        var reset = resetCount;
        DrawCaughtCountLimitTree(label, condition, () => {
            if (ImGui.RadioButton(UIStrings.Stop_Casting, step == FishingSteps.StopCasting)) {
                step = FishingSteps.StopCasting;
                Configuration.Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(UIStrings.Auto_Cast_Stopped);

            if (ImGui.RadioButton(UIStrings.Quit_Fishing, step == FishingSteps.QuitRequested)) {
                step = FishingSteps.QuitRequested;
                Configuration.Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(UIStrings.Quit_Action_HelpText);

            Checkbox(UIStrings.Reset_the_counter, ref reset);
        });
        stopStep = step;
        resetCount = reset;
    }
}
