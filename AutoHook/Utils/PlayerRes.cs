using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Task = System.Threading.Tasks.Task;

namespace AutoHook.Utils;

public static class PlayerRes
{
    public static bool IsMoochAvailable()
    {
        if (ActionTypeAvailable(IDs.Actions.Mooch))
            return true;

        if (ActionTypeAvailable(IDs.Actions.Mooch2))
            return true;

        return false;
    }

    public static bool HasStatus(uint statusID)
    {
        if (Svc.Objects.LocalPlayer?.StatusList == null)
            return false;

        foreach (var buff in Svc.Objects.LocalPlayer.StatusList)
        {
            if (buff.StatusId == statusID)
                return true;
        }

        return false;
    }

    public static bool HasAnyStatus(uint[] statusIDs)
    {
        if (Svc.Objects.LocalPlayer?.StatusList == null)
            return false;

        return Svc.Objects.LocalPlayer.StatusList.Any(buff => statusIDs.Contains(buff.StatusId));
    }

    public static unsafe bool IsInActiveSpectralCurrent()
    {
        if (EventFramework.Instance()->GetInstanceContentOceanFishing() is null)
            return false;

        return EventFramework.Instance()->GetInstanceContentOceanFishing()->SpectralCurrentActive;
    }

    public static uint GetCurrentGp()
    {
        if (Svc.Objects.LocalPlayer?.CurrentGp == null)
            return 0;

        return Svc.Objects.LocalPlayer.CurrentGp;
    }

    public static uint GetMaxGp()
    {
        if (Svc.Objects.LocalPlayer?.MaxGp == null)
            return 0;

        return Svc.Objects.LocalPlayer.MaxGp;
    }

    public static int GetStatusStacks(uint status)
    {
        if (Svc.Objects.LocalPlayer?.StatusList == null)
            return 0;

        foreach (var buff in Svc.Objects.LocalPlayer.StatusList)
        {
            if (buff.StatusId == status)
                return buff.Param;
        }

        return 0;
    }

    public static bool HasAnglersArtStacks(int amount)
    {
        if (Svc.Objects.LocalPlayer?.StatusList == null)
            return false;

        foreach (var buff in Svc.Objects.LocalPlayer.StatusList)
        {
            if (buff.StatusId == IDs.Status.AnglersArt)
                return buff.Param >= amount;
        }

        return false;
    }

    public static float GetStatusTime(uint statusId)
    {
        if (Svc.Objects.LocalPlayer?.StatusList == null)
            return 0;

        foreach (var buff in Svc.Objects.LocalPlayer.StatusList)
        {
            if (buff.StatusId == statusId)
                return buff.RemainingTime;
        }

        return 0;
    }

    // status 0 == available to cast? not sure but it seems to be
    // Also make sure its the skill is not on cooldown (mainly for mooch2)
    public static unsafe bool ActionTypeAvailable(uint id, ActionType actionType = ActionType.Action)
    {
        return ActionStatus(id, actionType) == 0 && !ActionOnCoolDown(id, actionType);
    }

    public static unsafe bool IsCastAvailable()
    {
        return ActionStatus(IDs.Actions.Cast) == 0 && !ActionOnCoolDown(IDs.Actions.Cast) && !_blockCasting;
    }

    public static unsafe bool HasMultihookAvailable()
    {
        if (DutyActionManager.GetInstanceIfReady() is not null and var dm)
        {
            for (var i = 0; i < dm->NumValidSlots; i++)
            {
                if (dm->ActionId[i] is IDs.Actions.MultiHook && dm->CurCharges[i] > 0)
                    return true;
            }
        }
        return false;
    }

    public static unsafe bool ActionOnCoolDown(uint id, ActionType actionType = ActionType.Action)
    {
        var group = GetRecastGroups(id, actionType);

        if (group == -1) // Im assuming -1 recast group has no CD
            return false;

        var recastDetail = ActionManager.Instance()->GetRecastGroupDetail(group);

        return recastDetail->Total - recastDetail->Elapsed > 0;
    }

    public static unsafe uint ActionStatus(uint id, ActionType actionType = ActionType.Action)
    {
        return ActionManager.Instance()->GetActionStatus(actionType, id);
    }

    public static unsafe bool CastAction(uint id)
    {
        return ActionManager.Instance()->UseAction(ActionType.Action, id);
    }

    public static unsafe int GetRecastGroups(uint id, ActionType actionType = ActionType.Action)
    {
        return ActionManager.Instance()->GetRecastGroup((int)actionType, id);
    }

