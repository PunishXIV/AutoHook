using AutoHook.Spearfishing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;

namespace AutoHook.Ui;

internal class TabAutoGig : BaseTab {
    public override string TabName => "Spearfishing Presets";
    public override bool Enabled => true;

    public override OpenWindow Type => OpenWindow.AutoGig;

    private readonly SpearFishingPresets _gigCfg = Configuration.C.AutoGigConfig;

    // null = viewing Catch Everything (Global)
    private AutoGigConfig? _displayed;
    private bool _viewingGlobal = true;
    private bool _displayInitialized;

    public override void DrawHeader() {
        DrawTabDescription(UIStrings.TabAutoGigDescription);
    }

    public override void Draw() {
        if (!_displayInitialized) {
            _displayInitialized = true;
            if (_gigCfg.SelectedPreset is { } active) {
                _displayed = active;
                _viewingGlobal = false;
            }
        }

        using var table = ImRaii.Table("###GigPresetTable", 2, ImGuiTableFlags.Resizable);
        if (!table)
            return;

        ImGui.TableSetupColumn("###GigOptionColumn", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("###GigPresetColumn", ImGuiTableColumnFlags.WidthStretch, 1f);

        ImGui.TableNextColumn();
        using (ImRaii.Child("###GigOptionSide"))
            DrawLeftPane();

        ImGui.TableNextColumn();
        using (ImRaii.Child("###GigPresetSide"))
            DrawRightPane();
    }

    private void DrawLeftPane() {
        if (_viewingGlobal || _displayed == null) {
            DrawCatchEverythingOptions();
            return;
        }

        _displayed.DrawOptions();
    }

    private void DrawCatchEverythingOptions() {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X / 2 - ImGui.CalcTextSize(UIStrings.CatchEverything).X / 2);
        ImGui.TextColored(ImGuiColors.DalamudOrange, $" {UIStrings.CatchEverything}");

        using var tabs = ImRaii.TabBar("###CatchAllTabs", ImGuiTabBarFlags.NoTooltip);
        if (!tabs)
            return;

        using (var tab = ImRaii.TabItem(UIStrings.Conditions)) {
            if (tab)
                _gigCfg.CatchAllConditionSet = ConditionUi.DrawConditionSet(UIStrings.Conditions, _gigCfg.CatchAllConditionSet, ConditionScope.Spearfishing, showAdvanced: true);
        }

        using (var tab = ImRaii.TabItem(UIStrings.Auto_Casts)) {
            if (tab) {
                var actionX = ImGui.GetCursorPosX();
                ImGui.SetCursorPosX(actionX);
                _gigCfg.Collect.DrawConfig();
                ImGui.SetCursorPosX(actionX);
                _gigCfg.ThaliaksFavor.DrawConfig();
                ImGui.SetCursorPosX(actionX);
                _gigCfg.Cordial.DrawConfigWithLabel("Cordials");
                ImGui.SetCursorPosX(actionX);
                _gigCfg.BaitedBreath.DrawConfig();
                ImGui.SetCursorPosX(actionX);
                _gigCfg.VitalSight.DrawConfig();
                ImGui.SetCursorPosX(actionX);
                _gigCfg.ElectricCurrent.DrawConfig();
            }
        }

        using (var tab = ImRaii.TabItem(UIStrings.ExtraOptions)) {
            if (tab) {
                ImGui.SetNextItemWidth(90.Scaled());
                if (ImGui.InputInt(UIStrings.GigHitbox, ref _gigCfg.GlobalHitboxSize)) {
                    _gigCfg.GlobalHitboxSize = Math.Max(0, Math.Min(_gigCfg.GlobalHitboxSize, 300));
                    Configuration.Save();
                }

                ImGui.Spacing();
                var x = ImGui.GetCursorPosX();
                _gigCfg.NatureBountyBeforeFishAction.DrawConfigWithLabel(UIStrings.UseNaturesBounty);
                ImGui.SetCursorPosX(x);
                _gigCfg.CatchAllNaturesBountyAction.DrawConfig();
                ImGui.SetCursorPosX(x);
                _gigCfg.CatchAllVeteranTradeAction.DrawConfig();
            }
        }
    }

    private void DrawRightPane() {
        DrawUtil.DrawAddNewPresetButton(_gigCfg);
        ImGui.SameLine(0, 3.Scaled());
        DrawUtil.DrawImportExport(_gigCfg, _viewingGlobal ? null : _displayed);
        ImGui.SameLine(0, 3.Scaled());
        DrawUtil.DrawDeletePresetButton(_gigCfg, _viewingGlobal ? null : _displayed);

        if (_displayed != null && _gigCfg.GetPreset(_displayed.UniqueId) == null) {
            _displayed = null;
            _viewingGlobal = true;
        }

        ImGui.Spacing();

        using var list = ImRaii.ListBox("###GigPresetList", ImGui.GetContentRegionAvail());
        if (!list)
            return;

        DrawCatchEverythingListItem();
        ImGui.Separator();

        foreach (var preset in _gigCfg.Presets.ToList()) {
            using var id = ImRaii.PushId(preset.UniqueId.ToString());
            var isActive = _gigCfg.AutoGigEnabled && _gigCfg.SelectedGuid == preset.UniqueId.ToString();
            var color = isActive ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudWhite;
            using (ImRaii.PushColor(ImGuiCol.Text, color)) {
                if (ImGui.Selectable((isActive ? "> " : "") + preset.PresetName, !_viewingGlobal && _displayed?.UniqueId == preset.UniqueId, ImGuiSelectableFlags.AllowDoubleClick)) {
                    _displayed = preset;
                    _viewingGlobal = false;

                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
                        if (isActive)
                            _gigCfg.SetCatchAll();
                        else
                            _gigCfg.SetActivePreset(preset);
                    }
                }
            }

            ImGui.TooltipOnHover(UIStrings.RightClickOptions);
            DrawUtil.DrawPresetContextMenu(_gigCfg, preset, isActive,
                toggleActive: () => {
                    if (isActive)
                        _gigCfg.SetCatchAll();
                    else
                        _gigCfg.SetActivePreset(preset);
                });
        }
    }

    private void DrawCatchEverythingListItem() {
        var globalActive = _gigCfg.IsCatchAllActive;
        var color = globalActive ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudWhite;
        using (ImRaii.PushColor(ImGuiCol.Text, color)) {
            if (ImGui.Selectable((globalActive ? "> " : "") + UIStrings.CatchEverything, _viewingGlobal, ImGuiSelectableFlags.AllowDoubleClick)) {
                _viewingGlobal = true;
                _displayed = null;

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
                    if (globalActive)
                        _gigCfg.SetDisabled();
                    else
                        _gigCfg.SetCatchAll();
                }
            }
        }
    }
}
