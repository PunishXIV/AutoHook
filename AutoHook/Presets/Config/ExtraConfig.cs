using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Threading;

namespace AutoHook.Presets.Config;

public enum ExtraStopAction {
    None,
    StopOnly,
    QuitFishing,
}

public class ExtraTrigger {
    [JsonIgnore]
    private static int _nextUiId = 1;

    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public int UiId { get; set; }

    public ConditionSet? ConditionSet { get; set; }

    public bool SwapPreset { get; set; }

    [DefaultValue("-")]
    public string PresetToSwap { get; set; } = @"-";

    public bool SwapBait { get; set; }
    public BaitFishClass BaitToSwap { get; set; } = new();

    public ExtraStopAction StopAction { get; set; } = ExtraStopAction.None;

    public bool ResolveCollectablesWindow { get; set; }
    public bool ResolveCollectablesForceNo { get; set; }

    public bool StartFishing { get; set; }

    public bool ReduceFish { get; set; }

    public bool RemoveStatus { get; set; }
    public uint StatusToRemove { get; set; }

    public bool ResetFishCaughtCounter { get; set; }

    public NotificationConfig NotifyOnSuccess { get; set; } = new();

    public void EnsureUiId() {
        if (UiId <= 0)
            UiId = Interlocked.Increment(ref _nextUiId);
    }

    public string GetRuleLabel(int index) {
        var summary = SummarizeActions();
        return string.IsNullOrEmpty(summary) ? $"Rule {index + 1}" : $"Rule {index + 1}: {summary}";
    }

    public string DescribeActions() {
        var summary = SummarizeActions();
        return string.IsNullOrEmpty(summary) ? "(no actions configured)" : summary;
    }

    private string SummarizeActions() {
        var parts = new List<string>();

        switch (StopAction) {
            case ExtraStopAction.StopOnly:
                parts.Add("Stop fishing");
                break;
            case ExtraStopAction.QuitFishing:
                parts.Add("Quit fishing");
                break;
        }

        if (ResetFishCaughtCounter)
            parts.Add(UIStrings.Reset_fish_caught_counter);

        if (SwapPreset && !string.IsNullOrEmpty(PresetToSwap) && PresetToSwap != "-")
            parts.Add($"Swap preset -> {PresetToSwap}");

        if (SwapBait)
            parts.Add($"Swap bait -> {BaitToSwap.Name}");

        if (RemoveStatus && StatusToRemove != 0)
            parts.Add($"Remove {Status.GetRow(StatusToRemove).Name}");

        if (StartFishing)
            parts.Add("Start fishing");

        if (ReduceFish)
            parts.Add(UIStrings.AetherialReduction_ReduceFish);

        if (ResolveCollectablesWindow)
            parts.Add(ResolveCollectablesForceNo ? "Decline collectables" : "Accept collectables");

        if (NotifyOnSuccess.Enabled)
            parts.Add("Notify");

        return parts.Count == 0 ? string.Empty : string.Join("; ", parts);
    }
}

public class ExtraConfig : BaseOption {
    public bool Enabled = false;

    public bool ResetCounterPresetSwap = false;
    public bool RetainCountersBetweenSessions = false;
    public bool ForceBaitSwap;
    public int ForcedBaitId;

    public List<ExtraTrigger> Triggers { get; set; } = [];

    // use this preset when auto ocean fishing hits a matching zone/time (+ optional conditions).
    public bool AutoOceanFishEnabled;
    public bool AutoOceanFishAllStops;
    public uint AutoOceanFishSpotId;
    public uint AutoOceanFishTimeId;
    public ConditionSet? AutoOceanFishConditionSet;

    [DefaultValue(OceanFishGoalKind.Points)]
    public OceanFishGoalKind AutoOceanFishGoal = OceanFishGoalKind.Points;
    public uint AutoOceanFishGoalId;

    [JsonIgnore] public List<bool> LastTriggerStates { get; } = [];

    public override void DrawOptions() { }
}
