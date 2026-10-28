using LuminaAction = Lumina.Excel.Sheets.Action;

namespace AutoHook.Conditions.Definitions;

public sealed class LastActionUsedCD : IConditionDefinition {
    public string Id => nameof(LastActionUsedCD);
    public string Name => "Last action used";
    public ConditionScopeFlags AllowedScopes
        => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast | ConditionScopeFlags.Spearfishing;

    private readonly record struct LastActionUsedParams(uint Id, bool Invert) {
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

    public string? IncompleteReason(IReadOnlyDictionary<string, object> parameters)
        => GetUInt(parameters, "id", 0) == 0 ? "select an action" : null;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var args = GetParams(parameters);
        if (args.Id == 0)
            return args.Invert;

        var last = world.Fishing.LastUsedAction;
        var result = last is { ActionId: var id } && id == args.Id;
        return args.Apply(result);
    }

    public void DrawParams(Condition condition) {
        var args = GetParams(condition.Params);
        var currentId = args.Id;
        var selectedLabel = currentId != 0 ? $"{currentId}: {LuminaAction.GetRow(currentId).Name}" : "Select action";

        var actions = typeof(IDs.Actions).GetFields()
            .Select(f => f.GetValue(null))
            .OfType<uint>()
            .Where(id => id != 0)
            .Select(id => (Id: id, Name: $"{id}: {LuminaAction.GetRow(id).Name}"))
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .OrderBy(x => x.Name)
            .ToList();

        DrawUtil.DrawComboSelector(
            actions,
            a => a.Name,
            selectedLabel,
            a => condition.Params = (args with { Id = a.Id }).ToParams());
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters) {
        var id = GetUInt(parameters, "id", 0);
        return id == 0 ? "any action" : LuminaAction.GetRow(id).Name.ToString();
    }

    private static LastActionUsedParams GetParams(IReadOnlyDictionary<string, object> p) {
        var id = GetUInt(p, "id", 0);
        var inv = GetBool(p, "inv", false);
        return new LastActionUsedParams(id, inv);
    }
}
