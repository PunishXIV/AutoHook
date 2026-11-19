using Dalamud.Plugin.Ipc;

namespace AutoHook.Services;

public sealed class AllaganToolsIPC : IPluginService {
    private static readonly uint[] OwnedTypes = [
        0, 1, 2, 3, 2000, 2001,
        4000, 4001, 4100, 4101,
        10000, 10001, 10002, 10003, 10004, 10005, 10006, 12001,
    ];

    private readonly ICallGateSubscriber<bool> _isInitialized;
    private readonly ICallGateSubscriber<uint, bool, uint[], uint> _itemCountOwned;

    public AllaganToolsIPC() {
        _isInitialized = Svc.Interface.GetIpcSubscriber<bool>("AllaganTools.IsInitialized");
        _itemCountOwned = Svc.Interface.GetIpcSubscriber<uint, bool, uint[], uint>("AllaganTools.ItemCountOwned");
    }

    public bool Available => _isInitialized.HasFunction && _isInitialized.InvokeFunc() && _itemCountOwned.HasFunction;
    public int? OwnedCount(uint itemId) => !Available ? null : (int)_itemCountOwned.InvokeFunc(itemId, true, OwnedTypes);
}
