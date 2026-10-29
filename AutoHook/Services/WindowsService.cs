using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;

namespace AutoHook.Services;

public sealed class WindowsService : IPluginService, IDisposable {
    public WindowSystem WindowSystem { get; } = new(Svc.Interface.Manifest.Name);
    public FileDialogManager FileDialog { get; } = new();
    public ReplayManagementWindow ReplayManagement { get; } = new();
    public FishDataWindow FishData { get; } = new();

    public void Dispose() {
        ReplayManagement.Dispose();
        FishData.Dispose();
    }
}
