using AutoHook.Spearfishing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace AutoHook.Ui;

public class TabSettings : BaseTab {
    public override string TabName => UIStrings.SettingsTab;
    public override bool Enabled { get; } = true;

    public override OpenWindow Type => OpenWindow.Settings;

    private readonly SpearFishingPresets _gigCfg = Configuration.C.AutoGigConfig;

    public override void DrawHeader() {
        DrawLanguageSelector();

        ImGui.Spacing();

        if (ImGui.Button(UIStrings.TabGeneral_DrawHeader_Localization_Help)) {
            Process.Start(new ProcessStartInfo { FileName = "https://crowdin.com/project/autohook", UseShellExecute = true });
        }
        ImGui.Spacing();
    }

    public override void Draw() {
        using var item = ImRaii.Child("SettingItems", new Vector2(0, 0), false);
        using var tabs = ImRaii.TabBar("###SettingsTabs", ImGuiTabBarFlags.NoTooltip);
        if (!tabs)
            return;

        using (var tab = ImRaii.TabItem("General")) {
            if (tab)
                DrawGeneralSettings();
        }

        using (var tab = ImRaii.TabItem("Fishing")) {
            if (tab)
                DrawFishingSettings();
        }

        using (var tab = ImRaii.TabItem("Spearfishing")) {
            if (tab)
                DrawSpearfishingSettings();
        }

        using (var tab = ImRaii.TabItem("Ocean Fishing")) {
            if (tab)
                DrawOceanFishingSettings();
        }
    }

    private void DrawGeneralSettings() {
        DrawUtil.Checkbox(UIStrings.Plugin_Enabled, ref Configuration.C.PluginEnabled, UIStrings.PluginEnabledHelp);
        DrawUtil.Checkbox(UIStrings.AntiAfkOption, ref Configuration.C.ResetAfkTimer);
        DrawUtil.Checkbox(UIStrings.Hide_Tab_Description, ref Configuration.C.HideTabDescription);
        DrawUtil.Checkbox(UIStrings.Show_Current_Status_Header, ref Configuration.C.ShowStatus);
        DrawUtil.Checkbox(UIStrings.Show_Chat_Logs, ref Configuration.C.ShowChatLogs, UIStrings.Show_Chat_Logs_HelpText);
        DrawUtil.Checkbox(UIStrings.Dtr_Show, ref Configuration.C.DtrBarEnabled, UIStrings.Dtr_Settings_Help_Text);
        DrawUtil.Checkbox(UIStrings.Dtr_Show_Preset, ref Configuration.C.DtrPresetBarEnabled, UIStrings.Dtr_Preset_Setting_Help);
        DrawUtil.TextV(UIStrings.Dtr_Help);
    }

    private void DrawFishingSettings() {
        DrawUtil.DrawTreeNodeEx(UIStrings.DelaySettings, () => {
            DrawDelayHook();
            DrawDelayCasts();
            DrawDelayCancel();
        });

        DrawUtil.Checkbox(UIStrings.AutoStartFishing, ref Configuration.C.AutoStartFishing, UIStrings.AutoStartFishingHelpText);
        DrawUtil.Checkbox(UIStrings.SpectralRestOnGain, ref Configuration.C.SpectralRest, UIStrings.SpectralRestOnGainHelpText);
        DrawUtil.Checkbox(UIStrings.AutoHandleCollectables, ref Configuration.C.AutoCollectablesEnabled, UIStrings.AutoHandleCollectablesHelpText);
    }

    private void DrawSpearfishingSettings() {
        DrawUtil.Checkbox(UIStrings.HideOverlayDuringSpearfishing, ref _gigCfg.AutoGigHideOverlay, UIStrings.AutoGigHideOverlayHelpMarker);
        DrawUtil.Checkbox(UIStrings.DrawFishHitbox, ref _gigCfg.AutoGigDrawFishHitbox);
        DrawUtil.Checkbox(UIStrings.DrawGigHitbox, ref _gigCfg.AutoGigDrawGigHitbox);
    }

    private void DrawOceanFishingSettings() {
        DrawUtil.Checkbox(UIStrings.AutoOceanFish, ref Configuration.C.AutoOceanFish, UIStrings.AutoOceanFishHelpText);
        if (!Configuration.C.AutoOceanFish)
            return;

        using (DrawUtil.BeginBranches(indent: true)) {
            DrawAutoOceanFishGoal();
            DrawUtil.Checkbox(UIStrings.AutoOceanFish_Fallthrough, ref Configuration.C.AOF_Fallthrough);
            DrawUtil.Checkbox(UIStrings.AutoOceanFish_AllowMovement, ref Configuration.C.AOF_WalkToRailing, UIStrings.AutoOceanFish_AllowMovementHelpText);
        }
    }

