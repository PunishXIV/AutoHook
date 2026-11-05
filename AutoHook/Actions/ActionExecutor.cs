using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AutoHook.Actions;

public enum ActionDelayMode {
    Delayed,
    NoDelay,
}

public readonly record struct ActionRequest(uint Id, ActionType Type = ActionType.Action, string Name = "", ActionDelayMode DelayMode = ActionDelayMode.Delayed, int DelayBeforeMs = 0, bool UseRaw = false, bool StellarHookset = false, DecisionContext? DecisionContext = null);

public sealed class ActionExecutor : IPluginService, IDisposable {
    private readonly record struct QueueItem(ActionRequest? Request, Action? Callback, long ReadyAtMs);

    private readonly WorldState _ws = WorldState.Get();
    private readonly Random _rng = new();
    private readonly Queue<QueueItem> _queue = new();
    private long _unblockAtTickMs;

    public ActionExecutor() {
        IFramework.Get().Update += OnFrameworkUpdate;
    }

    public bool IsBusy => _queue.Count > 0 || _ws.Player.BlockCasting || _unblockAtTickMs != 0;

    public void Dispose() {
        IFramework.Get().Update -= OnFrameworkUpdate;
        _queue.Clear();
        _unblockAtTickMs = 0;
        if (_ws.Player.BlockCasting)
            _ws.Execute(new WorldState.OpSetBlockCasting(false));
    }

    private void OnFrameworkUpdate(IFramework _) => Update();

    public void Update() {
        if (_unblockAtTickMs != 0 && Environment.TickCount64 >= _unblockAtTickMs) {
            _unblockAtTickMs = 0;
            if (_ws.Player.BlockCasting)
                _ws.Execute(new WorldState.OpSetBlockCasting(false));
        }

        DrainQueue();
    }

    private void DrainQueue() {
        while (_queue.Count > 0) {
            var head = _queue.Peek();
            if (head.ReadyAtMs > Environment.TickCount64)
                return;

            if (head.Callback != null) {
                _queue.Dequeue();
                try {
                    head.Callback();
                }
                catch (Exception e) {
                    IPluginLog.Get().Error(e, "Error running ActionExecutor callback");
                }
                continue;
            }

            var request = head.Request!.Value;
            if (!request.UseRaw && request.DelayMode == ActionDelayMode.Delayed && _ws.Player.BlockCasting)
                return;

            if (!ExecuteRequest(request)) {
                if (!request.UseRaw && request.DelayMode == ActionDelayMode.Delayed && _ws.Player.BlockCasting)
                    return;
                _queue.Dequeue();
                continue;
            }

            _queue.Dequeue();
        }
    }

    public bool Enqueue(ActionRequest request, bool forceQueue = false) {
        if (request.DelayBeforeMs > 0 || forceQueue) {
            Push(request, Environment.TickCount64 + Math.Max(0, request.DelayBeforeMs));
            return true;
        }

        return ExecuteRequest(request);
    }

    public bool Enqueue(ActionRequest request, params ActionRequest[] followUps) {
        if (followUps.Length == 0)
            return Enqueue(request);

        var readyAt = Environment.TickCount64;
        readyAt += Math.Max(0, request.DelayBeforeMs);
        Push(request, readyAt);
        foreach (var followUp in followUps) {
            readyAt += Math.Max(0, followUp.DelayBeforeMs);
            Push(followUp, readyAt);
        }
        return true;
    }

    public void EnqueueCallback(Action callback, int delayMs = 0) {
        _queue.Enqueue(new QueueItem(null, callback, Environment.TickCount64 + Math.Max(0, delayMs)));
    }

    public void RemoveCallback(Action callback) {
        var kept = _queue.Where(item => item.Callback != callback).ToList();
        _queue.Clear();
        foreach (var item in kept)
            _queue.Enqueue(item);
    }

    private void Push(ActionRequest request, long readyAtMs)
        => _queue.Enqueue(new QueueItem(request, null, readyAtMs));

    public bool ExecuteRequest(ActionRequest request) {
        if (request.UseRaw) {
            try {
                return UseAction(request.Id, request.Type);
            }
            catch (Exception e) {
                IPluginLog.Get().Error(e, $"Error casting raw action: {request.Name}, Id: {request.Id}");
                return false;
            }
        }

        if (request.StellarHookset)
            return TryUseStellarHookset(string.IsNullOrEmpty(request.Name) ? "Stellar Hookset" : request.Name);

        return TryCast(request);
    }

    public bool TryCast(ActionRequest request)
        => request.DelayMode == ActionDelayMode.NoDelay ? TryCastNoDelay(request.Id, request.Type, request.Name) : TryCastDelayed(request.Id, request.Type, request.Name);

    public bool TryCastDelayed(uint actionId, ActionType actionType = ActionType.Action, string actionName = "") {
        if (_ws.Player.BlockCasting)
            return false;

        if (actionType is not (ActionType.Action or ActionType.EventAction or ActionType.Item))
            return false;

        if (!_ws.ActionAvailable(actionId, actionType))
            return false;

        _ws.Execute(new WorldState.OpSetBlockCasting(true));
        try {
            UseAction(actionId, actionType);
        }
        catch (Exception e) {
            IPluginLog.Get().Error(e, $"Error casting action: {actionName}, Id: {actionId}");
        }

        BeginPostCastDelay();
        return true;
    }

    public bool TryCastNoDelay(uint actionId, ActionType actionType = ActionType.Action, string actionName = "") {
        if (!_ws.ActionAvailable(actionId, actionType))
            return false;

        try {
            return UseAction(actionId, actionType);
        }
        catch (Exception e) {
            IPluginLog.Get().Error(e, $"Error casting action: {actionName}, Id: {actionId}");
            return false;
        }
    }

    public bool TryUseStellarHookset(string actionName = "Stellar Hookset")
        => _ws.GetAvailableStellarHooksetId() is { } actionId && TryCastDelayed(actionId, ActionType.Action, actionName);

    public unsafe bool UseAction(uint id, ActionType actionType = ActionType.Action) {
        if (actionType == ActionType.Item) {
            AgentInventoryContext.Instance()->UseItem(id);
            return true;
        }

        return ActionManager.Instance()->UseAction(actionType, id);
    }

    public void BeginPostCastDelay() {
        var delayMs = GetPostCastDelayMs();
        _unblockAtTickMs = Environment.TickCount64 + delayMs;
        if (!_ws.Player.BlockCasting)
            _ws.Execute(new WorldState.OpSetBlockCasting(true));
    }

    public int GetPostCastDelayMs() {
        try {
            var min = Configuration.C.DelayBetweenCastsMin;
            var max = Configuration.C.DelayBetweenCastsMax;
            if (max < min)
                (min, max) = (max, min);
            return _rng.Next(min, max + 1);
        }
        catch (Exception e) {
            IPluginLog.Get().Error(@$"Error getting delay between casts: {e}");
            return 0;
        }
    }

    public static uint GetActionCost(uint id, ActionType actionType = ActionType.Action)
        => (uint)ActionManager.GetActionCost(actionType, id, 0, 0, 0, 0);
}
