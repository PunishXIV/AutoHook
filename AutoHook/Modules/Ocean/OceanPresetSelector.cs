namespace AutoHook.Modules.Ocean;

public static class OceanPresetSelector {
    public static void TryApply() {
        if (!Configuration.C.AutoOceanFish)
            return;

        var ws = WorldState.Get();
        var ocean = ws.OceanFishing;
        if (ocean == OceanFishingState.Empty || ocean.TimeOfDay == TimeOfDay.None)
            return;

        OceanGoalCatalog.PrefetchRouteAchievements(ocean.CurrentRoute);

        var settingsGoal = Configuration.C.AutoOceanFishGoal;
        var stop = OceanStopUtil.FormatStopLabel(ocean.CurrentSpotId, ocean.CurrentTimeId);
        var fallthrough = Configuration.C.AOF_Fallthrough ? "fallthrough if acquired" : "keep goal if acquired";
        var about = $"{settingsGoal} · route {ocean.CurrentRoute} · zone {ocean.CurrentZone + 1} · {stop} · {fallthrough}";
        var skips = new List<string>();

        foreach (var tier in OceanGoalCatalog.GetCascade(settingsGoal)) {
            if (tier == OceanFishGoalKind.Achievement) {
                if (!TryMatchAchievementTier(ws, ocean, skips, out var achPreset))
                    continue;
                ApplyOceanPresetChoice(ws, about, skips, achPreset, OceanFishGoalKind.Achievement);
                return;
            }

            if (tier == OceanFishGoalKind.Legendary) {
                if (!TryMatchLegendaryTier(ocean, skips, out var legPreset))
                    continue;
                ApplyOceanPresetChoice(ws, about, skips, legPreset, OceanFishGoalKind.Legendary);
                return;
            }

            if (tier == OceanFishGoalKind.Levelling) {
                if (!TryMatchLevellingTier(ocean, skips, out var levPreset))
                    continue;
                ApplyOceanPresetChoice(ws, about, skips, levPreset, OceanFishGoalKind.Levelling);
                return;
            }

            var pointsPreset = FindOceanPresetForGoal(ocean, tier, goalId: null);
            if (pointsPreset == null) {
                skips.Add($"{tier} — no matching preset");
                continue;
            }

            ApplyOceanPresetChoice(ws, about, skips, pointsPreset, tier);
            return;
        }

        ws.Decide(DecisionContext.OceanPreset, false, "No matching preset", JoinDetail(about, skips));
    }

    private static bool TryMatchAchievementTier(WorldState ws, OceanFishingState ocean, List<string> skips, out CustomPresetConfig preset) {
        preset = null!;

        var forRoute = OceanGoalCatalog.GetAchievementsForRoute(ocean.CurrentRoute).ToList();
        if (forRoute.Count == 0) {
            skips.Add("Achievement — none on this route");
            return false;
        }

        var partySize = Math.Max(1, ws.Party.QueuedWithContentIds.Count);
        var statusParts = forRoute.Select(def => {
            if (partySize < def.MinPartySize)
                return $"#{def.AchievementId} party<{def.MinPartySize}";
            return OceanGoalCatalog.IsAchievementIncomplete(def.AchievementId) switch {
                true => $"#{def.AchievementId} incomplete",
                false => $"#{def.AchievementId} obtained",
                null => $"#{def.AchievementId} unknown",
            };
        });
        var status = string.Join(", ", statusParts);

        var skipIfAcquired = Configuration.C.AOF_Fallthrough;
        var eligible = OceanGoalCatalog.GetEligibleAchievementIds(ocean.CurrentRoute, skipIfAcquired);
        if (eligible.Count == 0) {
            skips.Add(skipIfAcquired ? $"Achievement — not eligible ({status})" : $"Achievement — not eligible, party size ({status})");
            return false;
        }

        foreach (var achId in eligible) {
            var match = FindOceanPresetForGoal(ocean, OceanFishGoalKind.Achievement, achId);
            if (match == null)
                continue;
            preset = match;
            return true;
        }

        skips.Add($"Achievement — no matching preset (eligible {string.Join(",", eligible)}; {status})");
        return false;
    }

