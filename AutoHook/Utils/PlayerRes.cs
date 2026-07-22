using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Task = System.Threading.Tasks.Task;

namespace AutoHook.Utils;

// cast/item/delay helpers. block-casting from WorldState.
public static class PlayerRes {
    private static WorldState WS => Service.WorldState;

    public static unsafe bool IsInActiveSpectralCurrent() {
        if (FFXIVClientStructs.FFXIV.Client.Game.Event.EventFramework.Instance()->GetInstanceContentOceanFishing() is null)
            return false;
        return FFXIVClientStructs.FFXIV.Client.Game.Event.EventFramework.Instance()->GetInstanceContentOceanFishing()->SpectralCurrentActive;
    }

    public static unsafe uint ActionStatus(uint id, ActionType actionType = ActionType.Action)
        => ActionManager.Instance()->GetActionStatus(actionType, id);

    public static unsafe bool CastAction(uint id, ActionType actionType = ActionType.Action)
        => ActionManager.Instance()->UseAction(actionType, id);

    public static unsafe int GetRecastGroups(uint id, ActionType actionType = ActionType.Action)
        => ActionManager.Instance()->GetRecastGroup((int)actionType, id);

    public static unsafe void UseItems(uint id)
        => AgentInventoryContext.Instance()->UseItem(id);

    public static uint CastActionCost(uint id, ActionType actionType = ActionType.Action)
        => (uint)ActionManager.GetActionCost(actionType, id, 0, 0, 0, 0);

    public static bool ActionOnCoolDown(uint id, ActionType actionType = ActionType.Action)
        => WS.ActionOnCooldown(id, actionType);

    public static float GetCooldown(uint id, ActionType actionType) {
        var remaining = WS.GetCooldownRemaining(id, actionType);
        return remaining <= 0f ? 0f : remaining;
    }

    public static bool CastActionDelayed(uint actionId, ActionType actionType = ActionType.Action, string actionName = "") {
        if (WS.BlockCasting)
            return false;

        if (actionType is ActionType.Action or ActionType.EventAction) {
            if (!WS.ActionAvailable(actionId, actionType))
                return false;

            WS.Execute(new WorldState.OpSetBlockCasting(true));
            Service.PrintDebug(@$"[PlayerResources] Casting Action: {actionName}, Id: {actionId}");
            try { CastAction(actionId, actionType); }
            catch (Exception e) { Service.PrintDebug(@$"Error casting action: {actionName}, Id: {actionId}, {e}"); }

            DelayNextCast();
            return true;
        }

        if (actionType != ActionType.Item)
            return false;

        if (!WS.ActionAvailable(actionId, actionType))
            return false;

        WS.Execute(new WorldState.OpSetBlockCasting(true));
        Service.PrintDebug(@$"[PlayerResources] Using Item: {actionName}, Id: {actionId}");
        try { UseItems(actionId); }
        catch (Exception e) { Service.PrintDebug(@$"Error casting action: {actionName}, Id: {actionId}, {e}"); }
        DelayNextCast();
        return true;
    }

    // true if delayed cast started (block set + post-cast delay scheduled).
    public static bool TryUseStellarHookset(string actionName = "Stellar Hookset") {
        if (WS.GetAvailableStellarHooksetId() is not { } actionId)
            return false;

        return TryCastActionDelayed(actionId, ActionType.Action, actionName);
    }

    public static bool TryCastActionDelayed(uint actionId, ActionType actionType = ActionType.Action, string actionName = "")
        => CastActionDelayed(actionId, actionType, actionName);

    private static bool _blockActionNoDelay;

    public static void CastActionNoDelay(uint actionId, ActionType actionType = ActionType.Action, string actionName = "")
        => TryCastActionNoDelay(actionId, actionType, actionName);

    public static bool TryCastActionNoDelay(uint actionId, ActionType actionType = ActionType.Action, string actionName = "") {
        if (_blockActionNoDelay) return false;
        _blockActionNoDelay = true;
        var casted = false;
        if (actionType is ActionType.Action or ActionType.EventAction && WS.ActionAvailable(actionId, actionType)) {
            casted = CastAction(actionId, actionType);
            if (casted) Service.PrintDebug(@$"[PlayerResources] Casting Action: {actionName}, Id: {actionId}");
        }
        else if (actionType == ActionType.Item && WS.ActionAvailable(actionId, actionType)) {
            Service.PrintDebug(@$"[PlayerResources] Using Item: {actionName}, Id: {actionId}");
            UseItems(actionId);
            casted = true;
        }
        _blockActionNoDelay = false;
        return casted;
    }

    public static async void DelayNextCast() {
        await Task.Delay(GetPostCastDelayMs());
        WS.Execute(new WorldState.OpSetBlockCasting(false));
    }

    // delay after delayed cast/item before next action (same as DelayNextCast).
    public static int GetPostCastDelayMs() {
        try { return new Random().Next(Service.Configuration.DelayBetweenCastsMin, Service.Configuration.DelayBetweenCastsMax); }
        catch (Exception e) {
            Svc.Log.Error(@$"Error getting delay between casts: {e}");
            return 0;
        }
    }
}
