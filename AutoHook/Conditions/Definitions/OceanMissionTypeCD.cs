using Lumina.Excel.Sheets;

namespace AutoHook.Conditions.Definitions;

public sealed class OceanMissionTypeCD : IConditionDefinition {
    public string Id => nameof(OceanMissionTypeCD);
    public string Name => "Ocean mission type";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    public string? IncompleteReason(IReadOnlyDictionary<string, object> parameters)
        => GetIds(parameters).Count == 0 ? "select a mission" : null;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var ids = GetIds(parameters);
        if (ids.Count == 0) return false;
        var of = world.OceanFishing;
        var result = ids.Contains(of.Mission1.Type) || ids.Contains(of.Mission2.Type) || ids.Contains(of.Mission3.Type);
        return GetBool(parameters, "inv", false) ? !result : result;
    }

    public void DrawParams(Condition condition) {
        var ids = GetIds(condition.Params);
        var currentId = ids.Count > 0 ? ids[0] : 0;

        static string LabelForRow(uint rowId) {
            if (rowId == 0) return "Select mission";
            if (!IKDPlayerMissionCondition.TryGetRow(rowId, out var row)) return $"{rowId}";
            var name = row.Unknown0.ToString();
            return string.IsNullOrEmpty(name) ? $"{rowId}" : $"{rowId}: {name}";
        }

        var missions = IKDPlayerMissionCondition.Where(row => !row.Unknown0.ToString().IsEmpty).Select(row => (Id: row.RowId, Name: row.Unknown0.ToString())).OrderBy(x => x.Name).ToList();
        var label = LabelForRow(currentId);
        DrawUtil.DrawComboSelector(missions, m => $"{m.Id}: {m.Name}", label, m => { condition.Params["ids"] = new List<object> { (long)m.Id }; });
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters) {
        var ids = GetIds(parameters);
        if (ids.Count == 0)
            return "any mission";
        var id = ids[0];
        if (IKDPlayerMissionCondition.TryGetRow(id, out var row)) {
            var name = row.Unknown0.ToString();
            return string.IsNullOrEmpty(name) ? $"mission type {id}" : $"{id}: {name}";
        }
        return $"mission type {id}";
    }
}
