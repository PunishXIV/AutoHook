using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AutoHook.Presets.Config;

public record class NotificationConfig {
    public bool Enabled;
    public bool EchoChatMessage;
    public string ChatText = "";
    public bool DisplayGameToast;
    public string GameToastText = "";
    public bool DisplayToastNotification;
    public string ToastText = "";
    public bool FlashTaskbarIcon;
    public bool BringGameForeground;
    public bool BeepOnSuccess;

    public void DrawConfig(string fallbackText) {
        var hasPlugin = Svc.Interface.IsPluginLoaded("NotificationMaster");
        DrawUtil.DrawCheckboxTree("Notify On Success", ref Enabled,
            () => {
                DrawUtil.Checkbox("Play a beep", ref BeepOnSuccess);

                DrawUtil.Checkbox("Echo chat message", ref EchoChatMessage);
                if (EchoChatMessage)
                    DrawMessageInput("Chat message", ref ChatText, fallbackText);

                DrawUtil.Checkbox("Display in-game toast", ref DisplayGameToast);
                if (DisplayGameToast)
                    DrawMessageInput("Toast message", ref GameToastText, fallbackText);

                using var disabled = ImRaii.Disabled(!hasPlugin);
                DrawUtil.Checkbox("Display tray notification", ref DisplayToastNotification);
                if (DisplayToastNotification)
                    DrawMessageInput("Tray message", ref ToastText, fallbackText);

                DrawUtil.Checkbox("Flash taskbar icon", ref FlashTaskbarIcon);
                DrawUtil.Checkbox("Bring game to foreground", ref BringGameForeground);
            });
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !hasPlugin) {
            using var tooltip = ImRaii.Tooltip();
            if (tooltip.Alive) {
                ImGui.Text("NotificationMaster not installed. NotificationMaster-specific options below will have no effect");
            }
        }
    }

    private static void DrawMessageInput(string label, ref string field, string fallbackText) {
        using var branches = DrawUtil.BeginBranches(indent: true);
        DrawUtil.Branch();

        var text = field;
        ImGui.SetNextItemWidth(320.Scaled());
        ImGui.InputTextWithHint(label, fallbackText, ref text, 10_000);
        if (ImGui.IsItemEdited())
            field = text;
        if (ImGui.IsItemDeactivatedAfterEdit())
            Configuration.Save();
    }
}
