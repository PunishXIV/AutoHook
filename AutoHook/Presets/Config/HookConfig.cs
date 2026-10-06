using FFXIVClientStructs.FFXIV.Client.Game;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AutoHook.Presets.Config;

public class SwimbaitConfig {
    public bool UseSwimbait = false;
    public ConditionSet? ConditionSet { get; set; }
}

public class HookConfig : BaseOption {
    [DefaultValue(true)]
    public bool Enabled = true;

    public BaitFishClass BaitFish = new();

    public BaseHookset NormalHook = new(IDs.Status.None);
    public BaseHookset IntuitionHook = new(IDs.Status.FishersIntuition);

    public SwimbaitConfig SwimbaitNormal { get; set; } = new();
    public SwimbaitConfig SwimbaitIntuition { get; set; } = new();
    public NotificationConfig NotifyOnSuccess { get; set; } = new();

    public bool StopAfterResetCount;

    [DefaultValue(FishingSteps.StopCasting)]
    public FishingSteps StopFishingStep = FishingSteps.StopCasting;

    [JsonProperty("StopConditionSet")]
    [JsonConverter(typeof(SingleConditionConverter))]
    public SingleCondition<HookCountCD, (bool Enabled, int Limit)> StopAfterCaughtLimit { get => field ??= new SingleCondition<HookCountCD, (bool Enabled, int Limit)>(() => UniqueId); set; }

    //todo enable more hook settings based on the current status
    //List<BaseHookset> CustomHooksets = new();

    public HookConfig() { }

    public HookConfig(BaitFishClass baitFish) {
        BaitFish = baitFish;
    }

    public HookConfig(Fish fish) {
        BaitFish = new BaitFishClass(fish);
    }

    public HookConfig(int baitFishId) {
        BaitFish = new BaitFishClass(baitFishId);
    }

    public void SetBiteAndHookType(BiteType bite, HookType hookType, bool isIntuition = false) {
        var hookset = isIntuition ? IntuitionHook : NormalHook;
        var hookDictionary = new Dictionary<BiteType, (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph)>
        {
            { BiteType.Weak, (hookset.TripleWeak, hookset.DoubleWeak, hookset.PatienceWeak) },
            { BiteType.Strong, (hookset.TripleStrong, hookset.DoubleStrong, hookset.PatienceStrong) },
            { BiteType.Legendary, (hookset.TripleLegendary, hookset.DoubleLegendary, hookset.PatienceLegendary) }
        };

        if (hookDictionary.TryGetValue(bite, out var hook)) {
            hook.ph.HooksetEnabled = true;
            hook.ph.HooksetType = hookType;

            if (hookset.UseDoubleHook)
                hook.dh.HooksetEnabled = true;
            if (hookset.UseTripleHook)
                hook.th.HooksetEnabled = true;
        }
    }

    public void SetHooksetTimer(BiteType bite, double min, double max, bool isIntuition = false) {
        var hookset = isIntuition ? IntuitionHook : NormalHook;
        var hookDictionary = new Dictionary<BiteType, (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph)>
        {
            { BiteType.Weak, (hookset.TripleWeak, hookset.DoubleWeak, hookset.PatienceWeak) },
            { BiteType.Strong, (hookset.TripleStrong, hookset.DoubleStrong, hookset.PatienceStrong) },
            { BiteType.Legendary, (hookset.TripleLegendary, hookset.DoubleLegendary, hookset.PatienceLegendary) }
        };

        if (!hookDictionary.TryGetValue(bite, out var hook)) return;

        var maxSec = max + 1;
        var biteTimerId = Registry.GetId<BiteTimerCD>();
        foreach (var biteCfg in HookConfigsForTimer(hookset, hook))
            SetBiteTimerInConditionSet(biteCfg, biteTimerId, min, maxSec);
    }

    private static IEnumerable<BaseBiteConfig> HookConfigsForTimer(
        BaseHookset hookset,
        (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph) hook) {
        yield return hook.ph;
        if (hookset.UseDoubleHook)
            yield return hook.dh;
        if (hookset.UseTripleHook)
            yield return hook.th;
    }

