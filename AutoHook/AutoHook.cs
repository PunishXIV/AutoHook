using AutoHook.Spearfishing;
using clib;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Plugin;
using ECommons.EzDTR;
using PunishLib;
using System.Globalization;
using System.Threading;

namespace AutoHook;

/*
 * TODO:
 * stop movement while fishing
 * auto extract materia
 * move around to reduce fish weary
 */

public class AutoHook(IDalamudPluginInterface pluginInterface) : IAsyncDalamudPlugin {
    public const string GlobalPresetName = "Global Preset";

    private const string CmdAhCfg = "/ahcfg";
    private const string CmdAh = "/autohook";
    private const string CmdAhOn = "/ahon";
    private const string CmdAhOff = "/ahoff";
    private const string CmdAhtg = "/ahtg";
    private const string CmdAhPreset = "/ahpreset";
    private const string CmdAhStart = "/ahstart";
    private const string CmdAhBait = "/ahbait";
    private const string CmdBait = "/bait";
    private const string CmdAgPreset = "/agpreset";
    private const string CmdAhReplay = "/ahreplay";
    private const string CmdAhFish = "/ahfish";

    private static readonly Dictionary<string, string> CommandHelp = new()
    {
        { CmdAhOff, UIStrings.Disables_AutoHook },
        { CmdAhOn, UIStrings.Enables_AutoHook },
        { CmdAhCfg, UIStrings.Opens_Config_Window },
        { CmdAh, UIStrings.Opens_Config_Window },
        { CmdAhtg, UIStrings.Toggles_AutoHook_On_Off },
        { CmdAhPreset, UIStrings.Set_preset_command },
        { CmdAhStart, UIStrings.Starts_AutoHook },
        { CmdAhBait, UIStrings.SwitchFishBait },
        { CmdBait, UIStrings.SwitchFishBait },
        { CmdAgPreset, UIStrings.Set_agpreset_command },
        { CmdAhReplay, UIStrings.Opens_Replay_Window },
        { CmdAhFish, UIStrings.Opens_Fish_Data_Window }
    };

    private static PluginUi _pluginUi = null!;
    private static AutoGig _autoGig = null!;

    public async Task LoadAsync(CancellationToken cancellationToken) {
        ECommonsMain.Init(pluginInterface, this, Module.DalamudReflector, Module.ObjectFunctions);
        CLibMain.Init(pluginInterface, this, CLibModule.Automation);
        PunishLibMain.Init(pluginInterface, "AutoHook", new AboutPlugin() { Developer = "InitialDet & croizat", Sponsor = "https://ko-fi.com/initialdet" });

        Configuration.C.Initiate();
        Configuration.StartSaving();
        UIStrings.Culture = new CultureInfo(Configuration.C.CurrentLanguage);

        _pluginUi = new PluginUi();
        _autoGig = new AutoGig();

        foreach (var (command, help) in CommandHelp) {
            ICommandManager.Get().AddHandler(command, new CommandInfo(OnCommand) {
                HelpMessage = help
            });
        }

        await FishBaitCatalog.Get().InitializeAsync(cancellationToken);
        await FishSpotCatalog.Get().InitializeAsync(cancellationToken);

        Svc.Interface.UiBuilder.Draw += DrawUi;
        Svc.Interface.UiBuilder.OpenConfigUi += _pluginUi.Toggle;
        Svc.Interface.UiBuilder.OpenMainUi += _pluginUi.Toggle;

        SetupDtr();

#if DEBUG
        if (IClientState.Get().IsLoggedIn)
            _pluginUi.Toggle();
#endif
    }

    public async ValueTask DisposeAsync() {
        Configuration.StopSaving();
        await Configuration.FlushAsync(rebuildAll: true);
        _pluginUi.Dispose();
        _autoGig.Dispose();
        Svc.Interface.UiBuilder.Draw -= DrawUi;
        Svc.Interface.UiBuilder.OpenConfigUi -= _pluginUi.Toggle;
        Svc.Interface.UiBuilder.OpenMainUi -= _pluginUi.Toggle;

        foreach (var (command, _) in CommandHelp)
            ICommandManager.Get().RemoveHandler(command);

        ECommonsMain.Dispose();
        await CLibMain.DisposeAsync();
    }

