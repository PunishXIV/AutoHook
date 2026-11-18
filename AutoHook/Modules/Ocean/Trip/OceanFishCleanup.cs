using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace AutoHook.Modules.Ocean;

public readonly record struct FishSlot(InventoryType Container, int Slot, uint ItemId, uint Quantity);

public static unsafe class OceanFishCleanup {
    // https://github.com/PunishXIV/AutoRetainer/blob/main/AutoRetainer/Internal/Memory.cs
    // TODO: add to CS
    private const string SellItemSig = "48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 8B F2 8B E9";
    private static nint _sellItemAddress = -1;

    // every fish caught in ocean fishing, both lines
    public static HashSet<uint> OceanFish => field ??= [.. IKDSpot.Rows
        .SelectMany(s => new[] { s.SpotMain.RowId, s.SpotSub.RowId })
        .Where(id => id != 0)
        .Distinct()
        .SelectMany(id => FishingSpot.TryGetRow(id, out var spot) ? spot.Item.Select(i => i.RowId) : [])
        .Where(id => id != 0)];

    // never sold or desynthed
    public static HashSet<uint> Legendaries => field ??= [.. OceanGoalCatalog.Legendaries.Select(l => FishParameter.TryGetRow(l.FishParameterId, out var p) ? p.Item.RowId : 0).Where(id => id != 0)];

    public static bool IsCandidate(uint itemId) => OceanFish.Contains(itemId) && !Legendaries.Contains(itemId);

    public static List<FishSlot> FindFish(Func<Item, bool>? filter = null) {
        var result = new List<FishSlot>();
        var im = InventoryManager.Instance();
        foreach (var type in new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 }) {
            var container = im->GetInventoryContainer(type);
            if (container == null)
                continue;
            for (var i = 0; i < container->Size; i++) {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0 || !IsCandidate(slot->ItemId))
                    continue;
                if (filter != null && (!Item.TryGetRow(slot->ItemId, out var row) || !filter(row)))
                    continue;
                result.Add(new FishSlot(type, i, slot->ItemId, (uint)slot->Quantity));
            }
        }
        return result;
    }

    public static bool CanDesynth(Item item) => item.Desynth > 0 && !item.IsUntradable;

    public static bool CanSell(Item item) => item.PriceLow > 0;

    // desynthesis is unlocked through a DoH quest; a level of 0 means it isn't
    public static bool DesynthUnlocked => GamePlayerState.Instance()->GetDesynthesisLevel(15) > 0; // CUL

    public static bool SlotHolds(FishSlot slot) {
        var container = InventoryManager.Instance()->GetInventoryContainer(slot.Container);
        if (container == null)
            return false;
        var item = container->GetInventorySlot(slot.Slot);
        return item != null && item->ItemId == slot.ItemId && item->Quantity > 0;
    }

    public static bool Desynth(FishSlot slot) {
        var agent = AgentSalvage.Instance();
        var container = InventoryManager.Instance()->GetInventoryContainer(slot.Container);
        if (agent == null || container == null)
            return false;
        var item = container->GetInventorySlot(slot.Slot);
        if (item == null || item->ItemId != slot.ItemId)
            return false;
        agent->SalvageItem(item);
        return true;
    }

    public static bool SellItemAvailable => ResolveSellItem() != 0;

    public static bool Sell(FishSlot slot) {
        var address = ResolveSellItem();
        if (address == 0 || !TryGetAddonByName<AtkUnitBase>("Shop", out var shop) || !IsAddonReady(shop) || !SlotHolds(slot))
            return false;
        ((delegate* unmanaged<uint, InventoryType, uint, void>)address)((uint)slot.Slot, slot.Container, 0);
        return true;
    }

    private static nint ResolveSellItem() {
        if (_sellItemAddress == -1)
            _sellItemAddress = ISigScanner.Get().TryScanText(SellItemSig, out var address) ? address : 0;
        return _sellItemAddress;
    }
}
