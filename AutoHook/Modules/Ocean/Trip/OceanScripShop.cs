using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Runtime.CompilerServices;

namespace AutoHook.Modules.Ocean;

public static class OceanScripShop {
    public const uint VendorId = 1005423;
    public const uint ShopId = 3801095;
    public const int MenuIndex = 0;
    private const string AddonName = "InclusionShop";
    private const string ConfirmAddonName = "ShopExchangeItemDialog";

    public sealed record ScripItem(uint ItemId, string Name, uint Icon, uint CategoryRow, string Category, string SubCategory, uint Cost, uint CurrencySpecialId, uint ReceiveCount);

    public static IReadOnlyList<ScripItem> Items => field ??= BuildCatalog();
    public static ScripItem? Find(uint itemId) => Items.FirstOrDefault(i => i.ItemId == itemId);

    public static IReadOnlyList<ScripItem> OceanBaits {
        get {
            if (field == null && FishBaitCatalog.Get().IsReady)
                field = [.. Items.Where(i => FishBaitCatalog.Get().Fishes.Any(f => f.IsOceanFish && f.InitialBait == i.ItemId))];
            return field ?? [];
        }
    }

    public static List<uint> BaitsFor(uint routeId) {
        var sold = Items.Select(i => i.ItemId).ToHashSet();
        return [.. OceanRouteFishCatalog.GetFish(routeId)
            .Where(f => !OceanGoalCatalog.IsLegendaryCaught(f.FishParameterId))
            .Select(f => FishBaitCatalog.Get()[f.ItemId]?.InitialBait ?? 0)
            .Where(b => b > 0 && sold.Contains((uint)b))
            .Select(b => (uint)b)
            .Distinct()];
    }

    private static List<ScripItem> BuildCatalog() {
        var result = new List<ScripItem>();
        if (!InclusionShop.TryGetRow(ShopId, out var shop))
            return result;
        var seriesSheet = IDataManager.Get().GetSubrowExcelSheet<InclusionShopSeries>();
        foreach (var categoryRef in shop.Category) {
            if (categoryRef.RowId == 0 || !categoryRef.IsValid)
                continue;
            var category = categoryRef.Value;
            if (!seriesSheet.TryGetRow(category.InclusionShopSeries.RowId, out var subrows))
                continue;
            foreach (var series in subrows) {
                if (!series.SpecialShop.IsValid)
                    continue;
                var special = series.SpecialShop.Value;
                foreach (var entry in special.Item) {
                    var give = entry.ReceiveItems[0];
                    var cost = entry.ItemCosts[0];
                    if (give.Item.RowId == 0 || !give.Item.IsValid)
                        continue;
                    result.Add(new ScripItem(give.Item.RowId, give.Item.Value.Name.ToString(), give.Item.Value.Icon,
                        categoryRef.RowId, category.Name.ToString(), special.Name.ToString(),
                        cost.CurrencyCost, cost.CostType == 3 ? cost.ItemCost.RowId : 0, Math.Max(give.ReceiveCount, 1)));
                }
            }
        }
        return result;
    }

    public static unsafe uint CurrencyItemId(uint specialId) {
        var currency = CurrencyManager.Instance();
        return currency == null || specialId == 0 ? 0 : currency->GetItemIdBySpecialId((byte)specialId);
    }

    #region Shop window

    private static unsafe AgentInclusionShop.AgentData* Data {
        get {
            var agent = AgentInclusionShop.Instance();
            return agent == null ? null : agent->Data;
        }
    }

    public static unsafe bool IsOpen => Ready(AddonName) != null;
    public static unsafe bool IsReady => IsOpen && Data != null && Data->InclusionShopId == ShopId && Data->IsShopReady && !Data->NeedsAddonRefresh;

    public static unsafe void Close() {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName(AddonName);
        if (addon != null)
            addon->Close(true);
    }

    // the dropdown entries, the one Lumina says holds the item first
    public static unsafe List<byte> CategoryOrder(uint itemId) {
        var data = Data;
        if (data == null)
            return [];
        var hint = Find(itemId)?.CategoryRow ?? 0;
        var order = Enumerable.Range(0, data->CategoryCount).Select(i => (byte)i).ToList();
        return [.. order.OrderBy(i => CategoryAt(data, i) is var c && c != null && (c->InclusionShopRowId == hint || c->InclusionShopSeriesId == hint) ? 0 : 1)];
    }