    private static void DrawAutoOceanFishGoal() {
        DrawUtil.Branch();
        ImGui.TextV($"{UIStrings.Prioritise}:");
        ImGui.SameLine();

        var goal = Configuration.C.AutoOceanFishGoal;

        if (ImGui.RadioButton(UIStrings.OceanFishGoal_Levelling, goal == OceanFishGoalKind.Levelling)) {
            Configuration.C.AutoOceanFishGoal = OceanFishGoalKind.Levelling;
            Configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton(UIStrings.OceanFishGoal_Achievements, goal == OceanFishGoalKind.Achievement)) {
            Configuration.C.AutoOceanFishGoal = OceanFishGoalKind.Achievement;
            Configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton(UIStrings.OceanFishGoal_Legendary, goal == OceanFishGoalKind.Legendary)) {
            Configuration.C.AutoOceanFishGoal = OceanFishGoalKind.Legendary;
            Configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton(UIStrings.OceanFishGoal_Points, goal == OceanFishGoalKind.Points)) {
            Configuration.C.AutoOceanFishGoal = OceanFishGoalKind.Points;
            Configuration.Save();
        }
    }

    private static void DrawDelayHook() {
        using var id = ImRaii.PushId("DrawDelayHook");
        DrawUtil.Branch();
        using var group = ImRaii.Group();

        ImGui.TextWrapped(UIStrings.Delay_when_hooking);

        ref var min = ref Configuration.C.DelayBetweenHookMin;
        ref var max = ref Configuration.C.DelayBetweenHookMax;

        ImGui.SetNextItemWidth(45.Scaled());
        if (ImGui.InputInt(UIStrings.DrawConfigs_Min_, ref min, 0)) {
            min = Math.Clamp(min, 0, max);
            Configuration.Save();
        }

        ImGui.SameLine();

        ImGui.SetNextItemWidth(45.Scaled());
        if (ImGui.InputInt(UIStrings.DrawConfigs_Max_, ref max, 0)) {
            max = Math.Clamp(max, min, 9999);
            Configuration.Save();
        }
    }

    private static void DrawDelayCasts() {
        using var id = ImRaii.PushId("DrawDelayCasts");
        DrawUtil.Branch();
        using var group = ImRaii.Group();

        ImGui.TextWrapped(UIStrings.Delay_Between_Casts);

        ref var min = ref Configuration.C.DelayBetweenCastsMin;
        ref var max = ref Configuration.C.DelayBetweenCastsMax;

        ImGui.SetNextItemWidth(45.Scaled());
        if (ImGui.InputInt(UIStrings.DrawConfigs_Min_, ref min, 0)) {
            min = Math.Clamp(min, 0, max);
            Configuration.Save();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(45.Scaled());
        if (ImGui.InputInt(UIStrings.DrawConfigs_Max_, ref max, 0)) {
            max = Math.Clamp(max, min, 9999);
            Configuration.Save();
        }
    }

    private static void DrawDelayCancel() {
        using var id = ImRaii.PushId("DrawDelayCancel");
        DrawUtil.Branch();
        using var group = ImRaii.Group();

        DrawUtil.TextV(UIStrings.DelayBeforeCancel);
        ImGui.SameLine();
        DrawUtil.Info(UIStrings.DelayBeforeCancelInfo);

        ref var min = ref Configuration.C.DelayBeforeCancelMin;
        ref var max = ref Configuration.C.DelayBeforeCancelMax;

        ImGui.SetNextItemWidth(45.Scaled());
        if (ImGui.InputInt(UIStrings.DrawConfigs_Min_, ref min, 0)) {
            min = Math.Clamp(min, 0, max);
            Configuration.Save();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(45.Scaled());
        if (ImGui.InputInt(UIStrings.DrawConfigs_Max_, ref max, 0)) {
            max = Math.Clamp(max, min, 9999);
            Configuration.Save();
        }
    }

    private void DrawLanguageSelector() {
        ImGui.SetNextItemWidth(55.Scaled());
        var languages = new List<string>
        {
            @"en",
            @"es",
            @"fr",
            @"de",
            @"ja",
            @"ko",
            @"ru",
            @"zh"
        };
        var currentLanguage = languages.IndexOf(Configuration.C.CurrentLanguage);

        if (!ImGui.Combo("Language###currentLanguage", ref currentLanguage, languages.ToArray(), languages.Count))
            return;

        Configuration.C.CurrentLanguage = languages[currentLanguage];
        UIStrings.Culture = new CultureInfo(Configuration.C.CurrentLanguage);
        Configuration.Save();
    }
}
