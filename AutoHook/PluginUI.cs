using AutoHook.Ui;
using AutoHook.Utils;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using PunishLib.ImGuiMethods;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using AutoHook.Enums;
using AutoHook.Fishing;
using AutoHook.Resources.Localization;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ThreadLoadImageHandler = ECommons.ImGuiMethods.ThreadLoadImageHandler;

namespace AutoHook;

public class PluginUi : Window, IDisposable
{
    private static readonly List<BaseTab> _tabs = new()
    {
        new TabFishingPresets(),
        new TabAutoGig(),
        new TabCommunity(),
        new TabSettings()
    };

    private BaseTab debug = new TabDebug();

    private static OpenWindow _selectedTab = OpenWindow.FishingPreset;

    public PluginUi() : base(
        $"{Service.PluginName} {Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? ""}###MainAutoHook")
    {
        Service.WindowSystem.AddWindow(this);

        Flags |= ImGuiWindowFlags.NoScrollbar;
        Flags |= ImGuiWindowFlags.NoScrollWithMouse;

        TitleBarButtons.Add(new()
        {
            Click = (m) => { OpenBrowser(@"https://ko-fi.com/initialdet"); },
            Icon = FontAwesomeIcon.Heart,
            ShowTooltip = () => ImGui.SetTooltip("Support AutoHook"),
        });
    }

    public void Dispose()
    {
        Service.Save();

        foreach (var tab in _tabs)
        {
            tab.Dispose();
        }

        Service.WindowSystem.RemoveWindow(this);
    }

    public override void Draw()
    {
        if (!IsOpen)
            return;

        try
        {
            DrawNewLayout();
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(e.Message);
        }

        //DrawOldLayout()
    }

