﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Common.Math;
using ImGuiNET;

namespace AutoHook.Ui;

public class TabConfigGuides : BaseTab
{
    public override string TabName { get; } = UIStrings.SettingsTab;
    public override bool Enabled { get; } = true;

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
        if (ImGui.BeginChild("SettingItems", new Vector2(0, 0), true))
        {
            DrawConfigs();
            ImGui.EndChild();
        }
    }

    private void DrawConfigs()
    {
        if (ImGui.TreeNodeEx(UIStrings.DelaySettings, ImGuiTreeNodeFlags.FramePadding))
        {
            DrawDelayHook();
            DrawDelayCasts();
            ImGui.TreePop();
        }

        ImGui.Separator();
        
        DrawUtil.Checkbox(UIStrings.AntiAfkOption, ref Service.Configuration.ResetAfkTimer);

        DrawUtil.Checkbox(UIStrings.DontHideExtraAutoCast, ref Service.Configuration.DontHideOptionsDisabled);

        DrawUtil.Checkbox(UIStrings.Hide_Tab_Description, ref Service.Configuration.HideTabDescription);

        DrawUtil.Checkbox(UIStrings.Show_Current_Status_Header, ref Service.Configuration.ShowStatusHeader);

        DrawUtil.Checkbox(UIStrings.Show_Chat_Logs, ref Service.Configuration.ShowChatLogs, UIStrings.Show_Chat_Logs_HelpText);

        DrawUtil.Checkbox(UIStrings.Show_Debug_Console, ref Service.Configuration.ShowDebugConsole);

        DrawUtil.Checkbox(UIStrings.Show_Presets_As_Sidebar, ref Service.Configuration.ShowPresetsAsSidebar);
        
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

        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Min_, ref Service.Configuration.DelayBetweenHookMin, 0))
        {
            Service.Configuration.DelayBetweenHookMin =
                Math.Max(0, Math.Min(Service.Configuration.DelayBetweenHookMin, 9999));
            Service.Save();
        }

        ImGui.SameLine();

        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Max_, ref Service.Configuration.DelayBetweenHookMax, 0))
        {
            Service.Configuration.DelayBetweenHookMax =
                Math.Max(0, Math.Min(Service.Configuration.DelayBetweenHookMax, 9999));

            Service.Save();
        }

        ImGui.PopID();
    }

    private static void DrawDelayCasts()
    {
        ImGui.PushID("DrawDelayCasts");

        ImGui.TextWrapped(UIStrings.Delay_Between_Casts);

        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Min_, ref Service.Configuration.DelayBetweenCastsMin, 0))
        {
            Service.Configuration.DelayBetweenCastsMin =
                Math.Max(0, Math.Min(Service.Configuration.DelayBetweenCastsMin, 9999));
            Service.Save();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Max_, ref Service.Configuration.DelayBetweenCastsMax, 0))
        {
            Service.Configuration.DelayBetweenCastsMax =
                Math.Max(0, Math.Min(Service.Configuration.DelayBetweenCastsMax, 9999));
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