    private static void SetBiteTimerInConditionSet(BaseBiteConfig biteCfg, string biteTimerId, double min, double max) {
        var set = biteCfg.ConditionSet ??= new ConditionSet();
        Condition? found = null;
        foreach (var group in set.Groups) {
            found = group.Conditions.FirstOrDefault(c => c.TypeId == biteTimerId);
            if (found != null) break;
        }
        if (found != null) {
            found.Params["r"] = new List<object> { min, max };
            return;
        }
        var newGroup = new ConditionGroup { CombineMode = ConditionCombineMode.Any };
        newGroup.Conditions.Add(new Condition {
            TypeId = biteTimerId,
            Params = new Dictionary<string, object> { ["r"] = new List<object> { min, max } }
        });
        set.Groups.Add(newGroup);
    }

    public void ResetAllHooksets() {
        ResetHooksets(NormalHook);
        ResetHooksets(IntuitionHook);
    }

    private void ResetHooksets(BaseHookset hookset) {
        var hookDictionary = new Dictionary<BiteType, (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph)>
        {
            { BiteType.Weak, (hookset.TripleWeak, hookset.DoubleWeak, hookset.PatienceWeak) },
            { BiteType.Strong, (hookset.TripleStrong, hookset.DoubleStrong, hookset.PatienceStrong) },
            { BiteType.Legendary, (hookset.TripleLegendary, hookset.DoubleLegendary, hookset.PatienceLegendary) }
        };

        foreach (var hookDisable in hookDictionary) {
            hookDisable.Value.ph.HooksetEnabled = false;
            hookDisable.Value.dh.HooksetEnabled = false;
            hookDisable.Value.th.HooksetEnabled = false;
        }
    }

    public BaseHookset GetHookset() {
        return UsesIntuitionHookConfig() ? IntuitionHook : NormalHook;
    }

    // same intuition vs normal window as GetHookset — for swimbait selection.
    public SwimbaitConfig GetSwimbaitConfig()
        => UsesIntuitionHookConfig() ? SwimbaitIntuition : SwimbaitNormal;

    public bool UsesIntuitionHookConfig() {
        var fishing = WorldState.Get().Fishing;
        var intuitionActive = fishing.CastSnapshot is { Active: true } snapshot ? snapshot.IntuitionStatus is IntuitionStatus.Active or IntuitionStatus.Gained : fishing.Intuition.IsActive;
        return intuitionActive && IntuitionHook.UseCustomStatusHook;
    }

    public HookType? GetHook(WorldState ws, BiteType bite, double timePassed) {
        var hookset = GetHookset();

        var hookDictionary = new Dictionary<BiteType, (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph)>
        {
            { BiteType.Weak, (hookset.TripleWeak, hookset.DoubleWeak, hookset.PatienceWeak) },
            { BiteType.Strong, (hookset.TripleStrong, hookset.DoubleStrong, hookset.PatienceStrong) },
            { BiteType.Legendary, (hookset.TripleLegendary, hookset.DoubleLegendary, hookset.PatienceLegendary) }
        };

        PluginUi.Status = "";

        if (hookDictionary.TryGetValue(bite, out var hook)) {
            // Triple Hook
            if (hookset.UseTripleHook && hook.th.HooksetEnabled) {
                if (hook.th.ConditionSet.PassesOrUnconfigured(ws))
                    if (GetHookTypeForTime(ws, hook.th, timePassed) is { } ht && IsHookAvailable(ws, hook.th, timePassed))
                        return ht;

                if (hookset.LetFishEscapeTripleHook && ws.Player.CurrentGp < 700) {
                    PluginUi.Status = "Not enough GP to use Triple Hook, Letting fish escape is enabled";
                    return HookType.None;
                }

                PluginUi.Status = $"(Triple Hook) {PluginUi.Status}";
            }

            // Double Hook
            if (hookset.UseDoubleHook && hook.dh.HooksetEnabled) {
                if (hook.dh.ConditionSet.PassesOrUnconfigured(ws))
                    if (GetHookTypeForTime(ws, hook.dh, timePassed) is { } ht && IsHookAvailable(ws, hook.dh, timePassed))
                        return ht;

                if (hookset.LetFishEscapeDoubleHook && ws.Player.CurrentGp < 400) {
                    PluginUi.Status = "Not enough GP to use Double Hook, Letting fish escape is enabled";
                    return HookType.None;
                }

                PluginUi.Status = $"(Triple Hook) {PluginUi.Status}";
            }

            // Normal - Patience
            if (hook.ph.HooksetEnabled) {
                if (hook.ph.ConditionSet.PassesOrUnconfigured(ws)) {
                    if (GetHookTypeForTime(ws, hook.ph, timePassed) is { } ht) {
                        if (IsHookAvailable(ws, hook.ph, timePassed)) return ht;
                        var fallback = ht switch {
                            HookType.Stellar when bite is BiteType.Weak => HookType.Precision,
                            HookType.Stellar when bite is not BiteType.Weak => HookType.Powerful,
                            _ => HookType.Normal
                        };
                        return fallback != ht && ws.ActionAvailable((uint)fallback, ActionType.Action) ? fallback : HookType.Normal;
                    }
                    PluginUi.Status = "(Normal/Patience Hook) No hook type for current bite timer.";
                }
                else
                    PluginUi.Status = $"(Normal/Patience Hook) {PluginUi.Status}";
            }
            else if (PluginUi.Status == "")
                PluginUi.Status = UIStrings.Status_NoHookEnabled;
        }

        //PluginUi.Status = "Skipping bite - No hook for this bite is enabled";
        return HookType.None;
    }

