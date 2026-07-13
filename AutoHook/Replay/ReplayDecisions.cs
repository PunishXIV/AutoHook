using AutoHook.Conditions;
using Lumina.Excel.Sheets;

namespace AutoHook.Replay;

public static class ReplayDecisions {
    private static WorldState Ws => Service.WorldState;

    public static void Emit(string context, string action, string detail = "", ConditionSet? conditions = null, string? presetName = null)
        => Emit(context, action, detail, conditions?.DescribeEvaluation(Ws, ConditionRegistry.Registry), presetName);

    public static void Emit(string context, string action, string detail, IReadOnlyList<(string Label, bool Result)>? trace, string? presetName = null) {
        var preset = presetName ?? Service.Configuration.HookPresets.SelectedPreset?.PresetName ?? Service.GlobalPresetName;
        Ws.LogDecision(context, preset, action, trace, detail);
    }

    public static void AutoCast(BaseActionCast action, AutoCastsConfig acCfg, string? presetName = null) {
        var trace = DescribeAutoCastConditions(action, acCfg);
        Emit(UIStrings.Auto_Casts, $"Cast {action.GetName()}", detail: "", trace: trace, presetName: presetName);
    }

    public static string FormatConditionTrace(IReadOnlyList<(string Label, bool Result)> trace)
        => trace.Count == 0 ? string.Empty : string.Join("\n", trace.Select(t => $"{t.Label}: {(t.Result ? "T" : "F")}"));

    private static List<(string Label, bool Result)> DescribeAutoCastConditions(BaseActionCast action, AutoCastsConfig acCfg) {
        var trace = action.ConditionSet?.DescribeEvaluation(Ws, ConditionRegistry.Registry) ?? [];
        if (!action.RequiresTimeWindow() || acCfg.TimeWindow.BackingSet is not { } timeWindow)
            return trace;

        var global = timeWindow.DescribeEvaluation(Ws, ConditionRegistry.Registry);
        if (global.Count == 0)
            return trace;

        trace = [.. trace, .. global.Select(t => ($"Global {t.Label}", t.Result))];
        return trace;
    }

    public static void ExtraTrigger(int ruleIndex, ExtraTrigger trig, string presetName) {
        Emit(UIStrings.ExtraOptions, trig.GetRuleLabel(ruleIndex), trig.DescribeActions(), trig.ConditionSet, presetName);
    }

    public static void HookPresetOnBite(bool enabled) {
        Emit("Hook Preset", enabled ? "Enabled preset on bite" : "No enabled preset on bite");
    }

    public static void HookPresetChoice(BiteType bite, HookType? hook) {
        if (hook is null or HookType.None) {
            Emit("Hook Preset", $"No hook for {bite} bite");
            return;
        }

        Emit("Hook Preset", $"Use {hook} for {bite} bite");
    }

    public static void SwimbaitSlotFailed(uint fishId, ConditionSet? conditions, string presetName) {
        var fish = fishId == 0 ? "unknown fish" : Item.GetRow(fishId).Name.ToString();
        Emit("Swimbait", $"Conditions failed for {fish}", conditions: conditions, presetName: presetName);
    }

    public static void SwimbaitSlotSelected(int slotIndex, uint fishId, ConditionSet? conditions, string presetName) {
        var fish = fishId == 0 ? "unknown fish" : Item.GetRow(fishId).Name.ToString();
        Emit("Swimbait", $"Selected slot {slotIndex} for {fish}", conditions: conditions, presetName: presetName);
    }

    public static void OceanPresetApply(OceanFishingState ocean, OceanFishGoalKind settingsGoal, OceanFishGoalKind? matchedTier, uint matchedGoalId, string? matchedPresetName, bool alreadySelected, bool selectedGlobal) {
        var stop = OceanStopUtil.FormatStopLabel(ocean.CurrentSpotId, ocean.CurrentTimeId);
        var detail = $"settingsGoal={settingsGoal} route={ocean.CurrentRoute} zone={ocean.CurrentZone + 1} stop={stop}";

        if (OceanGoalCatalog.GetCascade(settingsGoal).Contains(OceanFishGoalKind.Achievement))
            detail += $"; achievements={OceanGoalCatalog.DescribeAchievements(ocean.CurrentRoute)}";

        if (matchedTier is { } tier)
            detail += $"; tier={tier} goalId={matchedGoalId}";

        string action;
        string presetName;
        if (matchedPresetName is null) {
            action = "No matching preset";
            presetName = Service.Configuration.HookPresets.SelectedPreset?.PresetName ?? Service.GlobalPresetName;
        }
        else if (selectedGlobal) {
            action = alreadySelected ? "Already on global" : "Selected global";
            presetName = Service.GlobalPresetName;
        }
        else if (alreadySelected) {
            action = $"Already on {matchedPresetName}";
            presetName = matchedPresetName;
        }
        else {
            action = $"Selected {matchedPresetName}";
            presetName = matchedPresetName;
        }

        Emit("Auto Ocean Fish", action, detail, presetName: presetName);
    }
}
