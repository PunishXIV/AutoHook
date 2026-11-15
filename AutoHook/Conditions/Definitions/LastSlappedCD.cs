using Lumina.Excel.Sheets;

namespace AutoHook.Conditions.Definitions;

public sealed class LastSlappedCD : IConditionDefinition {
    public string Name => "Last slapped";
    public ConditionScopeFlags AllowedScopes
        => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    private readonly record struct LastSlappedParams(uint Id, bool Invert) {
        public bool Apply(bool result) => Invert ? !result : result;

        public Dictionary<string, object> ToParams() {
            var dict = new Dictionary<string, object>();
            if (Id != 0)
                dict["id"] = (long)Id;
            if (Invert)
                dict["inv"] = true;
            return dict;
        }
    }

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var args = GetParams(parameters);
        var slapped = world.Fishing.SlappedFishId;
        var result = args.Id == 0 ? slapped != 0 : slapped == args.Id; // no fish selected = any
        return args.Apply(result);
    }

    public void DrawParams(Condition condition) {
        var args = GetParams(condition.Params);
        var currentFish = FishBaitCatalog.Get().Fishes.FirstOrDefault(f => f.Id == args.Id);
        var selectedName = currentFish is { Id: > 0 } ? $"[#{currentFish.Id}] {currentFish.Name}" : "Any fish slapped";
        DrawUtil.DrawComboSelector(FishBaitCatalog.Get().Fishes, fish => $"[#{fish.Id}] {fish.Name}", selectedName, fish => condition.Params = (args with { Id = (uint)fish.Id }).ToParams());
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters) {
        var id = GetUInt(parameters, "id", 0);
        return id == 0 ? "any" : Item.GetRow(id).Name.ToString();
    }

    private static LastSlappedParams GetParams(IReadOnlyDictionary<string, object> p) {
        var id = GetUInt(p, "id", 0);
        var inv = GetBool(p, "inv", false);
        return new LastSlappedParams(id, inv);
    }
}
