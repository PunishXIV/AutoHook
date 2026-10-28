using Lumina.Excel.Sheets;

namespace AutoHook.Conditions.Definitions;

public sealed class OceanRouteCD : IConditionDefinition {
    public string Id => nameof(OceanRouteCD);
    public string Name => "Ocean route";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    public string? IncompleteReason(IReadOnlyDictionary<string, object> parameters)
        => GetIds(parameters).Count == 0 ? "select a route" : null;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var ids = GetIds(parameters);
        if (ids.Count == 0) return false;
        var route = world.OceanFishing.CurrentRoute;
        var result = ids.Contains(route);
        return GetBool(parameters, "inv", false) ? !result : result;
    }

    public void DrawParams(Condition condition) {
        var ids = GetIds(condition.Params);
        var currentId = ids.Count > 0 ? ids[0] : 0;

        var unique = new Dictionary<string, uint>();
        foreach (var row in IKDRoute.Where(r => r.RowId != 0 && !r.Name.IsEmpty)) {
            if (!unique.ContainsKey(row.Name.ToString()))
                unique[row.Name.ToString()] = row.RowId;
        }

        var routes = unique.OrderBy(k => k.Key).Select(k => (Id: k.Value, Name: k.Key)).ToList();
        var label = currentId != 0 && IKDRoute.TryGetRow(currentId, out var currentRow) ? $"{currentRow.RowId}: {currentRow.Name}" : "Select route";

        DrawUtil.DrawComboSelector(routes, r => $"{r.Id}: {r.Name}", label, r => { condition.Params["ids"] = new List<object> { (long)r.Id }; });
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters) {
        var ids = GetIds(parameters);
        if (ids.Count == 0)
            return "any route";
        var id = ids[0];
        return IKDRoute.TryGetRow(id, out var row) ? $"{row.RowId}: {row.Name}" : $"route {id}";
    }
}
