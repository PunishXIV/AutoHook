namespace AutoHook.Modules.Ocean;

public static class OceanVoyageFilter {
    private static readonly OceanLine[] IndigoOnly = [OceanLine.Indigo];
    private static readonly OceanLine[] RubyOnly = [OceanLine.Ruby];

    public static OceanLine[] Lines {
        get => Configuration.C.OceanTabLine switch {
            OceanLineFilter.Ruby => RubyOnly,
            OceanLineFilter.Both => field,
            _ => IndigoOnly,
        };
    } = [OceanLine.Indigo, OceanLine.Ruby];

    public static bool Matches(OceanVoyage voyage) => Matches(voyage, unknownCountsAsMissing: true);

    public static bool Matches(OceanVoyage voyage, bool unknownCountsAsMissing) {
        var legendaryFilter = Configuration.C.OceanTabLegendaryFilter;
        var achievementFilter = Configuration.C.OceanTabAchievementFilter;
        var missingIndividual = Configuration.C.OceanTabMissingIndividual;
        var missingParty = Configuration.C.OceanTabMissingParty;

        var blueFish = OceanGoalCatalog.GetLegendariesForRoute(voyage.RouteId).ToList();
        var achievements = OceanGoalCatalog.GetAchievementsForRoute(voyage.RouteId).ToList();

        if (legendaryFilter != 0 && blueFish.All(f => f.FishParameterId != legendaryFilter))
            return false;
        if (achievementFilter != 0 && achievements.All(a => a.AchievementId != achievementFilter))
            return false;
        if (Configuration.C.OceanTabMissingLegendary || missingIndividual || missingParty || Configuration.C.OceanTabMissingAnyFish) {
            var fishLeft = Configuration.C.OceanTabMissingLegendary && blueFish.Where(f => legendaryFilter == 0 || f.FishParameterId == legendaryFilter).Any(f => !OceanGoalCatalog.IsLegendaryCaught(f.FishParameterId));
            var achievementsLeft = (missingIndividual || missingParty) && achievements
                .Where(a => achievementFilter == 0 || a.AchievementId == achievementFilter)
                .Any(a => (OceanGoalCatalog.IsPartyAchievement(a) ? missingParty : missingIndividual) && (OceanGoalCatalog.IsAchievementIncomplete(a.AchievementId) ?? unknownCountsAsMissing));
            var uncaughtLeft = Configuration.C.OceanTabMissingAnyFish && OceanRouteFishCatalog.GetFish(voyage.RouteId).Any(f => !OceanGoalCatalog.IsLegendaryCaught(f.FishParameterId));
            if (!fishLeft && !achievementsLeft && !uncaughtLeft)
                return false;
        }
        return true;
    }

    public static OceanVoyage? NextMatching(long now)
        => OceanSchedule.Upcoming(Lines, now).Take(OceanSchedule.RotationLength * Lines.Length * 2).Where(Matches).Cast<OceanVoyage?>().FirstOrDefault();
}
