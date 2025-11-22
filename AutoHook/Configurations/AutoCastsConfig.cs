using System.ComponentModel;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace AutoHook.Configurations;

public class AutoCastsConfig
{
    public bool EnableAll = false;

    [DefaultValue(true)] public bool DontCancelMooch = true;

    public TimeOnly StartTime = new(0);
    public TimeOnly EndTime = new(0);

    public bool OnlyCastDuringSpecificTime = false;

    public bool RecastAnimationCancel;
    public bool TurnCollectOff;
    public bool ChumAnimationCancel;
    public bool TurnCollectOffWithoutAnimCancel;

    public AutoCastLine CastLine = new();
    public AutoMooch CastMooch = new();
    public AutoChum CastChum = new();
    public AutoCollect CastCollect = new();
    public AutoCordial CastCordial = new();
    public AutoFishEyes CastFishEyes = new();
    public AutoMakeShiftBait CastMakeShiftBait = new();
    public AutoPatience CastPatience = new();
    public AutoPrizeCatch CastPrizeCatch = new();
    public AutoThaliaksFavor CastThaliaksFavor = new();
    public AutoBigGameFishing CastBigGame = new();
    //public AutoLures CastLures = new();

    private List<BaseActionCast> GetAutoCastOrder()
    {
        var output = new List<BaseActionCast>
        {
            CastThaliaksFavor,
            CastCordial,
            CastPatience,
            CastMakeShiftBait,
            CastChum,
            CastFishEyes,
            CastPrizeCatch,
            //CastCollect,
            CastBigGame
        }.OrderBy(x => x.Priority).ToList();

        return output;
    }

    public BaseActionCast? GetNextAutoCast(bool ignoreCurrentMooch)
    {
        if (!EnableAll)
            return null;

        BaseActionCast? cast = null;

        var order = GetAutoCastOrder();

        foreach (var action in order.Where(action => action.IsAvailableToCast(ignoreCurrentMooch)))
        {
            if (OnlyCastDuringSpecificTime && action.RequiresTimeWindow() && !InsideCastWindow())
                continue;

            Service.PrintDebug($"[AutoCast] Returning {action.Name}");
            return action;
        }

        return cast;
    }

    private unsafe bool InsideCastWindow()
    {
        var clientTime = Framework.Instance()->ClientTime.EorzeaTime;
        var eorzeaTime = TimeOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(clientTime).DateTime);

        return eorzeaTime.IsBetween(StartTime, EndTime);
    }

    public bool TryCastAction(BaseActionCast? action, bool noDelay = false, bool ignoreCurrentMooch = false)
    {
        if (action == null || !EnableAll)
            return false;

        if (OnlyCastDuringSpecificTime && action.RequiresTimeWindow() && !InsideCastWindow())
            return false;

        if (!action.Enabled || !action.IsAvailableToCast(ignoreCurrentMooch))
            return false;

        if (action.Id == IDs.Actions.Chum && ChumAnimationCancel)
            TryChumAnimationCancel();
        else if (noDelay)
            PlayerRes.CastActionNoDelay(action.Id, action.ActionType, action.GetName());
        else
            PlayerRes.CastActionDelayed(action.Id, action.ActionType, action.GetName());

        return true;
    }

    private void TryChumAnimationCancel()
    {
        Service.PrintDebug("Trying to cancel chum animation");
        // Make sure Salvage is disabled before chum

        Service.TaskManager.EnqueueDelay(40);
        Service.TaskManager.Enqueue(() => PlayerRes.CastAction(IDs.Actions.Chum));

        // Recast Salvage a few ms's later, maybe 500 is enough?
        Service.TaskManager.EnqueueDelay(465);
        Service.TaskManager.Enqueue(() => PlayerRes.CastAction(IDs.Actions.Salvage));
    }
}