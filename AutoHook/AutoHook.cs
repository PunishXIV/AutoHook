using AutoHook.IPC;
using AutoHook.Spearfishing;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PunishLib;
using System.Globalization;

namespace AutoHook;

public class AutoHook : IDalamudPlugin
{
    public string Name => UIStrings.AutoHook;

    internal static AutoHook Plugin = null!;

    //todo: - Spearfishing rework
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
        { CmdAgPreset, UIStrings.Set_agpreset_command }
    };

    private static PluginUi _pluginUi = null!;

    private static AutoGig _autoGig = null!;

    public readonly FishingManager HookManager;

    public AutoHookIPC AutoHookIpc;

    public AutoHook(IDalamudPluginInterface pluginInterface, IDtrBar dtrBar)
    {
        ECommonsMain.Init(pluginInterface, this, Module.DalamudReflector, Module.ObjectFunctions);
        Service.Initialize(pluginInterface);
        PunishLibMain.Init(pluginInterface, "AutoHook",
            new AboutPlugin() { Developer = "InitialDet", Sponsor = "https://ko-fi.com/initialdet" });
        Plugin = this;
        Service.BaitManager = new BaitManager();
        Service.TugType = new SeTugType(Svc.SigScanner);
        Svc.PluginInterface.UiBuilder.Draw += Service.WindowSystem.Draw;
        Svc.PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
        Svc.PluginInterface.UiBuilder.OpenMainUi += OnOpenConfigUi;

        Service.Language = Svc.ClientState.ClientLanguage;

        GameRes.Initialize();

        Service.Configuration = Configuration.Load();
        UIStrings.Culture = new CultureInfo(Service.Configuration.CurrentLanguage);
        _pluginUi = new PluginUi();
        _autoGig = new AutoGig();

        foreach (var (command, help) in CommandHelp)
        {
            Svc.Commands.AddHandler(command, new CommandInfo(OnCommand)
            {
                HelpMessage = help
            });
        }

        HookManager = new FishingManager();
        AutoHookIpc = new AutoHookIPC();

        _ = new EzDtr2(() =>
            $"{((SeIconChar)0xE05E).ToIconString()} {(Service.Configuration.PluginEnabled ? UIStrings.Enabled : UIStrings.Disabled)}",
            evt =>
            {
                if (evt.ClickType is MouseClickType.Left)
                {
                    Service.Configuration.PluginEnabled ^= true;
                    Service.Configuration.Save();
                }
                else if (evt.ClickType is MouseClickType.Right)
                    _pluginUi.Toggle();
            },
            showCondition: () => Service.Configuration.DtrBarEnabled && Player.Job is ECommons.ExcelServices.Job.FSH
        );

        _ = new EzDtr2(() => $"{SeIconChar.Collectible.ToIconString()} {Service.Configuration.HookPresets.SelectedPreset?.PresetName ?? $"{UIStrings.GlobalPreset}"}",
            evt =>
            {
                if (Service.Configuration.HookPresets.SelectedPreset == null) return;
                var presets = Service.Configuration.HookPresets.CustomPresets;
                var index = presets.IndexOf(Service.Configuration.HookPresets.SelectedPreset);
                var direction = evt.ClickType == MouseClickType.Left ? 1 : -1;
                Service.Configuration.HookPresets.SelectedPreset = presets[(index + direction + presets.Count) % presets.Count];
                Service.Configuration.Save();
            },
            $"{Name}Presets",
            () => Service.Configuration.DtrPresetBarEnabled && Player.Job is ECommons.ExcelServices.Job.FSH && Service.Configuration.HookPresets.SelectedPreset != null
        );

#if (DEBUG)
    if (Svc.ClientState.IsLoggedIn)
            OnOpenConfigUi();
#endif
    }

    private void OnCommand(string command, string args)
    {
        switch (command.Trim())
        {
            case CmdAhCfg:
            case CmdAh:
                OnOpenConfigUi();
                break;
            case CmdAhOn:
                Svc.Chat.Print(UIStrings.AutoHook_Enabled);
                Service.Configuration.PluginEnabled = true;
                break;
            case CmdAhOff:
                Svc.Chat.Print(UIStrings.AutoHook_Disabled);
                Service.Configuration.PluginEnabled = false;
                break;
            case CmdAhtg when Service.Configuration.PluginEnabled:
                Svc.Chat.Print(UIStrings.AutoHook_Disabled);
                Service.Configuration.PluginEnabled = false;
                break;
            case CmdAhtg:
                Svc.Chat.Print(UIStrings.AutoHook_Enabled);
                Service.Configuration.PluginEnabled = true;
                break;
            case CmdAhPreset:
                SetPreset(args);
                break;
            case CmdAhStart:
                HookManager.StartFishing();
                break;
            case CmdBait:
            case CmdAhBait:
                SwapBait(args);
                break;
            case CmdAgPreset:
                SetGigPreset(args);
                break;
        }
    }

    private static void SwapBait(string args)
    {
        var bait = GameRes.Baits.FirstOrDefault(f => f.Name.ToLower() == args.ToLower() || f.Id.ToString() == args);
        Service.BaitManager.ChangeBait((uint)bait?.Id!);
    }

    private static void SetPreset(string presetName)
    {
        var preset = Service.Configuration.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == presetName);
        if (preset == null)
        {
            Svc.Chat.Print(UIStrings.Preset_not_found);
            return;
        }

        Service.Save();
        Service.Configuration.HookPresets.SelectedPreset = preset;
        Svc.Chat.Print(@$"{UIStrings.Preset_set_to_} {preset.PresetName}");
        Service.Save();
    }

    private static void SetGigPreset(string presetName)
    {
        try
        {
            var preset = Service.Configuration.AutoGigConfig.Presets.FirstOrDefault(x => x.PresetName == presetName);
            if (preset == null)
            {
                Svc.Chat.Print(@$"{UIStrings.Preset_not_found} - {presetName}");
                return;
            }

            Service.Save();
            Service.Configuration.AutoGigConfig.SelectedPreset = preset;
            Svc.Chat.Print(@$"{UIStrings.Gig_preset_set_to_} {preset.PresetName}");
            Service.Save();
        }
        catch (Exception e)
        {
            Svc.Log.Error(e.Message);
        }
    }

    public void Dispose()
    {
        _pluginUi.Dispose();
        _autoGig.Dispose();
        HookManager.Dispose();
        Service.Save();
        Svc.PluginInterface.UiBuilder.Draw -= Service.WindowSystem.Draw;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= OnOpenConfigUi;

        foreach (var (command, _) in CommandHelp)
            Svc.Commands.RemoveHandler(command);

        EzDtr2.DisposeAll();
        ECommonsMain.Dispose();
    }

    private static void OnOpenConfigUi() => _pluginUi.Toggle();
}
