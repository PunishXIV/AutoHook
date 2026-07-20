using AutoHook.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using Lumina.Excel.Sheets;
using PunishLib.ImGuiMethods;
using System.ComponentModel;
using System.Numerics;
using System.Reflection;

namespace AutoHook;

public class PluginUi : Window, IDisposable {
    private static readonly List<BaseTab> _tabs =
    [
        new TabFishingPresets(),
        new TabAutoGig(),
        new TabCommunity(),
        new TabSettings()
    ];

    private readonly BaseTab debug = new TabDebug();

    private static OpenWindow _selectedTab = OpenWindow.FishingPreset;

    public PluginUi() : base($"{Service.PluginName} {Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? ""}###MainAutoHook") {
        Service.WindowSystem.AddWindow(this);

        Flags |= ImGuiWindowFlags.NoScrollbar;
        Flags |= ImGuiWindowFlags.NoScrollWithMouse;

        TitleBarButtons.Add(new() {
            Click = (m) => { Util.OpenLink(@"https://ko-fi.com/initialdet"); },
            Icon = FontAwesomeIcon.Heart,
            ShowTooltip = () => ImGui.SetTooltip("Support AutoHook"),
        });
    }

    public void Dispose() {
        Configuration.FlushAsync().GetAwaiter().GetResult();

        foreach (var tab in _tabs) {
            tab.Dispose();
        }

        Service.WindowSystem.RemoveWindow(this);
    }

    public override void Draw() {
        if (!IsOpen)
            return;

        try {
            DrawNewLayout();
        }
        catch (Exception e) {
            Svc.Log.Error(e, "[PluginUI] Draw failed.");
        }
    }
    private void Debug() {
        using var _ = ImRaii.PushId("debug");
        ImGui.SetNextItemWidth(300.Scaled());
        if (ImGui.Begin($"DebugWIndows", ref Service.OpenConsole)) {
            var logs = Service.LogMessages.AsEnumerable().Reverse().ToList();
            for (var i = 0; i < logs.Count; i++) {
                if (i == 0) {
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
    }

    private void DrawNewLayout() {
        var region = ImGui.GetContentRegionAvail();
        var topLeftSideHeight = region.Y;

        if (Service.Configuration.ShowStatus) {
            DrawStatus();
        }

        if (Service.OpenConsole)
            Debug();

        using (var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(5.Scaled(), 0))) {
            using var table = ImRaii.Table("###MainTable", 2, ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("##LeftColumn", ImGuiTableColumnFlags.WidthFixed, ImGui.GetWindowWidth() / 3);

            ImGui.TableNextColumn();

            var regionSize = ImGui.GetContentRegionAvail();
            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));

            using (var leftChild = ImRaii.Child($"###AhLeft", regionSize with { Y = topLeftSideHeight }, false, ImGuiWindowFlags.NoDecoration)) {
                if (ImGui.Selectable(UIStrings.StartActions))
                    Service.FishManager.StartFishing();

                using (var c = ImRaii.Child("logo", new(0, 125.Scaled()))) {
                    if (Svc.Texture.GetFromManifestResource(Assembly.GetExecutingAssembly(), $"AutoHook.Assets.Fishy{(Service.Configuration.PluginEnabled ? "" : "_g")}.png").TryGetWrap(out var image, out var _)) {
                        ImGuiEx.LineCentered("###AHLogo", () => {
                            ImGui.Image(image.Handle, new Vector2(125.Scaled(), 125.Scaled()));

                            if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                                if (ImGui.GetIO().KeyShift && Service.Configuration.PluginEnabled)
                                    Service.FishManager.RequestStopAfterNextFish();
                                else
                                    Service.Configuration.PluginEnabled = !Service.Configuration.PluginEnabled;
                            }

                            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                Service.OpenConsole = !Service.OpenConsole;

                            ImGui.TooltipOnHover(UIStrings.ClickToToggle);
                        });
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();

                foreach (var tab in _tabs) {
                    if (!tab.Enabled) continue;

                    if (ImGui.Selectable($"{tab.TabName}###{tab.TabName}Main", _selectedTab == tab.Type))
                        _selectedTab = tab.Type;
                }

#if DEBUG
                if (ImGui.Selectable($"{debug.TabName}###{debug.TabName}Main", _selectedTab == debug.Type))
                    _selectedTab = OpenWindow.Debug;
#endif

                if (ImGui.Selectable($"{UIStrings.AboutTab}"))
                    _selectedTab = OpenWindow.About;

                if (ImGui.Selectable($"{UIStrings.Changelog}"))
                    _openChangelog = !_openChangelog;
            }

            ImGui.PopStyleVar();

            ImGui.TableNextColumn();
            using var rightChild = ImRaii.Child($"###AhRight", Vector2.Zero, false);
            if (_selectedTab == OpenWindow.About)
                AboutTab.Draw("AutoHook");
            else if (_selectedTab == OpenWindow.Debug) {
                debug.DrawHeader();
                debug.Draw();
            }
            else {
                if (_tabs.FirstOrDefault(x => x.Type == _selectedTab) is { } tab) {
                    tab.DrawHeader();
                    tab.Draw();
                }
            }
        }

        if (_openChangelog)
            DrawChangelog();
    }

    private static void DrawStatus() {
        ImGuiEx.LineCentered("###AhStatus", () => {
            if (!Service.Configuration.PluginEnabled) {
                ImGui.TextColored(ImGuiColors.DalamudGrey, UIStrings.Plugin_Disabled);
            }
            else if (Service.WorldState.FishingState == FishingState.None) {
                try {
                    var preset = _presets.SelectedPreset;
                    if (preset == null) {
                        ImGui.TextColored(ImGuiColors.ParsedBlue,
                            UIStrings.StatusNoPreset);
                    }
                    else {
                        var baitId = Service.WorldState.Fishing.BaitInfo.BaitId;
                        var baitName = baitId == 0 ? UIStrings.None : Item.GetRow(baitId).Name.ToString();

                        var hasBait = preset != null && preset.HasBaitOrMooch(baitId);
                        var presetName = hasBait ? _presets.SelectedPreset?.PresetName : _presets.DefaultPreset.PresetName;
                        Service.Status = $"Equipped Bait: {baitName} - Preset \'{presetName}\' will be used.";

                        ImGui.TextColored(ImGuiColors.DalamudViolet, $"Equipped Bait:");
                        ImGui.SameLine(0, 3.Scaled());
                        ImGui.TextColored(ImGuiColors.ParsedGold, $"\'{baitName}\'");
                        ImGui.SameLine(0, 3.Scaled());
                        ImGui.TextColored(ImGuiColors.DalamudViolet, $"- Preset");
                        ImGui.SameLine(0, 3.Scaled());
                        ImGui.TextColored(ImGuiColors.ParsedGold, $"\'{presetName}\'");
                        ImGui.SameLine(0, 3.Scaled());
                        ImGui.TextColored(ImGuiColors.DalamudViolet, $"will be used.");
                    }
                }
                catch (Exception e) {
                    Svc.Log.Error(e, "[PluginUI] DrawStatus failed.");
                }
            }
            else
                ImGui.TextColored(ImGuiColors.DalamudViolet, Service.Status);
        });

        ImGui.Separator();
    }

    public override void OnClose() => Configuration.FlushAsync().GetAwaiter().GetResult();

    public static void ShowKofi() {
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, 0xFF000000 | 0x005E5BFF);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, 0xDD000000 | 0x005E5BFF);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xAA000000 | 0x005E5BFF);

