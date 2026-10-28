using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoHook.Ui;

public static class SubTabConditions {
    private static Guid? _selectedId;

    public static void DrawConditionsTab(CustomPresetConfig preset) {
        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudYellow, UIStrings.PresetConditions_HelpText);
        ImGui.Spacing();

        // stale selection (preset switch, delete) falls back to the first item without forgetting the old one
        var selected = preset.NamedConditions.FirstOrDefault(n => n.UniqueId == _selectedId)
                       ?? preset.NamedConditions.FirstOrDefault();

        var height = ImGui.GetContentRegionAvail().Y;
        using var table = ImRaii.Table("###PresetConditionsTable", 2, ImGuiTableFlags.BordersInnerV);
        if (!table)
            return;

        ImGui.TableSetupColumn("##list", ImGuiTableColumnFlags.WidthFixed, ListWidth(preset));
        ImGui.TableSetupColumn("##editor", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        DrawList(preset, selected, height);

        ImGui.TableNextColumn();
        selected = preset.NamedConditions.FirstOrDefault(n => n.UniqueId == _selectedId)
                   ?? preset.NamedConditions.FirstOrDefault();
        using var editor = ImRaii.Child("###PresetConditionEditor", new Vector2(0, height), false);
        if (selected == null)
            ImGui.TextDisabled("Add a condition to get started");
        else
            DrawEditor(preset, selected);
    }

    // fits the longest name (dot + text) and the header buttons, capped so the editor keeps most of the width
    private static float ListWidth(CustomPresetConfig preset) {
        var style = ImGui.GetStyle();
        var dotWidth = ImGui.GetTextLineHeight() + style.ItemInnerSpacing.X;
        var longest = preset.NamedConditions
            .Select(n => ImGui.CalcTextSize(string.IsNullOrWhiteSpace(n.Name) ? UIStrings.PresetConditions_NewName : n.Name).X)
            .DefaultIfEmpty(0)
            .Max();
        var header = ImGui.GetFrameHeight() * 2 + ImGui.CalcTextSize($"({preset.NamedConditions.Count})").X + style.ItemSpacing.X * 2;
        var width = Math.Max(dotWidth + longest, header) + style.ScrollbarSize + style.WindowPadding.X;
        return Math.Clamp(width, 120.Scaled(), Math.Max(120.Scaled(), ImGui.GetContentRegionAvail().X * 0.4f));
    }

    private static void DrawList(CustomPresetConfig preset, NamedConditionConfig? selected, float height) {
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus)) {
            var added = new NamedConditionConfig {
                Name = UIStrings.PresetConditions_NewName,
                ConditionSet = new(),
            };
            preset.NamedConditions.Add(added);
            _selectedId = added.UniqueId;
            Configuration.Save();
        }
        ImGui.TooltipOnHover(UIStrings.Add);

        ImGui.SameLine();
        using (ImRaii.Disabled(selected == null)) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy) && selected != null) {
                var copy = new NamedConditionConfig {
                    Name = $"{selected.Name} (copy)",
                    ConditionSet = ConditionUi.CloneConditionSet(selected.ConditionSet),
                };
                copy.ConditionSet.Expression = selected.ConditionSet.Expression;
                preset.NamedConditions.Insert(preset.NamedConditions.IndexOf(selected) + 1, copy);
                _selectedId = copy.UniqueId;
                Configuration.Save();
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Duplicate");

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudGrey, $"({preset.NamedConditions.Count})");

        var listHeight = Math.Max(height - ImGui.GetFrameHeightWithSpacing(), ImGui.GetFrameHeight());
        using var list = ImRaii.Child("###PresetConditionList", new Vector2(0, listHeight), false);

        var moveFrom = -1;
        var moveTo = -1;
        var lineHeight = ImGui.GetTextLineHeight();
        var dl = ImGui.GetWindowDrawList();
        for (var i = 0; i < preset.NamedConditions.Count; i++) {
            var named = preset.NamedConditions[i];
            named.EnsureUiId();
            using var id = ImRaii.PushId(named.UiId);

            var name = string.IsNullOrWhiteSpace(named.Name) ? UIStrings.PresetConditions_NewName : named.Name;
            var isTrue = IsNamedConditionTrue(named);
            var start = ImGui.GetCursorScreenPos();

            if (ImGui.Selectable("##named", named == selected, ImGuiSelectableFlags.None, new Vector2(0, lineHeight)))
                _selectedId = named.UniqueId;
            ImGui.TooltipOnHover(name);

            ImGui.DragDropSource(i, "NAMED_CONDITION"u8, name);
            ImGui.DragDropTarget(i, "NAMED_CONDITION"u8, preset.NamedConditions.Count, (sourceIndex, insertIndex) => {
                moveFrom = sourceIndex;
                moveTo = insertIndex;
            });

            // dot first so a long name can't push it out of view
            var dotColor = isTrue ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudGrey3;
            dl.AddCircleFilled(start + new Vector2(lineHeight / 2, lineHeight / 2), lineHeight * 0.25f, ImGui.GetColorU32(dotColor));
            dl.AddText(start + new Vector2(lineHeight + ImGui.GetStyle().ItemInnerSpacing.X, 0), ImGui.GetColorU32(ImGuiCol.Text), name);
        }

        if (moveFrom >= 0 && moveTo >= 0 && moveFrom < preset.NamedConditions.Count) {
            var item = preset.NamedConditions[moveFrom];
            preset.NamedConditions.RemoveAt(moveFrom);
            if (moveTo > moveFrom) moveTo--;
            preset.NamedConditions.Insert(Math.Clamp(moveTo, 0, preset.NamedConditions.Count), item);
            Configuration.Save();
        }
    }

    private static void DrawEditor(CustomPresetConfig preset, NamedConditionConfig named) {
        named.EnsureUiId();
        using var id = ImRaii.PushId(named.UiId);

        var style = ImGui.GetStyle();
        var trashWidth = ImGui.GetFrameHeight();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            trashWidth = Math.Max(trashWidth, ImGui.CalcTextSize(FontAwesomeIcon.Trash.ToIconString()).X + style.FramePadding.X * 2);

        var name = named.Name;
        ImGui.SetNextItemWidth(Math.Max(ImGui.GetContentRegionAvail().X - trashWidth - style.ItemSpacing.X, 100.Scaled()));
        if (ImGui.InputTextWithHint("##name", UIStrings.PresetName, ref name, 64))
            named.Name = name;
        if (ImGui.IsItemDeactivatedAfterEdit())
            Configuration.Save();

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash) && ImGui.GetIO().KeyShift) {
            preset.NamedConditions.Remove(named);
            _selectedId = null;
            Configuration.Save();
            return;
        }
        ImGui.TooltipOnHover(UIStrings.HoldShiftToDelete);

        ImGui.Separator();
        ImGui.Spacing();

        var previousExclude = ConditionUi.ExcludeNamedConditionId;
        ConditionUi.ExcludeNamedConditionId = named.UniqueId;
        named.ConditionSet = ConditionUi.DrawConditionSet(UIStrings.Conditions, named.ConditionSet, ConditionScope.PresetDefinition, showAdvanced: true) ?? named.ConditionSet;
        ConditionUi.ExcludeNamedConditionId = previousExclude;
    }

    private static bool IsNamedConditionTrue(NamedConditionConfig named)
        => named.ConditionSet is { Groups.Count: > 0 } && named.ConditionSet.Evaluate(WorldState.Get(), Registry);
}
