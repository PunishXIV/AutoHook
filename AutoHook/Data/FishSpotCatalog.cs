using Lumina.Excel.Sheets;
using System.Threading;

namespace AutoHook.Data;

public sealed class FishSpotCatalog : IPluginService {
    public readonly record struct SpearfishingSpotRef(uint GatheringPointId, uint GatheringPointBaseId, uint NotebookId, bool IsShadowNode);
    public readonly record struct SpearfishingPoolRef(uint NotebookId, uint GatheringPointBaseId, string Name, bool IsShadowNode, IReadOnlyList<uint> ItemIds);

    private Dictionary<uint, SpearfishingPoolRef> _poolsByNotebookId = [];
    private Dictionary<uint, IReadOnlyList<uint>> _notebookIdsByItemId = [];
    private Dictionary<uint, SpearfishingSpotRef> _spotsByPointId = [];

    public async Task InitializeAsync(CancellationToken cancellationToken = default) {
        var built = await Task.Run(() => Build(cancellationToken), cancellationToken).ConfigureAwait(true);
        _poolsByNotebookId = built.Pools;
        _notebookIdsByItemId = built.NotebooksByItem;
        _spotsByPointId = built.Spots;
    }

    public bool TryGetPool(uint notebookId, out SpearfishingPoolRef pool)
        => _poolsByNotebookId.TryGetValue(notebookId, out pool);

    public string GetPoolName(uint notebookId)
        => notebookId == 0 ? "Any Pool" : _poolsByNotebookId.TryGetValue(notebookId, out var pool) ? pool.Name : $"Unknown Pool #{notebookId}";

    public IEnumerable<uint> PoolNotebookIds
        => _poolsByNotebookId.Keys.Where(id => id != 0);

    public bool TryGetSpot(uint gatheringPointId, out SpearfishingSpotRef spot)
        => _spotsByPointId.TryGetValue(gatheringPointId, out spot);

    public IReadOnlyList<uint> GetNotebookIdsForItem(uint itemId)
        => _notebookIdsByItemId.TryGetValue(itemId, out var ids) ? ids : [];

    public static uint FindFishingSpot(uint placeNameId, uint territoryId) {
        if (territoryId == 0)
            return 0;
        var matches = FishingSpot.Rows.Where(spot => spot.PlaceName.RowId == placeNameId && spot.TerritoryType.RowId == territoryId).Take(2).ToList();
        return matches.Count == 1 ? matches[0].RowId : 0;
    }

    public static IReadOnlyList<Fish> GetFishAtSpot(uint fishingSpotId) {
        if (fishingSpotId == 0 || !FishingSpot.TryGetRow(fishingSpotId, out var spot))
            return [];

        var catalog = FishBaitCatalog.Get();
        return [.. spot.Item.Where(item => item.RowId != 0).Select(item => catalog[item.RowId] ?? new Fish { Id = (int)item.RowId, Name = item.ValueNullable?.Name.ToString() ?? $"#{item.RowId}" })];
    }

    public static string GetFishingSpotName(uint fishingSpotId)
        => FishingSpot.TryGetRow(fishingSpotId, out var spot) && spot.PlaceName.ValueNullable is { } place ? place.Name.ToString() : "-";

    public IReadOnlyList<Fish> GetSpearFishesInPool(uint notebookId) {
        var catalog = FishBaitCatalog.Get();
        if (notebookId == 0 || !TryGetPool(notebookId, out var pool))
            return catalog.SpearFishes;

        return [.. pool.ItemIds.Select(id => catalog[id]).Where(f => f != null).Select(f => f!)];
    }

    private static BuiltSpots Build(CancellationToken ct) {
        ct.ThrowIfCancellationRequested();

        var spearfishingRows = SpearfishingItem.Where(row => row.Item.RowId != 0).ToList();
        var itemIdBySpearfishingRowId = spearfishingRows.GroupBy(row => row.RowId).ToDictionary(group => group.Key, group => group.First().Item.RowId);
        var pools = new Dictionary<uint, SpearfishingPoolRef>();
        foreach (var notebook in SpearfishingNotebook.Rows) {
            ct.ThrowIfCancellationRequested();
            var baseId = notebook.GatheringPointBase.RowId;
            if (baseId == 0)
                continue;

            var itemIds = notebook.GatheringPointBase.Value.Item
                .Select(item => itemIdBySpearfishingRowId.GetValueOrDefault(item.RowId))
                .Where(itemId => itemId != 0)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            pools[notebook.RowId] = new SpearfishingPoolRef(notebook.RowId, baseId, notebook.PlaceName.Value.Name.ToString(), notebook.IsShadowNode, itemIds);
        }

        var notebooksByItem = pools.Values
            .SelectMany(pool => pool.ItemIds.Select(itemId => (itemId, pool.NotebookId)))
            .GroupBy(entry => entry.itemId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<uint>)[.. group.Select(entry => entry.NotebookId).Distinct().OrderBy(id => id)]);

        var poolByBaseId = pools.Values
            .Where(pool => pool.GatheringPointBaseId != 0)
            .GroupBy(pool => pool.GatheringPointBaseId)
            .ToDictionary(group => group.Key, group => group.First());

        var spots = new Dictionary<uint, SpearfishingSpotRef>();
        foreach (var point in GatheringPoint.Where(r => r.GatheringPointBase.RowId != 0)) {
            var baseId = point.GatheringPointBase.RowId;
            if (!poolByBaseId.TryGetValue(baseId, out var pool))
                continue;
            spots[point.RowId] = new SpearfishingSpotRef(point.RowId, baseId, pool.NotebookId, pool.IsShadowNode);
        }

        return new BuiltSpots(pools, notebooksByItem, spots);
    }

    private readonly record struct BuiltSpots(Dictionary<uint, SpearfishingPoolRef> Pools, Dictionary<uint, IReadOnlyList<uint>> NotebooksByItem, Dictionary<uint, SpearfishingSpotRef> Spots);
}
