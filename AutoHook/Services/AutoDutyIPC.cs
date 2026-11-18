using Dalamud.Plugin.Ipc;

namespace AutoHook.Services;

public sealed class AutoDutyIPC : IPluginService {
    private readonly ICallGateSubscriber<bool> _isLooping;
    private readonly ICallGateSubscriber<bool> _isStopped;

    public AutoDutyIPC() {
        _isLooping = Svc.Interface.GetIpcSubscriber<bool>("AutoDuty.IsLooping");
        _isStopped = Svc.Interface.GetIpcSubscriber<bool>("AutoDuty.IsStopped");
    }

    public bool IsBusy => _isLooping.HasFunction && _isLooping.InvokeFunc() || _isStopped.HasFunction && !_isStopped.InvokeFunc();
}
