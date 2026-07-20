using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace AutoHook.Utils;

public static class GameRes {
    public const uint FishingTackleRow = 30;
    public const int AllBaitsId = -99;
    public const int AllMoochesId = -98;

    public static List<BaitFishClass> Baits { get; private set; } = [];
    public static List<BaitFishClass> Fishes { get; private set; } = [];
    public static List<BaitFishClass> LureFishes { get; private set; } = [];
    public static List<BaitFishClass> MoochableFish { get; private set; } = [];
    public static List<ImportedFish> ImportedFishes { get; private set; } = [];
    public static List<ImportedFish> SpearfishFishes { get; private set; } = [];
    public static List<uint> FishingStatuses { get; private set; } = [];

    public static void Initialize() {
        FishingStatuses = [.. typeof(IDs.Status).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => f.GetValue(null))
            .OfType<uint>()
            .Where(id => id != 0)
            .OrderBy(id => id)];

        Baits = [.. FindRows<Item>(i => i.ItemSearchCategory.RowId == FishingTackleRow).ToList()
            .Concat([.. FindRows<WKSItemInfo>(i => i.WKSItemSubCategory.RowId == 5).Select(i => i.Item.Value)])
            .Select(b => new BaitFishClass(b))];

        Fishes = FindRows<FishParameter>(f => f.Item.RowId is not 0 and < 1000000)
            .Select(f => new BaitFishClass(f)).GroupBy(f => f.Id).Select(group => group.First()).ToList() ?? [];

        LureFishes = [.. Fishes.Where(f => f.LureMessage != "")];

        MoochableFish = FindRows<FishingBaitParameter>(x => x.Item.Value.ItemUICategory.RowId != 33).Select(f => new BaitFishClass(f.Item.RowId)).ToList() ?? [];

        try {
            var fishList = Path.Combine(Svc.Interface.AssemblyLocation.DirectoryName!, $"Data\\FishData\\fish_list.json");

            if (File.Exists(fishList)) {
                ImportedFishes = JsonSerializer.Deserialize<List<ImportedFish>>(File.ReadAllText(fishList))!;
            }

            // fish_list is wrong when it comes to most timeworn maps not being spearfish so build a list of actual spearfish and match fish_list to it
            SpearfishFishes =
            [
                .. SpearfishingItem
                    .Where(row => row.Item.RowId != 0)
                    .Join(ImportedFishes, row => (int)row.Item.RowId, f => f.ItemId, (_, match) => new ImportedFish {
                        ItemId = match.ItemId,
                        IsSpearFish = true,
                        Size = match.Size,
                        Speed = match.Speed,
                    })
            ];
        }
        catch (Exception e) {
            ImGui.SetClipboardText(e.Message);
            Svc.Log.Error(e, "[GameRes] Init failed.");
        }
    }
}
