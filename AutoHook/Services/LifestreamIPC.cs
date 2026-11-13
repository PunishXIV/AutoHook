using Dalamud.Plugin.Ipc;

namespace AutoHook.Services;

public sealed class LifestreamIPC : IPluginService {
    private readonly ICallGateSubscriber<bool> _isBusy;
    private readonly ICallGateSubscriber<int?, object> _enqueueInnShortcut;
    // type: home=1, fc=2, apartment=3; mode: enter house=2
    private readonly ICallGateSubscriber<int, int?, object> _enqueuePropertyShortcut;

    public LifestreamIPC() {
        _isBusy = Svc.Interface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        _enqueueInnShortcut = Svc.Interface.GetIpcSubscriber<int?, object>("Lifestream.EnqueueInnShortcut");
        _enqueuePropertyShortcut = Svc.Interface.GetIpcSubscriber<int, int?, object>("Lifestream.EnqueuePropertyShortcut");
    }

    public bool Available => _isBusy.HasFunction;

    public bool IsBusy => _isBusy.HasFunction && _isBusy.InvokeFunc();

    public bool EnqueueInn() {
        if (!_enqueueInnShortcut.HasAction)
            return false;
        _enqueueInnShortcut.InvokeAction(null);
        return true;
    }

    public bool EnqueueProperty(int type, bool enter = true) {
        if (!_enqueuePropertyShortcut.HasAction)
            return false;
        _enqueuePropertyShortcut.InvokeAction(type, enter ? 2 : null);
        return true;
    }
}
