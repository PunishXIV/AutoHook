namespace AutoHook.Modules;

/// <summary>Shared preset/bait swap used by Extra side-effects and fish-caught swaps.</summary>
public static class PresetSwapHelpers {
    public static CustomPresetConfig? FindPresetByName(string presetName) {
        if (string.IsNullOrEmpty(presetName) || presetName == @"-")
            return null;

        var presets = RodFishingModule.Presets;
        if (presets.DefaultPreset.PresetName == presetName)
            return presets.DefaultPreset;

        return presets.CustomPresets.FirstOrDefault(p => p.PresetName == presetName);
    }

    /// <returns>True if a different preset was selected.</returns>
    public static bool TrySwapPreset(WorldState ws, string presetName, string reason, string successMessage, string? notFoundMessage = null, bool clearExtraTriggerStates = false) {
        if (ws.Fishing.FishingStep.HasFlag(FishingSteps.PresetSwapped))
            return false;

        var presets = RodFishingModule.Presets;
        if (presets.CurrentPreset.PresetName == presetName) {
            ws.Execute(new RodState.OpSetFishingStep(FishingSteps.PresetSwapped, Or: true));
            FindPresetByName(presetName)?.TryResetCounter();
            return false;
        }

        var preset = FindPresetByName(presetName);
        ws.Execute(new RodState.OpSetFishingStep(FishingSteps.PresetSwapped, Or: true));

        if (preset != null) {
            Configuration.Save();
            presets.Select(preset, reason);
            if (clearExtraTriggerStates)
                preset.ExtraCfg.LastTriggerStates.Clear();
            IChatGui.Get().PrintStatus(successMessage);
            Configuration.Save();
            return true;
        }

        if (notFoundMessage != null)
            IChatGui.Get().PrintStatus(notFoundMessage);

        return false;
    }

    public static ChangeBaitReturn TrySwapBait(WorldState ws, BaitFishClass bait, bool skipIfAlreadySwapped = false) {
        if (skipIfAlreadySwapped && ws.Fishing.FishingStep.HasFlag(FishingSteps.BaitSwapped))
            return ChangeBaitReturn.AlreadyEquipped;

        var result = BaitComponent.ChangeBait(bait);
        ws.Execute(new RodState.OpSetFishingStep(FishingSteps.BaitSwapped, Or: true));
        return result;
    }
}
