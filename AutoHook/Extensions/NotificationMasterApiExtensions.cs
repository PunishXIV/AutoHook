namespace AutoHook.Extensions;

public static class NotificationMasterApiExtensions {
    extension(NotificationMasterAPI.NotificationMasterApi api) {
        public bool TryNotify(NotificationConfig cfg, string? fallbackText = null) {
            if (!cfg.Enabled)
                return false;

            var success = false;
            var chatMessage = ResolveMessage(cfg.ChatText, fallbackText);
            var gameToastMessage = ResolveMessage(cfg.GameToastText, fallbackText);
            var trayMessage = ResolveMessage(cfg.ToastText, fallbackText);
            var master = NotificationMasterService.Get().Api;

            try {
                if (cfg.EchoChatMessage && !string.IsNullOrWhiteSpace(chatMessage)) {
                    IChatGui.Get().Print(new Dalamud.Game.Text.XivChatEntry() { Message = $"[AutoHook] {chatMessage}", Type = Dalamud.Game.Text.XivChatType.Echo });
                    success = true;
                }

                if (cfg.DisplayGameToast && !string.IsNullOrWhiteSpace(gameToastMessage)) {
                    IToastGui.Get().ShowQuest(gameToastMessage);
                    success = true;
                }

                if (master.IsIPCReady()) {
                    if (cfg.DisplayToastNotification && !string.IsNullOrWhiteSpace(trayMessage) && master.DisplayTrayNotification("AutoHook", trayMessage))
                        success = true;

                    if (cfg.FlashTaskbarIcon && master.FlashTaskbarIcon())
                        success = true;

                    if (cfg.BringGameForeground && master.TryBringGameForeground())
                        success = true;
                }
            }
            catch (Exception e) {
                IPluginLog.Get().Warning($"[AutoHook] Notification failed: {e.Message}");
            }

            if (cfg.BeepOnSuccess) {
                _ = Task.Run(() => {
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
                });

                success = true;
            }

            return success;
        }

        private static string ResolveMessage(string customText, string? fallbackText)
            => string.IsNullOrWhiteSpace(customText) ? fallbackText ?? "" : customText;
    }
}
