using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace AutoHook.Conditions.Definitions;

public sealed class BaitCountCD : IConditionDefinition {
    public string Id => nameof(BaitCountCD);
    public string Name => "Bait count";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var baitId = GetInt(parameters, "id", 0);
        var args = GetIntCompareParams(parameters);
        if (baitId <= 0)
            return args.Invert;

        var result = CompareInt(world.Player.GetItemCount((uint)baitId), args.Value, args.Op);
        return args.Apply(result);
    }

    public void DrawParams(Condition condition) {
        var baitId = GetInt(condition.Params, "id", 0);
        var currentBait = FishBaitCatalog.Get().Baits.FirstOrDefault(b => b.Id == baitId);
        var selectedName = currentBait is { Id: > 0 }
            ? $"[#{currentBait.Id}] {currentBait.Name}"
            : "-";

        DrawUtil.DrawComboSelector(FishBaitCatalog.Get().Baits, bait => $"[#{bait.Id}] {bait.Name}", selectedName, bait => condition.Params["id"] = (long)bait.Id);

        ImGui.SameLine();
        DrawIntCompareParams(condition, "##baitcount_op", "Count", clamp: v => Math.Max(0, v));
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters) {
        var baitId = GetInt(parameters, "id", 0);
        var bait = baitId > 0 ? Item.GetRow((uint)baitId).Name.ToString() : "any bait";
        return $"{bait} count {ConditionParameterFormat.FormatIntCompare(parameters)}";
    }

    public bool DescribeOmitsName => true;
}
