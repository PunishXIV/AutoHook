using ECommons.Throttlers;
using Lumina.Excel.Sheets;
using System.Collections.Immutable;

namespace AutoHook.Modules.Ocean;

public enum OceanFishGoalKind {
    Points = 1,
    Legendary = 2,
    Achievement = 3,
    Levelling = 4,
}

public sealed record OceanLegendaryDef(uint FishParameterId, ImmutableArray<uint> RouteIds);
public sealed record OceanAchievementDef(uint AchievementId, int MinPartySize, ImmutableArray<uint> RouteIds);

public static class OceanGoalCatalog {
    public static readonly ImmutableArray<OceanLegendaryDef> Legendaries = [
        new(904, [2, 6]),        // Sothis
        new(905, [3, 5]),        // Coral Manta
        new(906, [6, 10]),       // Stonescale
        new(907, [2, 9]),        // Elasmosaurus
        new(990, [9, 12]),       // Hafgufa
        new(991, [8]),           // Seafaring Toad
        new(992, [12]),          // Placodus
        new(1253, [13, 16, 21]), // Taniwha
        new(1254, [14, 17]),     // Glass Dragon
        new(1255, [18]),         // Hells' Claw
        new(1256, [14]),         // Jewel of the Plum Spring
        new(1491, [20]),         // Akupara
        new(1511, [19]),         // Manasvin
    ];

    public static readonly ImmutableArray<OceanAchievementDef> Achievements = [
        new(2563, 7, [1]),       // Octopodes
        new(2564, 7, [5]),       // Shark
        new(2565, 7, [4]),       // Jellyfish
        new(2566, 7, [3]),       // Seahorse
        new(2754, 7, [10, 11]),  // Fugu
        new(2755, 7, [8]),       // Crabs
        new(2756, 1, [7, 11]),   // Mantas
        new(3267, 7, [13, 15]),  // Shellfish
        new(3268, 7, [16, 17]),  // Squid
        new(3269, 1, [15, 18]),  // Shrimp
        new(3975, 7, [20]),      // Prehistoric
        new(3976, 1, [19, 21]),  // Stomatopods
    ];

    public static IEnumerable<OceanFishGoalKind> GetCascade(OceanFishGoalKind settingsGoal) {
        switch (settingsGoal) {
            case OceanFishGoalKind.Achievement:
                yield return OceanFishGoalKind.Achievement;
                yield return OceanFishGoalKind.Levelling;
                yield return OceanFishGoalKind.Legendary;
                yield return OceanFishGoalKind.Points;
                break;
            case OceanFishGoalKind.Levelling:
                yield return OceanFishGoalKind.Levelling;
                yield return OceanFishGoalKind.Achievement;
                yield return OceanFishGoalKind.Legendary;
                yield return OceanFishGoalKind.Points;
                break;
            case OceanFishGoalKind.Legendary:
                yield return OceanFishGoalKind.Legendary;
                yield return OceanFishGoalKind.Levelling;
                yield return OceanFishGoalKind.Achievement;
                yield return OceanFishGoalKind.Points;
                break;
            default:
                yield return OceanFishGoalKind.Points;
                break;
        }
    }

    private static readonly Dictionary<uint, IKDContentBonus?> AchievementBonuses = [];

    // this is hacky and idk if it works in all languages but good enough!
    public static IKDContentBonus? GetAchievementBonus(uint achievementId) {
        if (AchievementBonuses.TryGetValue(achievementId, out var cached))
            return cached;

        IKDContentBonus? found = null;
        if (Achievement.TryGetRow(achievementId, out var achievement)) {
            var description = achievement.Description.ToString();
            foreach (var bonus in IKDContentBonus.Rows) {
                var objective = bonus.Objective.ToString();
                if (objective.Length > 0 && description.Contains(objective, StringComparison.OrdinalIgnoreCase)) {
                    found = bonus;
                    break;
                }
            }
        }
        return AchievementBonuses[achievementId] = found;
    }

    public static IEnumerable<OceanLegendaryDef> GetLegendariesForRoute(uint routeId)
        => Legendaries.Where(d => d.RouteIds.Contains(routeId));

    public static IEnumerable<OceanAchievementDef> GetAchievementsForRoute(uint routeId)
        => Achievements.Where(d => d.RouteIds.Contains(routeId)).OrderByDescending(d => d.MinPartySize).ThenBy(d => d.AchievementId);

    public static unsafe List<uint> GetEligibleLegendaryIds(uint routeId, bool skipIfAcquired = true)
        => [.. GetLegendariesForRoute(routeId)
            .Where(f => !skipIfAcquired || !IsLegendaryCaught(f.FishParameterId))
            .Select(f => f.FishParameterId)];

    public static unsafe bool IsLegendaryCaught(uint fishParameterId)
        => GamePlayerState.Instance()->IsFishCaught(fishParameterId);

    public static unsafe bool IsLevellingNeeded()
        => IPlayerState.Get().GetClassJobLevel(ClassJob.GetRow(18)) < GamePlayerState.Instance()->MaxLevel;

    public static List<uint> GetEligibleAchievementIds(uint routeId, bool skipIfAcquired = true) {
        var partySize = Math.Max(1, WorldState.Get().Party.QueuedWithContentIds.Count);
        // treat unk as incomplete so z1 doesn't skip past Achievements before the server response
        return [.. GetAchievementsForRoute(routeId)
            .Where(def => partySize >= def.MinPartySize
                          && (!skipIfAcquired || IsAchievementIncomplete(def.AchievementId) != false))
            .Select(def => def.AchievementId)];
    }

    public static unsafe bool? IsAchievementIncomplete(uint achievementId) {
        if (WorldState.Get().AchievementProgress.TryGetValue(achievementId, out var progress) && progress.Max > 0)
            return progress.Current < progress.Max;

        var ach = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.Instance();
        if (ach == null)
            return null;
        if (ach->IsLoaded())
            return !ach->IsComplete((int)achievementId);

        if (EzThrottler.Throttle($"OceanAch{achievementId}", 3000))
            ach->RequestAchievementProgress(achievementId);
        return null;
    }

    public static void PrefetchRouteAchievements(uint routeId) => GetAchievementsForRoute(routeId).ForEach(def => IsAchievementIncomplete(def.AchievementId));
}