        if (ImGui.Button("Ko-fi"))
            Util.OpenLink(@"https://ko-fi.com/initialdet");

        ImGui.PopStyleColor(3);
    }

    private bool _openChangelog = false;
    private static readonly FishingPresets _presets = Service.Configuration.HookPresets;

    [Localizable(false)]
    private void DrawChangelog() {
        var text = UIStrings.Changelog;
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ImGuiHelpers.GetButtonSize(text).X - 5.Scaled());

        ImGui.SetNextItemWidth(400.Scaled());
        if (ImGui.Begin($"{text}", ref _openChangelog)) {
            var changes = PluginChangelog.Versions;

            if (changes.Count > 0) {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                ImGui.TextWrapped($"{changes[0].VersionNumber}");
                ImGui.PopStyleColor();
                ImGui.Separator();

                //First value is the current Version
                foreach (var mainChange in changes[0].Main) {
                    ImGui.TextWrapped($"- {mainChange}");
                }

                ImGui.Spacing();

                if (changes[0].Minor.Count > 0) {
                    ImGui.TextWrapped("Minor Changes");
                    foreach (var minorChange in changes[0].Minor) {
                        ImGui.TextWrapped($"- {minorChange}");
                    }
                }

                ImGui.Separator();

                using var item = ImRaii.Child("###old_versions", new Vector2(0, 0), true);
                for (var i = 1; i < changes.Count; i++) {
                    if (!ImGui.TreeNode($"{changes[i].VersionNumber}"))
                        continue;

                    foreach (var mainChange in changes[i].Main)
                        ImGui.TextWrapped($"- {mainChange}");

                    if (changes[i].Minor.Count > 0) {
                        ImGui.Spacing();
                        ImGui.TextWrapped("Minor Changes");

                        foreach (var minorChange in changes[i].Minor)
                            ImGui.TextWrapped($"- {minorChange}");
                    }

                    ImGui.TreePop();
                }
            }
        }

        ImGui.End();
    }
}