    public static unsafe int HasItem(uint itemId)
        => InventoryManager.Instance()->GetInventoryItemCount(itemId);

    public static unsafe void UseItems(uint id)
    {
        AgentInventoryContext.Instance()->UseItem(id);
    }

    // RecastGroup 68 = Cordial pots
    public static unsafe bool IsPotOffCooldown()
    {
        var recast = ActionManager.Instance()->GetRecastGroupDetail(68);
        return recast->Total - recast->Elapsed == 0;
    }

    public static unsafe uint CastActionCost(uint id, ActionType actionType = ActionType.Action)
    {
        return (uint)ActionManager.GetActionCost(actionType, id, 0, 0, 0, 0);
    }

    public static unsafe float GetCooldown(uint id, ActionType actionType)
    {
        var group = GetRecastGroups(id, actionType);

        if (group == -1) // Im assuming -1 recast group has no CD
            return 0;

        var recast = ActionManager.Instance()->GetRecastGroupDetail(group);

        return recast->Total - recast->Elapsed;
    }

    public static unsafe bool HaveItemInInventory(uint id, bool isHQ = false)
        => InventoryManager.Instance()->GetInventoryItemCount(id, isHQ) > 0;

    public static unsafe bool HaveCordialInInventory(uint id)
    {
        return InventoryManager.Instance()->GetInventoryItemCount(id) > 0;
    }

    private static bool _blockCasting = false;

    public static void CastActionDelayed(uint actionId, ActionType actionType = ActionType.Action,
        string actionName = "")
    {
        if (_blockCasting)
            return;

        if (actionType is ActionType.Action or ActionType.EventAction)
        {
            if (!ActionTypeAvailable(actionId, actionType))
                return;

            _blockCasting = true;
            Service.PrintDebug(@$"[PlayerResources] Casting Action: {actionName}, Id: {actionId}");
            try
            {
                CastAction(actionId);
            }
            catch (Exception e)
            {
                Service.PrintDebug(@$"Error casting action: {actionName}, Id: {actionId}, {e}");
            }

            DelayNextCast(actionId);
        }
        else if (actionType == ActionType.Item)
        {
            _blockCasting = true;
            Service.PrintDebug(@$"[PlayerResources] Using Item: {actionName}, Id: {actionId}");
            try
            {
                UseItems(actionId);
            }
            catch (Exception e)
            {
                Service.PrintDebug(@$"Error casting action: {actionName}, Id: {actionId}, {e}");
            }

            DelayNextCast(actionId);
        }
    }

    private static bool _blockActionNoDelay = false;

    public static void CastActionNoDelay(uint actionId, ActionType actionType = ActionType.Action,
        string actionName = "")
    {
        // sometimes it tries to cast the same action while, this prevents that
        if (_blockActionNoDelay)
            return;

        _blockActionNoDelay = true;
        if (actionType == ActionType.Action)
        {
            if (ActionTypeAvailable(actionId, actionType))
            {
                var casted = CastAction(actionId);
                if (casted)
                    Service.PrintDebug(@$"[PlayerResources] Casting Action: {actionName}, Id: {actionId}");
            }
        }
        else if (actionType == ActionType.Item)
        {
            Service.PrintDebug(@$"[PlayerResources] Using Item: {actionName}, Id: {actionId}");
            UseItems(actionId);
        }

        _blockActionNoDelay = false;
    }

    public static async void DelayNextCast(uint actionId)
    {
        var delay = 0;
        try
        {
            delay = new Random().Next(Service.Configuration.DelayBetweenCastsMin,
                Service.Configuration.DelayBetweenCastsMax);
        }
        catch (Exception e)
        {
            Svc.Log.Error(@$"Error getting delay between casts: {e}");
        }

        await Task.Delay(delay + ConditionalDelay(actionId));

        _blockCasting = false;
    }

    private static int ConditionalDelay(uint id)
        => id switch
        {
            IDs.Actions.ThaliaksFavor => 1100,
            IDs.Actions.MakeshiftBait => 1100,
            IDs.Actions.NaturesBounty => 1100,
            IDs.Item.Cordial => 1100,
            IDs.Item.HQCordial => 1100,
            IDs.Item.HiCordial => 1100,
            IDs.Item.WateredCordial => 1100,
            IDs.Item.HQWateredCordial => 1100,
            _ => 0,
        };
}