    private void OnCommand(string command, string args) {
        switch (command.Trim()) {
            case CmdAhCfg:
            case CmdAh:
                _pluginUi.Toggle();
                break;
            case CmdAhOn:
                IChatGui.Get().Print(UIStrings.AutoHook_Enabled);
                Configuration.C.PluginEnabled = true;
                break;
            case CmdAhOff:
                IChatGui.Get().Print(UIStrings.AutoHook_Disabled);
                Configuration.C.PluginEnabled = false;
                break;
            case CmdAhtg when Configuration.C.PluginEnabled:
                IChatGui.Get().Print(UIStrings.AutoHook_Disabled);
                Configuration.C.PluginEnabled = false;
                break;
            case CmdAhtg:
                IChatGui.Get().Print(UIStrings.AutoHook_Enabled);
                Configuration.C.PluginEnabled = true;
                break;
            case CmdAhPreset:
                SetPreset(args);
                break;
            case CmdAhStart:
                FishingSessionManager.Get().StartFishing();
                break;
            case CmdBait:
            case CmdAhBait:
                SwapBait(args);
                break;
            case CmdAgPreset:
                SetGigPreset(args);
                WindowsService.Get().ReplayManagement.Toggle();
                break;
            case CmdAhReplay:
                WindowsService.Get().ReplayManagement.Toggle();
                break;
            case CmdAhFish:
                OpenFishData(args);
                break;
        }
    }

    private static void OpenFishData(string args) {
        var window = WindowsService.Get().FishData;
        var query = args.Trim();
        if (query.Length == 0) {
            window.Toggle();
            return;
        }

        var fish = FishBaitCatalog.Get().Fishes.FirstOrDefault(f => f.Id.ToString() == query || f.Name.Equals(query, StringComparison.CurrentCultureIgnoreCase))
                   ?? FishBaitCatalog.Get().Fishes.FirstOrDefault(f => f.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        if (fish == null) {
            IChatGui.Get().PrintError($"No fish found matching \"{query}\"");
            return;
        }
        window.Open(fish.Id);
    }

    private static void SwapBait(string args) {
        var bait = FishBaitCatalog.Get().Baits.FirstOrDefault(f => f.Name.Equals(args, StringComparison.CurrentCultureIgnoreCase) || f.Id.ToString() == args);
        BaitComponent.ChangeBait((uint)bait?.Id!);
    }

    private static void SetPreset(string presetName) {
        var preset = Configuration.C.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == presetName);
        if (preset == null) {
            IChatGui.Get().Print(UIStrings.Preset_not_found);
            return;
        }

        Configuration.C.HookPresets.Select(preset, FishingPresets.ReasonManual);
        IChatGui.Get().Print(@$"{UIStrings.Preset_set_to_} {preset.PresetName}");
        Configuration.FlushAsync().GetAwaiter().GetResult();
    }

    private static void SetGigPreset(string presetName) {
        try {
            var preset = Configuration.C.AutoGigConfig.Presets.FirstOrDefault(x => x.PresetName == presetName);
            if (preset == null) {
                IChatGui.Get().Print(@$"{UIStrings.Preset_not_found} - {presetName}");
                return;
            }

            Configuration.C.AutoGigConfig.SetActivePreset(preset);
            IChatGui.Get().Print(@$"{UIStrings.Gig_preset_set_to_} {preset.PresetName}");
            Configuration.FlushAsync().GetAwaiter().GetResult();
        }
        catch (Exception e) {
            IPluginLog.Get().Error(e, "[AutoHook] SetGigPreset failed.");
        }
    }

    private static void DrawUi() {
        var windows = WindowsService.Get();
        windows.WindowSystem.Draw();
        windows.FileDialog.Draw();
        OceanFishingSpotOverlay.Draw();
    }

    private void SetupDtr() {
        _ = new EzDtr(() => $"{((SeIconChar)0xE05E).ToIconString()} {(Configuration.C.PluginEnabled ? UIStrings.Enabled : UIStrings.Disabled)}",
                evt => {
                    if (evt.ClickType is MouseClickType.Left) {
                        Configuration.C.PluginEnabled ^= true;
                        Configuration.Save();
                    }
                    else if (evt.ClickType is MouseClickType.Right)
                        _pluginUi.Toggle();
                },
                showCondition: () => Configuration.C.DtrBarEnabled && Player.Job is ECommons.ExcelServices.Job.FSH
            );

        _ = new EzDtr(() => $"{SeIconChar.Collectible.ToIconString()} {Configuration.C.HookPresets.SelectedPreset?.PresetName ?? $"{UIStrings.GlobalPreset}"}",
            evt => {
                if (Configuration.C.HookPresets.SelectedPreset == null) return;
                var presets = Configuration.C.HookPresets.CustomPresets;
                var index = presets.IndexOf(Configuration.C.HookPresets.SelectedPreset);
                var direction = evt.ClickType == MouseClickType.Left ? 1 : -1;
                Configuration.C.HookPresets.SelectedPreset = presets[(index + direction + presets.Count) % presets.Count];
                Configuration.Save();
            },
            $"{Svc.Interface.Manifest.Name}Presets",
            () => Configuration.C.DtrPresetBarEnabled && Player.Job is ECommons.ExcelServices.Job.FSH && Configuration.C.HookPresets.SelectedPreset != null
        );
    }
}
