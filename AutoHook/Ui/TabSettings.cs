using System.Diagnostics;
using System.Globalization;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Common.Math;
using Dalamud.Bindings.ImGui;

namespace AutoHook.Ui;

public class TabSettings : BaseTab
{
    public override string TabName => UIStrings.SettingsTab;
    public override bool Enabled { get; } = true;

    public override OpenWindow Type => OpenWindow.Settings;

    public override void DrawHeader()
    {
        DrawLanguageSelector();

        ImGui.Spacing();

        if (ImGui.Button(UIStrings.TabGeneral_DrawHeader_Localization_Help))
        {
            Process.Start(new ProcessStartInfo
            { FileName = "https://crowdin.com/project/autohook", UseShellExecute = true });
        }

        ImGui.Spacing();

        if (ImGui.Button(UIStrings.TabAutoCasts_DrawHeader_Guide_Collectables))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/PunishXIV/AutoHook/blob/main/AcceptCollectable.md",
                UseShellExecute = true
            });
        }

        ImGui.Spacing();
    }

    public override void Draw()
    {
        using (var item = ImRaii.Child("SettingItems", new Vector2(0, 0), true))
        {
            DrawConfigs();
        }
    }

    private void DrawConfigs()
    {
        DrawUtil.Checkbox(UIStrings.Plugin_Enabled, ref Service.Configuration.PluginEnabled, UIStrings.PluginEnabledHelp);

        if (ImGui.TreeNodeEx(UIStrings.DelaySettings, ImGuiTreeNodeFlags.FramePadding))
        {
            DrawDelayHook();
            DrawDelayCasts();
            DrawDelayCancel();
            ImGui.TreePop();
        }

        ImGui.Separator();

        DrawUtil.Checkbox(UIStrings.AntiAfkOption, ref Service.Configuration.ResetAfkTimer);

        DrawUtil.Checkbox(UIStrings.AutoStartFishing, ref Service.Configuration.AutoStartFishing, UIStrings.AutoStartFishingHelpText);

        DrawUtil.Checkbox(UIStrings.DontHideExtraAutoCast, ref Service.Configuration.DontHideOptionsDisabled);

        DrawUtil.Checkbox(UIStrings.Hide_Tab_Description, ref Service.Configuration.HideTabDescription);

        DrawUtil.Checkbox(UIStrings.Show_Current_Status_Header, ref Service.Configuration.ShowStatus);

        DrawUtil.Checkbox(UIStrings.Show_Chat_Logs, ref Service.Configuration.ShowChatLogs, UIStrings.Show_Chat_Logs_HelpText);

        //DrawUtil.Checkbox(UIStrings.Show_Debug_Console, ref Service.Configuration.ShowDebugConsole);

        //DrawUtil.Checkbox(UIStrings.Show_Presets_As_Sidebar, ref Service.Configuration.ShowPresetsAsSidebar);

        DrawUtil.DrawCheckboxTree(UIStrings.SwapTreeNodeButtons, ref Service.Configuration.SwapToButtons, () =>
        {
            if (ImGui.RadioButton(UIStrings.Type_1, Service.Configuration.SwapType == 0))
            {
                Service.Configuration.SwapType = 0;
                Service.Save();
            }

            if (ImGui.RadioButton(UIStrings.Type_2, Service.Configuration.SwapType == 1))
            {
                Service.Configuration.SwapType = 1;
                Service.Save();
            }

            ImGui.Text("Hello, you're cute!");
        });
    }

    private static void DrawDelayHook()
    {
        ImGui.PushID("DrawDelayHook");

        ImGui.TextWrapped(UIStrings.Delay_when_hooking);

        ref var min = ref Service.Configuration.DelayBetweenHookMin;
        ref var max = ref Service.Configuration.DelayBetweenHookMax;

        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Min_, ref min, 0))
        {
            min = Math.Clamp(min, 0, max);
            Service.Save();
        }

        ImGui.SameLine();

        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Max_, ref max, 0))
        {
            max = Math.Clamp(max, min, 9999);
            Service.Save();
        }

        ImGui.PopID();
    }

    private static void DrawDelayCasts()
    {
        ImGui.PushID("DrawDelayCasts");

        ImGui.TextWrapped(UIStrings.Delay_Between_Casts);

        ref var min = ref Service.Configuration.DelayBetweenCastsMin;
        ref var max = ref Service.Configuration.DelayBetweenCastsMax;

        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Min_, ref min, 0))
        {
            min = Math.Clamp(min, 0, max);
            Service.Save();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Max_, ref max, 0))
        {
            max = Math.Clamp(max, min, 9999);
            Service.Save();
        }

        ImGui.PopID();
    }

    private static void DrawDelayCancel()
    {
        ImGui.PushID("DrawDelayCancel");

        DrawUtil.TextV(UIStrings.DelayBeforeCancel);
        ImGui.SameLine();
        DrawUtil.Info(UIStrings.DelayBeforeCancelInfo);

        ref var min = ref Service.Configuration.DelayBeforeCancelMin;
        ref var max = ref Service.Configuration.DelayBeforeCancelMax;

        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Min_, ref min, 0))
        {
            min = Math.Clamp(min, 0, max);
            Service.Save();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Max_, ref max, 0))
        {
            max = Math.Clamp(max, min, 9999);
            Service.Save();
        }

        ImGui.PopID();
    }

    private void DrawLanguageSelector()
    {
        ImGui.SetNextItemWidth(55);
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
        var currentLanguage = languages.IndexOf(Service.Configuration.CurrentLanguage);

        if (!ImGui.Combo("Language###currentLanguage", ref currentLanguage, languages.ToArray(), languages.Count))
            return;

        Service.Configuration.CurrentLanguage = languages[currentLanguage];
        UIStrings.Culture = new CultureInfo(Service.Configuration.CurrentLanguage);
        Service.Save();
        //Service.Chat.Print("Saved");
    }
}