    private void DrawOldLayout()
    {
        DrawUtil.Info(UIStrings.StartActionHelpText);

        ImGui.SameLine(0, 3);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Play, UIStrings.StartActions))
            AutoHook.Plugin.HookManager.StartFishing();

        ImGui.SameLine();

        DrawUtil.Checkbox("###PluginEnable", ref Service.Configuration.PluginEnabled);

        ImGui.SameLine(0, 1);

        if (Service.Configuration.PluginEnabled)
            ImGui.TextColored(ImGuiColors.HealerGreen, UIStrings.Plugin_Enabled);
        else
            ImGui.TextColored(ImGuiColors.DalamudRed, UIStrings.Plugin_Disabled);

        ImGui.SameLine();

        DrawChangelog();

        /*if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Start using your Auto Casts!\n\nYou can also use the command /ahstart to start fishing based on your Auto Cast settings. Try making a macro with it!");*/

        if (Service.Configuration.ShowDebugConsole)
        {
            ImGui.Spacing();
            if (ImGui.Button(UIStrings.Open_Console))
                Service.OpenConsole = !Service.OpenConsole;
#if DEBUG
            ImGui.SameLine();
            TestButtons();
#endif
            if (Service.OpenConsole)
                Debug();
        }

        if (Service.Configuration.ShowStatus)
        {
            if (string.IsNullOrEmpty(Service.Status))
            {
                ImGui.Dummy(new Vector2(ImGui.GetFontSize()));
            }
            else
            {
                ImGui.TextColored(ImGuiColors.DalamudViolet, Service.Status);
            }
        }

        ImGui.Spacing();
        DrawTabs();
    }

    private void Debug()
    {
        ImGui.PushID(@"debug");
        ImGui.SetNextItemWidth(300);
        if (ImGui.Begin($"DebugWIndows", ref Service.OpenConsole))
        {
            var logs = Service.LogMessages.ToArray().Reverse().ToList();
            for (var i = 0; i < logs.Count; i++)
            {
                if (i == 0)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                    ImGui.TextWrapped($"{i + 1} - {logs[i]}");
                    ImGui.PopStyleColor();
                }
                else
                    ImGui.TextWrapped($"{i + 1} - {logs[i]}");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }
        }

        ImGui.End();
        ImGui.PopID();
    }

    private void DrawNewLayout()
    {
        var region = ImGui.GetContentRegionAvail();
        var topLeftSideHeight = region.Y;

        if (Service.Configuration.ShowStatus)
        {
            DrawStatus();
        }
        
        if (Service.OpenConsole)
            Debug();

        using (var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(5, 0)))
        {
            using (var table = ImRaii.Table("###MainTable", 2, ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("##LeftColumn", ImGuiTableColumnFlags.WidthFixed, ImGui.GetWindowWidth() / 3);

                ImGui.TableNextColumn();

                var regionSize = ImGui.GetContentRegionAvail();
                ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));

                using (var leftChild = ImRaii.Child($"###AhLeft", regionSize with { Y = topLeftSideHeight },
                           false, ImGuiWindowFlags.NoDecoration))
                {
                    if (ImGui.Selectable($"Start Actions"))
                        AutoHook.Plugin.HookManager.StartFishing();
                    
                    var image = Service.Configuration.PluginEnabled ? "images/Fishy.png" : "images/Fishy_g.png";
                    var imagePath = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName!, image);
                    using (var c = ImRaii.Child("logo", new(0, 125f.Scale())))
                    {
                        if (ThreadLoadImageHandler.TryGetTextureWrap(imagePath, out var logo))
                        {
                            ImGuiEx.LineCentered("###AHLogo", () =>
                            {
                                ImGui.Image(logo.ImGuiHandle, new(125f.Scale(), 125f.Scale()));

                                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                                    Service.Configuration.PluginEnabled = !Service.Configuration.PluginEnabled;

                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                    Service.OpenConsole = !Service.OpenConsole;
                                

                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.BeginTooltip();
                                    ImGui.Text($"Click to disable the plugin");
                                    ImGui.EndTooltip();
                                }
                            });
                        }
                    }

                    ImGui.Spacing();
                    ImGui.Separator();

                    foreach (var tab in _tabs)
                    {
                        if (tab.Enabled == false) continue;

                        if (ImGui.Selectable($"{tab.TabName}###{tab.TabName}Main", _selectedTab == tab.Type))
                        {
                            _selectedTab = tab.Type;
                        }
                    }

                    if (ImGui.Selectable($"{debug.TabName}###{debug.TabName}Main",
                            _selectedTab == debug.Type))
                    {
                        _selectedTab = OpenWindow.Debug;
                    }

                    if (ImGui.Selectable($"{UIStrings.AboutTab}", _selectedTab == null))
                    {
                        _selectedTab = OpenWindow.About;
                    }

                    if (ImGui.Selectable($"{UIStrings.Changelog}", _selectedTab == null))
                    {
                        _openChangelog = !_openChangelog;
                    }
                }

                ImGui.PopStyleVar();

                ImGui.TableNextColumn();
                using (var rightChild = ImRaii.Child($"###AhRight", Vector2.Zero, false))
                {
                    if (_selectedTab == OpenWindow.About)
                        AboutTab.Draw("AutoHook");
                    else if (_selectedTab == OpenWindow.Debug)
                    {
                        debug.DrawHeader();
                        debug.Draw();
                    }
                    else
                    {
                        var tab = _tabs.FirstOrDefault(x => x.Type == _selectedTab);
                        if (tab != null)
                        {
                            tab.DrawHeader();
                            tab.Draw();
                        }
                    }
                }
            }
        }

        if (_openChangelog)
            DrawChangelog();
    }

    private static void DrawStatus()
    {
        ImGuiEx.LineCentered("###AhStatus", () =>
        {
            if (Service.BaitManager.FishingState == FishingState.NotFishing)
            {
                try
                {
                    var preset = _presets.SelectedPreset;
                    var baitId = Service.BaitManager.CurrentBaitSwimBait;
                    var baitName = MultiString.GetItemName(baitId);

                    var hasBait = preset != null && preset.HasBaitOrMooch(baitId);
                    var presetName = hasBait ? _presets.SelectedPreset?.PresetName : _presets.DefaultPreset.PresetName;
                    Service.Status = $"Equipped Bait: {baitName} - Preset \'{presetName}\' will be used.";
                }
                catch (Exception e)
                {
                    Service.PluginLog.Error(e.Message);
                }
            }

            ImGui.TextColored(ImGuiColors.DalamudViolet, Service.Status);
        });

        ImGui.Separator();
    }

    private void DrawTabs()
    {
        try
        {
            if (ImGui.BeginTabBar(@"AutoHook###TabBars", ImGuiTabBarFlags.NoTooltip))
            {
                foreach (var tab in _tabs)
                {
                    if (tab.Enabled == false) continue;

                    if (ImGui.BeginTabItem($"{tab.TabName}###{tab.TabName}Main"))
                    {
                        ImGui.PushID($"{tab.TabName}MainId");
                        tab.DrawHeader();
                        tab.Draw();
                        ImGui.PopID();
                        ImGui.EndTabItem();
                    }
                }

                if (ImGui.BeginTabItem(UIStrings.AboutTab))
                {
                    AboutTab.Draw("AutoHook");
                    ImGui.EndTabItem();
                }
#if DEBUG
                if (ImGui.BeginTabItem("Debug"))
                {
                    debug.DrawHeader();
                    debug.Draw();
                    ImGui.EndTabItem();
                }
#endif
                ImGui.EndTabBar();
            }
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(e.Message);
            ImGui.EndTabBar();
        }
    }

    public override void OnClose()
    {
        Service.Save();
    }

    public static void ShowKofi()
    {
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, 0xFF000000 | 0x005E5BFF);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, 0xDD000000 | 0x005E5BFF);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xAA000000 | 0x005E5BFF);

        if (ImGui.Button("Ko-fi"))
        {
            OpenBrowser(@"https://ko-fi.com/initialdet");
        }

        ImGui.PopStyleColor(3);
    }


    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private bool _openChangelog = false;
    private static FishingPresets _presets = Service.Configuration.HookPresets;

    [Localizable(false)]
    private void DrawChangelog()
    {
        var text = UIStrings.Changelog;
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ImGuiHelpers.GetButtonSize(text).X - 5);

        ImGui.SetNextItemWidth(400);
        if (ImGui.Begin($"{text}", ref _openChangelog))
        {
            var changes = PluginChangelog.Versions;

            if (changes.Count > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                ImGui.TextWrapped($"{changes[0].VersionNumber}");
                ImGui.PopStyleColor();
                ImGui.Separator();

                //First value is the current Version
                foreach (var mainChange in changes[0].Main)
                {
                    ImGui.TextWrapped($"- {mainChange}");
                }

                ImGui.Spacing();

                if (changes[0].Minor.Count > 0)
                {
                    ImGui.TextWrapped("Minor Changes");
                    foreach (var minorChange in changes[0].Minor)
                    {
                        ImGui.TextWrapped($"- {minorChange}");
                    }
                }

                ImGui.Separator();

                if (ImGui.BeginChild("old_versions", new Vector2(0, 0), true))
                {
                    for (var i = 1; i < changes.Count; i++)
                    {
                        if (!ImGui.TreeNode($"{changes[i].VersionNumber}"))
                            continue;

                        foreach (var mainChange in changes[i].Main)
                            ImGui.TextWrapped($"- {mainChange}");

                        if (changes[i].Minor.Count > 0)
                        {
                            ImGui.Spacing();
                            ImGui.TextWrapped("Minor Changes");

                            foreach (var minorChange in changes[i].Minor)
                                ImGui.TextWrapped($"- {minorChange}");
                        }

                        ImGui.TreePop();
                    }
                }

                ImGui.EndChild();
            }
        }

        ImGui.End();
    }

    private static unsafe void TestButtons()
    {
        try
        {
            if (ImGui.Button("Check"))
            {
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}