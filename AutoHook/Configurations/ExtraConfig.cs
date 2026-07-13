using AutoHook.Conditions;
using AutoHook.Conditions.Definitions;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using System.Threading;
using AhCondition = AutoHook.Conditions.Condition;

namespace AutoHook.Configurations;

public enum ExtraStopAction {
    None,
    StopOnly,
    QuitFishing,
}

public class ExtraTrigger {
    [JsonIgnore]
    private static int _nextUiId = 1;

    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public int UiId { get; set; }

    public ConditionSet? ConditionSet { get; set; }

    public bool SwapPreset { get; set; }
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
        var summary = SummarizeTrigger();
        return string.IsNullOrEmpty(summary)
            ? $"Rule {index + 1}"
            : $"Rule {index + 1}: {summary}";
    }

    public string GetTriggerHeaderLabel(int index) {
        var summary = SummarizeTrigger();
        return string.IsNullOrEmpty(summary) ? $"Rule {index + 1}" : $"Rule {index + 1} – {summary}";
    }

    public string DescribeActions() {
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
            parts.Add($"Swap preset → {PresetToSwap}");

        if (SwapBait)
            parts.Add($"Swap bait → {BaitToSwap.Name}");

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

        return parts.Count == 0 ? "(no actions configured)" : string.Join("; ", parts);
    }

    private string SummarizeTrigger() {
        if (ResetFishCaughtCounter)
            return UIStrings.Reset_fish_caught_counter;

        if (ReduceFish)
            return UIStrings.AetherialReduction_ReduceFish;

        if (RemoveStatus && StatusToRemove != 0)
            return $"Remove {Status.GetRow(StatusToRemove).Name}";

        if (ConditionSet is not { } set || !set.HasGroups())
            return string.Empty;

        if (set.Groups.Count != 1)
            return string.Empty;

        var group = set.Groups[0];
        if (group.Conditions.Count != 1)
            return string.Empty;

        var cond = group.Conditions[0];
        var core = SummarizeCondition(cond);
        if (string.IsNullOrEmpty(core))
            return string.Empty;

        var inv = IConditionDefinition.GetBool(cond.Params, "inv", false);
        return inv ? $"On Lose {core}" : $"On Gain {core}";
    }

    private static string SummarizeCondition(AhCondition cond) {
        switch (cond.TypeId) {
            case "IntuitionActive" or nameof(IntuitionActiveCD):
                return "Fisher's Intuition";
            case "SpectralActive" or nameof(SpectralActiveCD):
                return "Spectral Current";
            case "StatusStacksCD" or "StatusStacks":
                if (cond.Params.TryGetValue("ids", out var idsObj) && idsObj is List<object> list && list.Count == 1) {
                    var id = Convert.ToUInt32(list[0]);
                    if (id == IDs.Status.AnglersArt) {
                        var stacks = IConditionDefinition.GetInt(cond.Params, "minStacks", 1);
                        return $"Angler's Art ≥ {stacks} Stacks";
                    }
                }
                return "Status Stacks";
            case "SwimbaitCountCD" or "SwimbaitCount":
                var fishId = IConditionDefinition.GetInt(cond.Params, "id", 0);
                var fishLabel = fishId == 0 ? "Slot Fish" : Item.GetRow((uint)fishId).Name.ToString();
                return $"Swimbaits ({fishLabel}) {FormatIntCompare(cond.Params)}";
            default:
                return cond.Describe(ConditionRegistry.Registry);
        }
    }

    private static string FormatIntCompare(IReadOnlyDictionary<string, object> p) {
        var args = IConditionDefinition.GetIntCompareParams(p);
        var cmp = args.Op switch {
            ">" => ">",
            "<" => "<",
            "<=" => "≤",
            "=" => "=",
            _ => "≥",
        };
        return $"{cmp} {args.Value}";
    }
}

public class ExtraConfig : BaseOption {
    public bool Enabled = false;

    public bool ResetCounterPresetSwap = false;
    public bool ForceBaitSwap;
    public int ForcedBaitId;

    public List<ExtraTrigger> Triggers { get; set; } = [];

    /// <summary>Use this preset when auto ocean fishing starts a stop matching zone/time and optional conditions.</summary>
    public bool AutoOceanFishEnabled;
    public bool AutoOceanFishAllStops;
    public uint AutoOceanFishSpotId;
    public uint AutoOceanFishTimeId;
    public ConditionSet? AutoOceanFishConditionSet;
    public OceanFishGoalKind AutoOceanFishGoal = OceanFishGoalKind.Points;
    public uint AutoOceanFishGoalId;

    [JsonIgnore] public List<bool> LastTriggerStates { get; } = [];

    public override void DrawOptions() { }
}
