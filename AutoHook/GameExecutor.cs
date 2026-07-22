using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Task = System.Threading.Tasks.Task;

namespace AutoHook;

// fishing action executor: cast, use item, change bait/swimbait. block-casting/availability from WorldState.
public sealed class GameExecutor(WorldState ws) {
    private bool _blockActionNoDelay;

    public bool IsCastAvailable() => ws.IsCastAvailable();

    public unsafe bool CastAction(uint id, ActionType actionType = ActionType.Action)
        => ActionManager.Instance()->UseAction(actionType, id);

    public unsafe void UseItems(uint id)
        => AgentInventoryContext.Instance()->UseItem(id);

    public void CastActionDelayed(uint actionId, ActionType actionType = ActionType.Action, string actionName = "") {
        if (ws.BlockCasting) return;

        if (actionType is ActionType.Action or ActionType.EventAction) {
            if (!ws.ActionAvailable(actionId, actionType)) return;
            ws.Execute(new WorldState.OpSetBlockCasting(true));
            Service.PrintDebug($"[Executor] Casting Action: {actionName}, Id: {actionId}");
            try { CastAction(actionId, actionType); }
            catch (Exception e) { Service.PrintDebug($"[Executor] Error casting: {actionName}, Id: {actionId}, {e}"); }
            DelayNextCast();
        }
        else if (actionType == ActionType.Item) {
            ws.Execute(new WorldState.OpSetBlockCasting(true));
            Service.PrintDebug($"[Executor] Using Item: {actionName}, Id: {actionId}");
            try { UseItems(actionId); }
            catch (Exception e) { Service.PrintDebug($"[Executor] Error using item: {actionName}, Id: {actionId}, {e}"); }
            DelayNextCast();
        }
    }

    public void CastActionNoDelay(uint actionId, ActionType actionType = ActionType.Action, string actionName = "") {
        if (_blockActionNoDelay) return;
        _blockActionNoDelay = true;
        if (actionType is ActionType.Action or ActionType.EventAction && ws.ActionAvailable(actionId, actionType)) {
            var casted = CastAction(actionId, actionType);
            if (casted) Service.PrintDebug($"[Executor] Casting Action: {actionName}, Id: {actionId}");
        }
        else if (actionType == ActionType.Item) {
            Service.PrintDebug($"[Executor] Using Item: {actionName}, Id: {actionId}");
            UseItems(actionId);
        }
        _blockActionNoDelay = false;
    }

    public async void DelayNextCast() {
        var delay = 0;
        try {
            delay = new Random().Next(Service.Configuration.DelayBetweenCastsMin, Service.Configuration.DelayBetweenCastsMax);
        }
        catch (Exception e) {
            Svc.Log.Error($"[Executor] Error getting delay: {e}");
        }
        await Task.Delay(delay);
        ws.Execute(new WorldState.OpSetBlockCasting(false));
    }

    public ChangeBaitReturn ChangeBait(uint baitId) {
        if (baitId == ws.Fishing.BaitInfo.BaitId) return ChangeBaitReturn.AlreadyEquipped;
        if (baitId == 0 || GameRes.Baits.All(b => b.Id != baitId)) return ChangeBaitReturn.InvalidBait;
        if (ws.GetItemCount(baitId) <= 0) return ChangeBaitReturn.NotInInventory;
        return GameMain.ExecuteCommand(701, 4, (int)baitId, 0, 0) ? ChangeBaitReturn.Success : ChangeBaitReturn.UnknownError;
    }

    public ChangeBaitReturn ChangeSwimbait(uint index) {
        if (index > 2) return ChangeBaitReturn.InvalidBait;
        return GameMain.ExecuteCommand(701, 25, (int)index, 0, 0) ? ChangeBaitReturn.Success : ChangeBaitReturn.UnknownError;
    }

    public ChangeBaitReturn ChangeBait(BaitFishClass bait) {
        if (bait.Id == ws.Fishing.BaitInfo.BaitId) {
            Service.PrintChat($"Bait \"{bait.Name}\" is already equipped.");
            return ChangeBaitReturn.AlreadyEquipped;
        }
        if (bait.Id == 0 || GameRes.Baits.All(b => b.Id != bait.Id)) {
            Service.PrintChat($"Bait \"{bait.Name}\" is not a valid bait.");
            return ChangeBaitReturn.InvalidBait;
        }
        if (ws.GetItemCount((uint)bait.Id) <= 0) {
            Service.PrintChat($"Bait \"{bait.Name}\" is not in your inventory.");
            return ChangeBaitReturn.NotInInventory;
        }
        return GameMain.ExecuteCommand(701, 4, bait.Id, 0, 0) ? ChangeBaitReturn.Success : ChangeBaitReturn.UnknownError;
    }

    public enum ChangeBaitReturn {
        Success,
        AlreadyEquipped,
        NotInInventory,
        InvalidBait,
        UnknownError,
    }
}
