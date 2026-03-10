using System.IO;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace AutoHook.Utils;

public static class GameRes
{
    public const uint FishingTackleRow = 30;
    public const int AllBaitsId = -99;
    public const int AllMoochesId = -98;

    public static List<BaitFishClass> Baits { get; private set; } = [];
    public static List<BaitFishClass> Fishes { get; private set; } = [];
    public static List<BaitFishClass> LureFishes => [.. Fishes.Where(f => f.LureMessage != "")];
    public static List<BaitFishClass> MoochableFish { get; private set; } = [];
    public static List<ImportedFish> ImportedFishes { get; private set; } = [];

    public static void Initialize()
    {
        Baits = [.. FindRows<Item>(i => i.ItemSearchCategory.RowId == FishingTackleRow).ToList()
            .Concat([.. FindRows<WKSItemInfo>(i => i.WKSItemSubCategory.RowId == 5).Select(i => i.Item.Value)])
            .Select(b => new BaitFishClass(b))];

        Fishes = FindRows<FishParameter>(f => f.Item.RowId is not 0 and < 1000000)
            .Select(f => new BaitFishClass(f)).GroupBy(f => f.Id).Select(group => group.First()).ToList() ?? [];

        MoochableFish = FindRows<FishingBaitParameter>(x => x.Item.Value.ItemUICategory.RowId != 33).Select(f => new BaitFishClass(f.Item.RowId)).ToList() ?? [];

        try
        {
            var fishList = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName!,
                $"Data\\FishData\\fish_list.json");

            if (File.Exists(fishList))
            {
                var json = File.ReadAllText(fishList);

                ImportedFishes = JsonSerializer.Deserialize<List<ImportedFish>>(json)!;
            }
        }
        catch (Exception e)
        {
            ImGui.SetClipboardText(e.Message);
            Svc.Log.Error($"{e.Message}");
        }
    }
}
