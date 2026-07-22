namespace AutoHook.Configurations.Legacy;

// converts very old presets (v1/v2/v3) into the current model; lives next to legacy DTOs.
internal static class LegacyPresetMapper {
    private static void SetFieldNewClass(HookConfig newOne, BaitConfig old) {
        var oldType = old.GetType();
        var newType = newOne.GetType();

        var oldFields = oldType.GetFields();
        var newFields = newType.GetFields();

        foreach (var sourceField in oldFields) {
            var targetField = newFields.FirstOrDefault(f => f.Name == sourceField.Name && f.FieldType == sourceField.FieldType);
            if (targetField != null) {
                var value = sourceField.GetValue(old);
                targetField.SetValue(newOne, value);
            }
        }
    }

    public static CustomPresetConfig? ConvertOldPreset(BaitPresetConfig? preset) {
        if (preset == null)
            return null;

        var filteredBaits = new List<HookConfig>();
        var filteredMooch = new List<HookConfig>();
        foreach (var old in preset.ListOfBaits) {
            var matchingBait = GameRes.Baits.FirstOrDefault(b => b.Name == old.BaitName);
            var matchingFish = GameRes.Fishes.FirstOrDefault(f => f.Name == old.BaitName);

            if (matchingBait != null) {
                var newOne = new HookConfig(matchingBait);
                SetFieldNewClass(newOne, old);
                filteredBaits.Add(newOne);
            }
            else if (matchingFish != null) {
                var newOne = new HookConfig(matchingFish);
                SetFieldNewClass(newOne, old);
                filteredMooch.Add(newOne);
            }
        }

        var newPreset = new CustomPresetConfig(@$"[Old Version] {preset.PresetName}") {
            ListOfBaits = filteredBaits,
            ListOfMooch = filteredMooch
        };
        return newPreset;
    }

    public static CustomPresetConfig? ConvertOldPresetV3(OldPresetConfig? old) {
        if (old == null)
            return null;

        var newPreset = new CustomPresetConfig(old.PresetName);

        Service.PrintDebug($"Converting v3 to v4: {old.PresetName}");
        foreach (var bait in old.ListOfBaits) {
            bait.ConvertV3ToV4();

            var newBait = new HookConfig(bait.BaitFish) {
                Enabled = bait.Enabled,
                NormalHook = bait.NormalHook,
                IntuitionHook = bait.IntuitionHook,
                StopAfterResetCount = bait.StopAfterResetCount,
                StopFishingStep = bait.StopFishingStep,
            };
            if (bait.StopAfterCaught)
                newBait.StopAfterCaughtLimit.Value = (true, bait.StopAfterCaughtLimit);
            newBait.IntuitionHook.UseCustomStatusHook = bait.UseCustomIntuitionHook;

            newPreset.AddItem(newBait);
        }

        foreach (var mooch in old.ListOfMooch) {
            mooch.ConvertV3ToV4();
            var newMooch = new HookConfig(mooch.BaitFish) {
                Enabled = mooch.Enabled,
                NormalHook = mooch.NormalHook,
                IntuitionHook = mooch.IntuitionHook,
                StopAfterResetCount = mooch.StopAfterResetCount,
                StopFishingStep = mooch.StopFishingStep,
            };
            if (mooch.StopAfterCaught)
                newMooch.StopAfterCaughtLimit.Value = (true, mooch.StopAfterCaughtLimit);
            newMooch.IntuitionHook.UseCustomStatusHook = mooch.UseCustomIntuitionHook;

            newPreset.AddItem(newMooch);
        }

        newPreset.ListOfFish = old.ListOfFish;
        newPreset.ExtraCfg = old.ExtraCfg;
        newPreset.AutoCastsCfg = old.AutoCastsCfg;

        return newPreset;
    }
}