    private HookType? GetHookTypeForTime(WorldState ws, BaseBiteConfig hookType, double timePassed)
        => hookType.UseMultipleHookTypesByTimer
            ? GetTimedHookType(ws, hookType, timePassed) is { } timedHook ? timedHook : null
            : hookType.HookTypeConditionSet.PassesOrUnconfigured(ws)
                ? hookType.HooksetType
                : null;

    private HookType? GetTimedHookType(WorldState ws, BaseBiteConfig hookType, double timePassed) {
        bool InRange(bool enabled, double min, double max) {
            if (!enabled)
                return false;

            if (min > 0 && timePassed < min)
                return false;

            return max <= 0 || timePassed <= max;
        }

        // Highest value hook types first if multiple windows overlap
        if (InRange(hookType.UseStellarHookTypeByTimer, hookType.StellarHookTypeMin, hookType.StellarHookTypeMax)
            && hookType.StellarHookTypeConditionSet.PassesOrUnconfigured(ws))
            return HookType.Stellar;

        if (InRange(hookType.UsePowerfulHookTypeByTimer, hookType.PowerfulHookTypeMin, hookType.PowerfulHookTypeMax)
            && hookType.PowerfulHookTypeConditionSet.PassesOrUnconfigured(ws))
            return HookType.Powerful;

        if (InRange(hookType.UsePrecisionHookTypeByTimer, hookType.PrecisionHookTypeMin, hookType.PrecisionHookTypeMax)
            && hookType.PrecisionHookTypeConditionSet.PassesOrUnconfigured(ws))
            return HookType.Precision;

        if (InRange(hookType.UseNormalHookTypeByTimer, hookType.NormalHookTypeMin, hookType.NormalHookTypeMax)
            && hookType.NormalHookTypeConditionSet.PassesOrUnconfigured(ws))
            return HookType.Normal;

        return null;
    }

    private bool IsHookAvailable(WorldState ws, BaseBiteConfig hookType, double timePassed) {
        if (GetHookTypeForTime(ws, hookType, timePassed) is not { } timedHook)
            return false;

        if (timedHook == HookType.Stellar) {
            if (ws.IsStellarHooksetAvailable())
                return true;

            PluginUi.Status = UIStrings.Status_HookNotAvailableNormalWillBeUsed;
            return false;
        }

        if (!ws.ActionAvailable((uint)timedHook, ActionType.Action)) {
            PluginUi.Status = UIStrings.Status_HookNotAvailableNormalWillBeUsed;
            return false;
        }

        return true;
    }

    public override void DrawOptions() { }

    public override bool Equals(object? obj) => obj is HookConfig settings && BaitFish == settings.BaitFish;
    public override int GetHashCode() => HashCode.Combine(UniqueId);
}
