using AutoHook.Conditions;
using AutoHook.Conditions.Definitions;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AutoHook.Configurations;

public class FishConfig : BaseOption {
    [DefaultValue(true)]
    public bool Enabled = true;

    public ConditionSet? IgnoreConditionSet { get; set; }

    public BaitFishClass Fish = new();

    public bool StopAfterResetCount = false;

    public AutoIdenticalCast IdenticalCast = new();
    public AutoSurfaceSlap SurfaceSlap = new();
    public AutoMooch Mooch = new();
    public AutoSparefulHand SparefulHand = new();
    public AutoMultiHook Multihook = new();
    public NotificationConfig NotifyOnSuccess { get; set; } = new();

    public BaitFishClass BaitToSwap = new();
    public bool SwapBaitResetCount = false;

    [DefaultValue("-")]
    public string PresetToSwap = "-";

    public bool NeverMooch = false;

    [DefaultValue(FishingSteps.None)]
    public FishingSteps StopFishingStep = FishingSteps.None;

    public FishConfig() { }

    public FishConfig(BaitFishClass fish) {
        Fish = fish;
        Mooch.UseAlwaysMoochLabel = true;
    }

    public FishConfig(int fishId) {
        Fish = new BaitFishClass(fishId);
    }

    [JsonProperty("StopConditionSet")]
    [JsonConverter(typeof(SingleConditionConverter))]
    public SingleCondition<SessionCaughtCountCD, (bool Enabled, int Limit)> StopAfterCaughtLimit { get => field ??= new SingleCondition<SessionCaughtCountCD, (bool Enabled, int Limit)>(() => Fish.Id); set; }

    [JsonProperty("SwapBaitConditionSet")]
    [JsonConverter(typeof(SingleConditionConverter))]
    public SingleCondition<SessionCaughtCountCD, (bool Enabled, int Limit)> SwapBaitLimit { get => field ??= new SingleCondition<SessionCaughtCountCD, (bool Enabled, int Limit)>(() => Fish.Id); set; }

    [JsonProperty("SwapPresetConditionSet")]
    [JsonConverter(typeof(SingleConditionConverter))]
    public SingleCondition<SessionCaughtCountCD, (bool Enabled, int Limit)> SwapPresetLimit { get => field ??= new SingleCondition<SessionCaughtCountCD, (bool Enabled, int Limit)>(() => Fish.Id); set; }

    public override void DrawOptions() { }
}
