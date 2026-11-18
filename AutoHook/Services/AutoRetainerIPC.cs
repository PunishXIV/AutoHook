using Dalamud.Plugin.Ipc;

namespace AutoHook.Services;

// https://github.com/PunishXIV/AutoRetainer/blob/main/AutoRetainerAPI/AutoRetainerAPI/ApiConsts.cs
public sealed class AutoRetainerIPC : IPluginService, IDisposable {
    public const string PluginName = "AutoHook";

    private readonly ICallGateSubscriber<object> _onCharacterAdditionalTask;
    private readonly ICallGateSubscriber<string, object> _onCharacterReadyForPostprocess;
    private readonly ICallGateSubscriber<string, object> _requestCharacterPostprocess;
    private readonly ICallGateSubscriber<object> _finishCharacterPostprocess;
    private readonly ICallGateSubscriber<bool> _isBusy;
    private readonly ICallGateSubscriber<bool, object> _setSuppressed;
    private readonly ICallGateSubscriber<bool> _getMultiModeEnabled;
    private readonly ICallGateSubscriber<object> _enableMultiMode;
    private readonly ICallGateSubscriber<bool> _canAutoLogin;
    private readonly ICallGateSubscriber<string, bool> _relog;

    public event Action? CharacterAdditionalTask;
    public event Action? CharacterReadyForPostprocess;

    public AutoRetainerIPC() {
        _onCharacterAdditionalTask = Svc.Interface.GetIpcSubscriber<object>("AutoRetainer.OnCharacterAdditionalTask");
        _onCharacterReadyForPostprocess = Svc.Interface.GetIpcSubscriber<string, object>("AutoRetainer.OnCharacterReadyForPostprocess");
        _requestCharacterPostprocess = Svc.Interface.GetIpcSubscriber<string, object>("AutoRetainer.RequestCharacterPostprocess");
        _finishCharacterPostprocess = Svc.Interface.GetIpcSubscriber<object>("AutoRetainer.FinishCharacterPostprocessRequest");
        _isBusy = Svc.Interface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy");
        _setSuppressed = Svc.Interface.GetIpcSubscriber<bool, object>("AutoRetainer.SetSuppressed");
        _getMultiModeEnabled = Svc.Interface.GetIpcSubscriber<bool>("AutoRetainer.GetMultiModeEnabled");
        _enableMultiMode = Svc.Interface.GetIpcSubscriber<object>("AutoRetainer.PluginState.EnableMultiMode");
        _canAutoLogin = Svc.Interface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.CanAutoLogin");
        _relog = Svc.Interface.GetIpcSubscriber<string, bool>("AutoRetainer.PluginState.Relog");

        _onCharacterAdditionalTask.Subscribe(OnCharacterAdditionalTask);
        _onCharacterReadyForPostprocess.Subscribe(OnCharacterReadyForPostprocess);
    }

    public void Dispose() {
        _onCharacterAdditionalTask.Unsubscribe(OnCharacterAdditionalTask);
        _onCharacterReadyForPostprocess.Unsubscribe(OnCharacterReadyForPostprocess);
    }

    public bool Available => _requestCharacterPostprocess.HasAction;

    public bool IsBusy => _isBusy.HasFunction && _isBusy.InvokeFunc();

    public bool MultiModeEnabled => _getMultiModeEnabled.HasFunction && _getMultiModeEnabled.InvokeFunc();

    public bool CanAutoLogin => _canAutoLogin.HasFunction && _canAutoLogin.InvokeFunc();

    public void SetSuppressed(bool suppressed) {
        if (_setSuppressed.HasAction)
            _setSuppressed.InvokeAction(suppressed);
    }

    public void EnableMultiMode() {
        if (_enableMultiMode.HasAction)
            _enableMultiMode.InvokeAction();
    }

    public bool Relog(string character) => _relog.HasFunction && _relog.InvokeFunc(character);

    public void RequestCharacterPostprocess() {
        if (_requestCharacterPostprocess.HasAction)
            _requestCharacterPostprocess.InvokeAction(PluginName);
    }

    public void FinishCharacterPostprocess() {
        if (_finishCharacterPostprocess.HasAction)
            _finishCharacterPostprocess.InvokeAction();
    }

    private void OnCharacterAdditionalTask() {
        try {
            CharacterAdditionalTask?.Invoke();
        }
        catch (Exception e) {
            IPluginLog.Get().Error(e, "[AutoRetainerIPC] CharacterAdditionalTask handler failed");
        }
    }

    private void OnCharacterReadyForPostprocess(string plugin) {
        if (plugin != PluginName)
            return;
        try {
            CharacterReadyForPostprocess?.Invoke();
        }
        catch (Exception e) {
            IPluginLog.Get().Error(e, "[AutoRetainerIPC] CharacterReadyForPostprocess handler failed");
            FinishCharacterPostprocess();
        }
    }
}
