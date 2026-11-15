namespace AutoHook.Conditions.Definitions;

public sealed class OceanLastFishPointsCD : IntCompareConditionDefinition {
    public override string Name => "Last ocean fish points";
    public override ConditionScopeFlags AllowedScopes
        => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    protected override string ValueLabel => "Points";
    protected override int DefaultValue => 300;
    protected override Func<int, int>? Clamp => static v => Math.Max(0, v);

    protected override bool? InactiveResult(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        if (GetLastOceanFishPointsValue(world) != null)
            return null;

        var args = GetIntCompareParams(parameters, valueKey: ValueKey, defaultValue: DefaultValue, defaultOp: DefaultOp);
        return args.Invert;
    }

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => GetLastOceanFishPointsValue(world) ?? 0;

    private static int? GetLastOceanFishPointsValue(WorldState w) {
        var of = w.OceanFishing;
        if (of.FishData == null || of.FishData.Count < 60) return null;
        var zone = (int)Math.Clamp(of.CurrentZone, 0, 2);
        var start = zone * 20;
        for (var i = start + 19; i >= start; i--) {
            var f = of.FishData[i];
            if (f.ItemId == 0) continue;
            var count = f.NqAmount + f.HqAmount;
            if (count == 0) return (int)f.TotalPoints;
            return (int)(f.TotalPoints / count);
        }
        return null;
    }
}
