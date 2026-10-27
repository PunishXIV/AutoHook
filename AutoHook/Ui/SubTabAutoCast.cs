using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Common.Math;

namespace AutoHook.Ui;

public class SubTabAutoCast {
    private static readonly List<BaseActionCast> _actionsAvailable = [with(14)];

    private static CustomPresetConfig? _lastPreset;
    private static CustomPresetConfig _preset = null!;

    public static void DrawAutoCastTab(CustomPresetConfig presetCfg) {
        _preset = presetCfg;
        var acCfg = _preset.AutoCastsCfg;

        EnsureActions(acCfg);

        DrawHeader(acCfg);
        DrawBody(acCfg);
    }

    private static void EnsureActions(AutoCastsConfig acCfg) {
        if (_lastPreset == _preset && _actionsAvailable.Count > 0)
            return;

        _lastPreset = _preset;
        _actionsAvailable.Clear();
        _actionsAvailable.AddRange([
            acCfg.CastLine,
            acCfg.CastMooch,
            acCfg.CastChum,
            acCfg.CastCollect,
            acCfg.CastSnagging,
            acCfg.CastCordial,
            acCfg.CastFishEyes,
            acCfg.CastMakeShiftBait,
            acCfg.CastPatience,
            acCfg.CastPrizeCatch,
            acCfg.CastThaliaksFavor,
            acCfg.CastBigGame,
            acCfg.CastMultihook,
        ]);
        _actionsAvailable.Sort(CompareActions);
    }

    private static int CompareActions(BaseActionCast a, BaseActionCast b) {
        int CompareByType(Type type) {
            var aMatch = a.GetType() == type;
            var bMatch = b.GetType() == type;
            if (aMatch == bMatch)
                return 0;
            return aMatch.CompareTo(bMatch);
        }

        var c = CompareByType(typeof(AutoCastLine));
        if (c != 0) return c;
        c = CompareByType(typeof(AutoMooch));
        if (c != 0) return c;
        c = CompareByType(typeof(AutoCollect));
        if (c != 0) return c;
        c = CompareByType(typeof(AutoSnagging));
        if (c != 0) return c;
        c = CompareByType(typeof(AutoMultiHook));
        if (c != 0) return c;
        return a.Priority.CompareTo(b.Priority);
    }

    private static void DrawHeader(AutoCastsConfig acCfg) {
        ImGui.Spacing();

        DrawUtil.Checkbox(UIStrings.EnableActions, ref acCfg.EnableAll, UIStrings.Acton_Alert_Manual_Hook);

        ImGui.SameLine();

        if (DrawUtil.Checkbox(UIStrings.Dont_Cancel_Mooch, ref acCfg.DontCancelMooch,
                UIStrings.TabAutoCasts_DrawHeader_HelpText)) {
            foreach (var action in _actionsAvailable.Where(action => action != null))
                action.DontCancelMooch = acCfg.DontCancelMooch;
        }

        if (!_preset.IsGlobal) {
            if (!acCfg.EnableAll)
                ImGui.TextColored(ImGuiColors.ParsedBlue, UIStrings.AllActionsDisabled);
        }
        else {
            if (!acCfg.EnableAll)
                ImGui.TextColored(ImGuiColors.ParsedBlue, UIStrings.SubAuto_Disabled);
        }

        DrawUtil.SpacingSeparator();
    }

    private static void DrawBody(AutoCastsConfig acCfg) {
        DrawUtil.DrawTreeNodeEx(UIStrings.AnimationCanceling, () => {
            DrawUtil.Checkbox(UIStrings.EnableRecastCancel, ref acCfg.RecastAnimationCancel,
                UIStrings.EnableRecastCancelHelp);
            if (acCfg.RecastAnimationCancel)
                DrawUtil.SubCheckbox(UIStrings.TurnCollectOff,
                    ref acCfg.TurnCollectOff,
                    UIStrings.TurnCollectOffHelp);

            DrawUtil.Checkbox(UIStrings.EnableChumCancel, ref acCfg.ChumAnimationCancel,
                UIStrings.ChumCancelHelp);
        });

        DrawUtil.Checkbox(UIStrings.TurnCollectOffWithoutAnimCancel, ref acCfg.TurnCollectOffWithoutAnimCancel,
            UIStrings.TurnCollectOffWithoutAnimCancelHelp);

        var (enabled, start, end) = acCfg.TimeWindow.Value;
        var enabledLocal = enabled;
        var startTime = start.ToString(@"HH:mm");
        var endTime = end.ToString(@"HH:mm");
        DrawUtil.DrawCheckboxTree(UIStrings.AutoCastOnlyAtSpecificTimes, ref enabledLocal, () => {
            DrawUtil.Branch();
            using (ImRaii.ItemWidth(40.Scaled())) {
                var startTimeGui = ImGui.InputText(@$"{UIStrings.AutoCastStartTime}", ref startTime, 5,
                    ImGuiInputTextFlags.EnterReturnsTrue);
                if (startTimeGui && TimeOnly.TryParse(startTime, out var newStartTime)) {
                    acCfg.TimeWindow.Value = (true, newStartTime, end);
                    Configuration.Save();
                }
            }

            DrawUtil.Branch();
            using (ImRaii.ItemWidth(40.Scaled())) {
                var endTimeGui = ImGui.InputText(@$"{UIStrings.AutoCastEndTime}", ref endTime, 5,
                    ImGuiInputTextFlags.EnterReturnsTrue);
                if (endTimeGui && TimeOnly.TryParse(endTime, out var newEndTime)) {
                    acCfg.TimeWindow.Value = (true, start, newEndTime);
                    Configuration.Save();
                }
            }
        }, UIStrings.SpecificTimeWindowHelpText);
        if (enabledLocal != enabled) {
            acCfg.TimeWindow.Value = (enabledLocal, start, end);
            Configuration.Save();
        }

        ImGui.TextColored(ImGuiColors.DalamudOrange, UIStrings.Auto_Cast_Sort_Notice);

        _actionsAvailable.Sort(CompareActions);

        using var item = ImRaii.Child("###AutoCastItems", new Vector2(0, 0), false);
        foreach (var action in _actionsAvailable) {
            try {
                using var id = ImRaii.PushId(action.GetType().ToString());
                action.DrawConfig(_actionsAvailable);
            }
            catch (Exception e) {
                IPluginLog.Get().Error(e.ToString());
            }
        }
    }
}
