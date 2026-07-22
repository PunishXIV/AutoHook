using AutoHook.IPC;
using AutoHook.Ui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons.Automation.NeoTaskManager;
using System.Globalization;

namespace AutoHook;

public class Service {
    public static void Initialize(IDalamudPluginInterface pluginInterface)
        => pluginInterface.Create<Service>();

    public const string PluginName = "AutoHook";
    public const string GlobalPresetName = "Global Preset";

    public static WorldState WorldState { get; set; } = null!;
    // pushed each frame before fishing logic reads WorldState (framework tick).
    public static WorldStateUpdater WorldStateUpdater { get; set; } = null!;
    public static Configuration Configuration { get; set; } = null!;
    public static WindowSystem WindowSystem { get; } = new(PluginName);
    public static BaitFishClass LastCatch { get; set; } = new(@"-", -1);
    public static AutoCollectables AutoCollectables { get; set; } = null!;
    public static FishingManager FishManager { get; set; } = null!;
    public static AutoHookIPC Ipc { get; set; } = null!;
    public static NotificationMasterAPI.NotificationMasterApi NotificationMaster { get; set; } = null!;
    public static ReplayManager ReplayManager { get; set; } = null!;
    public static ReplayManagementWindow ReplayManagement { get; set; } = null!;
    public static FileDialogManager FileDialog { get; } = new();

    public static async ValueTask InitAsync() {
        unsafe {
            WorldState = new WorldState((ulong)FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->PerformanceCounterFrequency, Svc.Data.GameData.Repositories["ffxiv"].Version);
        }
        Configuration = await Configuration.LoadAsync();
        UIStrings.Culture = new CultureInfo(Configuration.CurrentLanguage);
        AutoCollectables = new AutoCollectables();
        NotificationMaster = new(Svc.Interface);
        WorldStateUpdater = new WorldStateUpdater();
        FishManager = new FishingManager();
        Ipc = new AutoHookIPC();
        ReplayManager = new ReplayManager();
    }

    public static async ValueTask DisposeAsync() {
        FishManager.Dispose();
        ReplayManager.Dispose();
        await Configuration.FlushAsync();
        WorldStateUpdater.Dispose();
        AutoCollectables.Dispose();
    }

    public static void Save() => Configuration.Save();

    public static string Status { get; set; } = @"";

    public static readonly TaskManager TaskManager = new() {
        DefaultConfiguration = { TimeLimitMS = 5000 }
    };

    private const int MaxLogSize = 50;
    public static Queue<string> LogMessages = new();
    public static bool OpenConsole;
    public static void PrintDebug(string msg) {
        if (LogMessages.Count >= MaxLogSize) {
            LogMessages.Dequeue();
        }

        LogMessages.Enqueue(msg);
        Svc.Log.Debug(msg);
    }

    public static void PrintVerbose(string msg) {
        if (LogMessages.Count >= MaxLogSize) {
            LogMessages.Dequeue();
        }

        LogMessages.Enqueue(msg);
        Svc.Log.Verbose(msg);
    }

    public static void PrintChat(string msg) {
        Status = msg;

        if (Configuration.ShowChatLogs)
            Svc.Chat.Print(msg);
    }
}

public static class NotificationMasterApiExtensions {
    extension(NotificationMasterAPI.NotificationMasterApi api) {
        public bool TryNotify(NotificationConfig cfg, string? fallbackText = null) {
            if (!cfg.Enabled)
                return false;

            var success = false;
            var chatMessage = ResolveMessage(cfg.ChatText, fallbackText);
            var gameToastMessage = ResolveMessage(cfg.GameToastText, fallbackText);
            var trayMessage = ResolveMessage(cfg.ToastText, fallbackText);

            try {
                if (cfg.EchoChatMessage && !string.IsNullOrWhiteSpace(chatMessage)) {
                    Svc.Chat.Print(new Dalamud.Game.Text.XivChatEntry() { Message = $"[AutoHook] {chatMessage}", Type = Dalamud.Game.Text.XivChatType.Echo });
                    success = true;
                }

                if (cfg.DisplayGameToast && !string.IsNullOrWhiteSpace(gameToastMessage)) {
                    Svc.Toasts.ShowQuest(gameToastMessage);
                    success = true;
                }

                if (Service.NotificationMaster.IsIPCReady()) {
                    if (cfg.DisplayToastNotification && !string.IsNullOrWhiteSpace(trayMessage) && Service.NotificationMaster.DisplayTrayNotification("AutoHook", trayMessage))
                        success = true;

                    if (cfg.FlashTaskbarIcon && Service.NotificationMaster.FlashTaskbarIcon())
                        success = true;

                    if (cfg.BringGameForeground && Service.NotificationMaster.TryBringGameForeground())
                        success = true;
                }
            }
            catch (Exception e) {
                Svc.Log.Warning($"[AutoHook] Notification failed: {e.Message}");
            }

            if (cfg.BeepOnSuccess) {
                const int frequency = 900;
                const int durationMs = 200;
                const int count = 3;

                for (var i = 0; i < count; i++) {
                    try {
                        Console.Beep(frequency, durationMs);
                    }
                    catch {
                        break;
                    }
                }

                success = true;
            }

            return success;
        }

        private static string ResolveMessage(string customText, string? fallbackText)
            => string.IsNullOrWhiteSpace(customText) ? fallbackText ?? "" : customText;
    }
}
