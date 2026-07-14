using AutoHook.Conditions;
using AutoHook.Conditions.Definitions;
using AutoHook.Replay;
using Newtonsoft.Json;

namespace AutoHook.Configurations;

public class AutoCastsConfig {
    public bool EnableAll = false;

    public bool DontCancelMooch = true;

    public bool RecastAnimationCancel;
    public bool TurnCollectOff;
    public bool ChumAnimationCancel;
    public bool TurnCollectOffWithoutAnimCancel;

    public AutoCastLine CastLine = new();
    public AutoMooch CastMooch = new();
    public AutoChum CastChum = new();
    public AutoCollect CastCollect = new();
    public AutoSnagging CastSnagging = new();
    public AutoCordial CastCordial = new();
    public AutoFishEyes CastFishEyes = new();
    public AutoMakeShiftBait CastMakeShiftBait = new();
    public AutoPatience CastPatience = new();
    public AutoPrizeCatch CastPrizeCatch = new();
    public AutoThaliaksFavor CastThaliaksFavor = new();
    public AutoBigGameFishing CastBigGame = new();
    public AutoMultiHook CastMultihook = new();

    private List<BaseActionCast> GetAutoCastOrder() {
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
            CastSnagging,
            CastBigGame,
            CastMultihook,
        }.OrderBy(x => x.Priority).ToList();

        return output;
    }

    public BaseActionCast? GetNextAutoCast(bool ignoreCurrentMooch)
        => GetNextAutoCast(GetAutoCastOrder(), ignoreCurrentMooch);

    public BaseActionCast? GetNextGpRestoringCast(bool ignoreCurrentMooch)
        => GetNextAutoCast(GetAutoCastOrder().Where(action => action.RestoresGp), ignoreCurrentMooch);

    private BaseActionCast? GetNextAutoCast(IEnumerable<BaseActionCast> order, bool ignoreCurrentMooch) {
        if (!EnableAll)
            return null;

        foreach (var action in order.Where(action => action.IsAvailableToCast(ignoreCurrentMooch))) {
            if (action.RequiresTimeWindow() && !TimeWindow.BackingSet.PassesOrUnconfigured()) {
                LogAutoCastDecision(action, "Time window blocked");
                continue;
            }

            Service.PrintDebug($"[AutoCast] Returning {action.GetName()}");
            return action;
        }

        return null;
    }

    public bool TryCastGpRestoringAction(bool ignoreCurrentMooch = false)
        => TryCastAction(GetNextGpRestoringCast(ignoreCurrentMooch), ignoreCurrentMooch: ignoreCurrentMooch);

    [JsonProperty("TimeWindowConditionSet")]
    [JsonConverter(typeof(SingleConditionConverter))]
    public SingleCondition<TimeWindowCD, (bool Enabled, TimeOnly Start, TimeOnly End)> TimeWindow { get; set; } = new SingleCondition<TimeWindowCD, (bool Enabled, TimeOnly Start, TimeOnly End)>();

    public bool TryCastAction(BaseActionCast? action, bool noDelay = false, bool ignoreCurrentMooch = false) {
        if (action == null || !EnableAll)
            return false;

        if (action.RequiresTimeWindow() && !TimeWindow.BackingSet.PassesOrUnconfigured()) {
            LogAutoCastDecision(action, "Time window blocked");
            return false;
        }

        if (action.DescribeUnavailable(ignoreCurrentMooch) is { } unavailable) {
            LogAutoCastDecision(action, unavailable);
            return false;
        }

        if (action.Id == IDs.Actions.Chum && ChumAnimationCancel) {
            TryChumAnimationCancel();
            LogAutoCastDecision(action);
            return true;
        }

        if (noDelay) {
            if (!PlayerRes.TryCastActionNoDelay(action.Id, action.ActionType, action.GetName())) {
                LogAutoCastDecision(action, "Cast rejected by game");
                return false;
            }
        }
        else if (!PlayerRes.TryCastActionDelayed(action.Id, action.ActionType, action.GetName())) {
            LogAutoCastDecision(action, "Cast rejected by game");
            return false;
        }

        LogAutoCastDecision(action);
        return true;
    }

    private void LogAutoCastDecision(BaseActionCast action, string? failureReason = null) {
        var trace = action.ConditionSet?.DescribeEvaluation(Service.WorldState, ConditionRegistry.Registry) ?? [];
        if (action.RequiresTimeWindow() && TimeWindow.BackingSet is { } timeWindow) {
            var global = timeWindow.DescribeEvaluation(Service.WorldState, ConditionRegistry.Registry);
            if (global.Count > 0)
                trace = [.. trace, .. global.Select(t => ($"Global {t.Label}", t.Result))];
        }

        var outcome = failureReason == null
            ? $"Cast {action.GetName()}"
            : $"Did not cast {action.GetName()} — {failureReason}";

        DecisionLog.Start(UIStrings.Auto_Casts)
            .WithConditionResults(trace)
            .Chose(outcome);
    }

    private void TryChumAnimationCancel() {
        Service.PrintDebug("Trying to cancel chum animation");
        // Make sure Salvage is disabled before chum

        Service.TaskManager.EnqueueDelay(40);
        Service.TaskManager.Enqueue(() => PlayerRes.CastAction(IDs.Actions.Chum));

        // Recast Salvage a few ms's later, maybe 500 is enough?
        Service.TaskManager.EnqueueDelay(465);
        Service.TaskManager.Enqueue(() => PlayerRes.CastAction(IDs.Actions.Salvage));
    }
}