    public static unsafe byte SelectedCategory => Data == null ? byte.MaxValue : Data->SelectedCategoryIndex;

    public static unsafe void SelectCategory(byte index) {
        var agent = AgentInclusionShop.Instance();
        if (agent != null)
            agent->SelectCategory(index);
    }

    // the subcategory tab holding the item in the selected category, -1 if it isn't there (or isn't loaded yet)
    public static unsafe int FindTab(uint itemId) {
        var data = Data;
        if (data == null)
            return -1;
        var category = CategoryAt(data, data->SelectedCategoryIndex);
        if (category == null || category->SubCategories == null)
            return -1;
        for (var tab = 0; tab < data->VisibleSubCategoryCount; tab++) {
            var sub = data->SubCategoryIndexMap[tab];
            if (sub < category->SubCategoryCount && FindItem(&category->SubCategories[sub], itemId) != null)
                return tab;
        }
        return -1;
    }

    public static unsafe bool SelectTab(int tab) => Data != null && Data->SelectSubCategory((byte)tab);

    // how many the scrips on hand pay for, from the selected subcategory
    public static unsafe int Affordable(uint itemId) {
        var data = Data;
        if (data == null)
            return 0;
        var category = CategoryAt(data, data->SelectedCategoryIndex);
        if (category == null || category->SubCategories == null || data->SelectedSubCategoryIndex >= category->SubCategoryCount)
            return 0;
        var item = FindItem(&category->SubCategories[data->SelectedSubCategoryIndex], itemId);
        if (item == null || item->CostAmount[0] == 0)
            return 0;
        return (int)(item->CostOwnedCount[0] / item->CostAmount[0]);
    }

    // position of the item in the window's list, -1 if not listed
    public static unsafe int ListIndex(uint itemId, out int maxAmount) {
        maxAmount = 0;
        var addon = (AddonInclusionShop*)Ready(AddonName);
        if (addon == null || addon->AtkValuesCount < 300)
            return -1;
        var values = addon->TypedAtkValues;
        var count = Math.Min(AsInt(values->ItemCount), values->Items.Length);
        for (var i = 0; i < count; i++) {
            if ((uint)AsInt(values->Items[i].ItemId) != itemId)
                continue;
            maxAmount = AsInt(values->Items[i].MaxAmount);
            return i;
        }
        return -1;
    }

    public static unsafe void Buy(int listIndex, int amount) {
        if (Ready(AddonName) is var addon && addon != null)
            FireCallback(addon, true, 14, listIndex, amount);
    }

    public static unsafe bool ConfirmOpen => Ready(ConfirmAddonName) != null;

    public static unsafe void Confirm() {
        if (Ready(ConfirmAddonName) is var addon && addon != null)
            FireCallback(addon, true, 0);
    }

    private static unsafe AgentInclusionShop.Category* CategoryAt(AgentInclusionShop.AgentData* data, int uiIndex) {
        if (uiIndex < 0 || uiIndex >= data->CategoryCount)
            return null;
        var index = data->CategoryIndexMap[uiIndex];
        return index < data->Categories.Length ? (AgentInclusionShop.Category*)Unsafe.AsPointer(ref data->Categories[index]) : null;
    }

    private static unsafe AgentInclusionShop.Item* FindItem(AgentInclusionShop.SubCategory* sub, uint itemId) {
        for (var i = 0; i < sub->ItemCount && i < sub->Items.Length; i++) {
            if (sub->Items[i].GiveItemId[0] == itemId)
                return (AgentInclusionShop.Item*)Unsafe.AsPointer(ref sub->Items[i]);
        }
        return null;
    }

    private static unsafe AtkUnitBase* Ready(string name) {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName(name);
        return addon != null && addon->IsVisible && addon->IsReady ? addon : null;
    }

    private static int AsInt(AtkValue value) => value.Type switch {
        AtkValueType.UInt => (int)value.UInt,
        AtkValueType.Int => value.Int,
        _ => 0,
    };

    private static unsafe void FireCallback(AtkUnitBase* addon, bool update, params int[] args) {
        var values = stackalloc AtkValue[args.Length];
        for (var i = 0; i < args.Length; i++) {
            values[i] = default;
            values[i].SetInt(args[i]);
        }
        addon->FireCallback((uint)args.Length, values, update);
    }

    #endregion
}
