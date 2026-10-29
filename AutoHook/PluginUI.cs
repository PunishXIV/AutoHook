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
    public static string Status { get; set; } = "";

    private static readonly List<BaseTab> _tabs =
    [
        new TabFishingPresets(),
        new TabAutoGig(),
        new TabCommunity(),
        new TabOceanFishing(),
        new TabSettings()
    ];

    private readonly BaseTab debug = new TabDebug();
    private static OpenWindow _selectedTab = OpenWindow.FishingPreset;

    public PluginUi() : base($"{Svc.Interface.Manifest.Name} {Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? ""}###MainAutoHook") {
        WindowsService.Get().WindowSystem.AddWindow(this);

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

        WindowsService.Get().WindowSystem.RemoveWindow(this);
    }

    public override void Draw() {
        if (!IsOpen)
            return;

        try {
            DrawNewLayout();
        }
        catch (Exception e) {
            IPluginLog.Get().Error(e, "[PluginUI] Draw failed.");
        }
    }

    private void DrawNewLayout() {
        var region = ImGui.GetContentRegionAvail();
        var topLeftSideHeight = region.Y;

        if (Configuration.C.ShowStatus) {
            DrawStatus();
        }

        using (var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(5.Scaled(), 0))) {
            using var table = ImRaii.Table("###MainTable", 2, ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("##LeftColumn", ImGuiTableColumnFlags.WidthFixed, ImGui.GetWindowWidth() / 3);

            ImGui.TableNextColumn();

            var regionSize = ImGui.GetContentRegionAvail();
            using (ImRaii.PushStyle(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f)))
            using (var leftChild = ImRaii.Child($"###AhLeft", regionSize with { Y = topLeftSideHeight }, false, ImGuiWindowFlags.NoDecoration)) {
                if (ImGui.Selectable(UIStrings.StartActions))
                    FishingSessionManager.Get().StartFishing();

                using (var c = ImRaii.Child("logo", new(0, 125.Scaled()))) {
                    if (ITextureProvider.Get().GetFromManifestResource(Assembly.GetExecutingAssembly(), $"AutoHook.Assets.Fishy{(Configuration.C.PluginEnabled ? "" : "_g")}.png").TryGetWrap(out var image, out var _)) {
                        ImGuiEx.LineCentered("###AHLogo", () => {
                            ImGui.Image(image.Handle, new Vector2(125.Scaled(), 125.Scaled()));

                            if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                                if (ImGui.GetIO().KeyShift && Configuration.C.PluginEnabled)
                                    FishingSessionManager.Get().RequestStopAfterNextFish();
                                else
                                    Configuration.C.PluginEnabled = !Configuration.C.PluginEnabled;
                            }

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
            if (!Configuration.C.PluginEnabled) {
                ImGui.TextColored(ImGuiColors.DalamudGrey, UIStrings.Plugin_Disabled);
            }
            else if (WorldState.Get().Fishing.FishingState == FishingState.None) {
                try {
                    var preset = _presets.SelectedPreset;
                    if (preset == null) {
                        ImGui.TextColored(ImGuiColors.ParsedBlue,
                            UIStrings.StatusNoPreset);
                    }
                    else {
                        var baitId = WorldState.Get().Fishing.BaitInfo.BaitId;
                        var baitName = baitId == 0 ? UIStrings.None : Item.GetRow(baitId).Name.ToString();

                        var hasBait = preset != null && preset.HasBaitOrMooch(baitId);
                        var presetName = hasBait ? _presets.SelectedPreset?.PresetName : _presets.DefaultPreset.PresetName;
                        Status = $"Equipped Bait: {baitName} - Preset \'{presetName}\' will be used.";

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
                    IPluginLog.Get().Error(e, "[PluginUI] DrawStatus failed.");
                }
            }
            else
                ImGui.TextColored(ImGuiColors.DalamudViolet, Status);
        });

        ImGui.Separator();
    }

    public override void OnClose() => Configuration.FlushAsync().GetAwaiter().GetResult();

    public static void ShowKofi() {
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, 0xFF000000 | 0x005E5BFF))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, 0xDD000000 | 0x005E5BFF))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, 0xAA000000 | 0x005E5BFF)) {
            if (ImGui.Button("Ko-fi"))
                Util.OpenLink(@"https://ko-fi.com/initialdet");
        }
    }

    private bool _openChangelog = false;
    private static readonly FishingPresets _presets = Configuration.C.HookPresets;

    [Localizable(false)]
    private void DrawChangelog() {
        var text = UIStrings.Changelog;
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ImGuiHelpers.GetButtonSize(text).X - 5.Scaled());

        ImGui.SetNextItemWidth(400.Scaled());
        if (ImGui.Begin($"{text}", ref _openChangelog)) {
            var changes = PluginChangelog.Versions;

            if (changes.Count > 0) {
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
                    ImGui.TextWrapped($"{changes[0].VersionNumber}");
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