    private static bool TryMatchLevellingTier(OceanFishingState ocean, List<string> skips, out CustomPresetConfig preset) {
        preset = null!;

        if (!OceanGoalCatalog.IsLevellingNeeded() && Configuration.C.AOF_Fallthrough) {
            skips.Add("Levelling — max level");
            return false;
        }

        var match = FindOceanPresetForGoal(ocean, OceanFishGoalKind.Levelling, goalId: null);
        if (match == null) {
            skips.Add("Levelling — no matching preset");
            return false;
        }

        preset = match;
        return true;
    }

    private static bool TryMatchLegendaryTier(OceanFishingState ocean, List<string> skips, out CustomPresetConfig preset) {
        preset = null!;

        var forRoute = OceanGoalCatalog.GetLegendariesForRoute(ocean.CurrentRoute).ToList();
        if (forRoute.Count == 0) {
            skips.Add("Legendary — none on this route");
            return false;
        }

        var status = string.Join(", ", forRoute.Select(f =>
            $"#{f.FishParameterId} {(OceanGoalCatalog.IsLegendaryCaught(f.FishParameterId) ? "caught" : "uncaught")}"));

        var skipIfAcquired = Configuration.C.AOF_Fallthrough;
        var eligible = OceanGoalCatalog.GetEligibleLegendaryIds(ocean.CurrentRoute, skipIfAcquired);
        if (eligible.Count == 0) {
            skips.Add($"Legendary — already caught ({status})");
            return false;
        }

        var match = FindOceanPresetForGoal(ocean, OceanFishGoalKind.Legendary, goalId: null);
        if (match == null) {
            skips.Add($"Legendary — no matching preset (still need {string.Join(",", eligible)}; {status})");
            return false;
        }

        preset = match;
        return true;
    }

    private static void ApplyOceanPresetChoice(WorldState ws, string about, List<string> skips, CustomPresetConfig match, OceanFishGoalKind tier) {
        var presets = RodFishingModule.Presets;
        if (match.IsGlobal) {
            var alreadyGlobal = presets.SelectedPreset == null;
            if (!alreadyGlobal)
                presets.Select(null, FishingPresets.ReasonAutoOceanFish);
            ws.Decide(DecisionContext.OceanPreset, true, alreadyGlobal ? $"Already on global ({tier})" : $"Selected global ({tier})", JoinDetail(about, skips), AutoHook.GlobalPresetName);
            return;
        }

        var alreadySelected = presets.SelectedPreset?.UniqueId == match.UniqueId;
        if (!alreadySelected)
            presets.Select(match, FishingPresets.ReasonAutoOceanFish);

        ws.Decide(DecisionContext.OceanPreset, true, alreadySelected ? $"Already on {match.PresetName} ({tier})" : $"Selected {match.PresetName} ({tier})", JoinDetail(about, skips), match.PresetName);
    }

    private static string JoinDetail(string about, List<string> skips) {
        if (skips.Count == 0)
            return about;
        return about + "\n" + string.Join("\n", skips);
    }

    private static CustomPresetConfig? FindOceanPresetForGoal(OceanFishingState ocean, OceanFishGoalKind tier, uint? goalId) {
        foreach (var preset in EnumerateHookPresets()) {
            if (!MatchesOceanBase(preset.ExtraCfg, ocean))
                continue;
            if (preset.ExtraCfg.AutoOceanFishGoal != tier)
                continue;
            if (goalId is { } id && preset.ExtraCfg.AutoOceanFishGoalId != id)
                continue;
            return preset;
        }

        return null;
    }

    private static bool MatchesOceanBase(ExtraConfig extra, OceanFishingState ocean) {
        if (!extra.AutoOceanFishEnabled)
            return false;
        if (!extra.AutoOceanFishAllStops && !OceanStopUtil.MatchesStop(extra.AutoOceanFishSpotId, extra.AutoOceanFishTimeId, ocean))
            return false;
        return !(extra.AutoOceanFishConditionSet is { } set) || !set.HasAnyCondition() || !set.Fails(WorldState.Get());
    }

    internal static IEnumerable<CustomPresetConfig> EnumerateHookPresets() {
        yield return RodFishingModule.Presets.DefaultPreset;
        foreach (var preset in RodFishingModule.Presets.CustomPresets)
            yield return preset;
    }
}
