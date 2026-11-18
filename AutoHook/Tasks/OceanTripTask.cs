using clib.Enums;
using clib.TaskSystem;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Numerics;
using Framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace AutoHook.Tasks;

public enum OceanTripSource {
    Manual,
    Alert,
    FullAuto,
    // AutoRetainer character postprocess
    ArPostprocess,
    // AutoRetainer multi mode paused while idle
    ArMultiMode,
}

public enum OceanTripOriginKind {
    Other,
    Inn,
    Housing,
}

public readonly record struct OceanTripOrigin(OceanTripOriginKind Kind, uint Territory, Vector3 Position, int GearsetIndex);

// limsa -> bait -> register -> voyage -> cleanup -> post steps
public sealed class OceanTripTask(OceanVoyage voyage, OceanTripSource source, System.Action? onFinished = null) : TaskBase, IAutoTaskHooks {
    private const uint FisherJobId = 18;
    private const uint OceanFishingIntendedUse = 46;

    public OceanVoyage Voyage { get; } = voyage;
    public OceanTripSource Source { get; } = source;
    public OceanTripOrigin Origin { get; private set; }

    private uint? _pendingZone;
    private bool _pluginWasEnabled;

    private static OceanTripConfig Cfg => Configuration.C.OceanTrip;
    private static WorldState Ws => WorldState.Get();

    // called by RodFishingModule instead of starting a separate AutoOceanFish task, which would cancel this one
    public void NotifyZoneStarted(uint zoneIndex) => _pendingZone = zoneIndex;
    public long BoardTime => Source is OceanTripSource.Manual or OceanTripSource.Alert ? Voyage.BoardingStart : OceanTripService.StartTime(Voyage);

    protected override async Task Execute() {
        using var scope = BeginScope(nameof(OceanTripTask));
        _pluginWasEnabled = Configuration.C.PluginEnabled;
        Origin = CaptureOrigin();
        Log($"Trip for route {Voyage.RouteId} boarding at {Voyage.BoardingStart}, origin {Origin}");

        try {
            if (OceanTripService.QueuedOceanContent() == 0) {
                await TravelToDock();
                await EnsureFisher();
                if (Cfg.EquipRecommended)
                    await EquipRecommended();
                if (Cfg.Repair)
                    await RepairGear();
                if (Cfg.BuyBait)
                    await BuyBait();
                if (Cfg.BuyBait && Cfg.BuyScripBait)
                    await BuyScripBait();
                await WaitForBoardTime();
            }
            else {
                await EnsureFisher();
            }
            await Register();
            await RideVoyage();
            await Cleanup();
            if (Cfg.SpendScrips)
                await SpendScrips();
            await RunPostSteps();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception) when (InOtherDuty) {
            Warning("Another duty took over, stopping the trip");
            IChatGui.Get().Print(OceanTripService.ChatPrefix + string.Format(UIStrings.OceanTrip_Stopped, UIStrings.OceanTrip_ReasonOtherDuty));
        }
        catch (Exception e) {
            IChatGui.Get().PrintError(OceanTripService.ChatPrefix + string.Format(UIStrings.OceanTrip_Stopped, e.Message));
            throw;
        }
        finally {
            Configuration.C.PluginEnabled = _pluginWasEnabled;
            onFinished?.Invoke();
        }
    }

    public void SetupHooks() { }
    public void EnableHooks() => IAddonLifecycle.Get().RegisterListener(AddonEvent.PostSetup, "IKDResult", OnResultsSetup);
    public void DisableHooks() => IAddonLifecycle.Get().UnregisterListener(AddonEvent.PostSetup, "IKDResult", OnResultsSetup);
    public void DisposeHooks() { }

    private static unsafe void OnResultsSetup(AddonEvent type, AddonArgs args) => args.GetAddon<AtkUnitBase>()->Close(true);

    #region Travel

    private async Task TravelToDock() {
        using var scope = BeginScope(nameof(TravelToDock));
        if (InOceanFishing)
            return; // already aboard, e.g. registered by hand
        Status = UIStrings.OceanTrip_StatusTravelling;
        await WaitUntil(() => !IsOccupied(), "WaitUnoccupied", timeout: TimeSpan.FromSeconds(30));
        await MoveTo(OceanTripConsts.DockTerritory, OceanTripConsts.DryskthotaPosition, MovementConfig.Default.WithTolerance(4f));
    }

    private async Task EnsureFisher() {
        using var scope = BeginScope(nameof(EnsureFisher));
        if (InOceanFishing || CurrentJob == FisherJobId)
            return;

        Status = UIStrings.OceanTrip_StatusSwappingGearsets;
        var gearset = FindFisherGearset();
        ErrorIf(gearset < 0, UIStrings.OceanTrip_NoFisherGearset);
        await WaitUntil(() => !IsOccupied(), "WaitUnoccupied", timeout: TimeSpan.FromSeconds(30));
        await TryUntil(() => EquipGearset(gearset), () => CurrentJob == FisherJobId, "EquipFisher", timeoutSeconds: 3f, maxRetries: 3);
    }

    private async Task EquipRecommended() {
        using var scope = BeginScope(nameof(EquipRecommended));
        if (InOceanFishing || !SetupRecommended())
            return;
        Status = UIStrings.OceanTrip_StatusEquippingRecommended;
        await WaitWhile(IsRecommendUpdating, "WaitRecommendSetup", timeout: TimeSpan.FromSeconds(10));
        if (AllRecommendedEquipped())
            return;
        await WaitUntil(() => !IsOccupied(), "WaitUnoccupied", timeout: TimeSpan.FromSeconds(30));
        EquipRecommendedGear();
        await WaitWhile(IsRecommendUpdating, "WaitRecommendEquip", timeout: TimeSpan.FromSeconds(10));
        await DelayMs(250);
        UpdateCurrentGearset();
    }

    #endregion

    #region Repair

    private async Task RepairGear() {
        using var scope = BeginScope(nameof(RepairGear));
        if (InOceanFishing || LowestCondition() >= Cfg.RepairThreshold)
            return;
        Status = UIStrings.OceanTrip_StatusRepairing;
        if (Cfg.RepairMode == OceanTripRepair.SelfThenNpc) {
            await SelfRepair();
            if (LowestCondition() >= Cfg.RepairThreshold)
                return;
            Log($"Self repair left gear at {LowestCondition()}%, trying the mender");
        }
        await NpcRepair();
        if (LowestCondition() < Cfg.RepairThreshold)
            Warning($"Gear still at {LowestCondition()}% after repairing");
    }

    private async Task SelfRepair() {
        using var scope = BeginScope(nameof(SelfRepair));
        await WaitUntil(() => !IsOccupied(), "WaitUnoccupied", timeout: TimeSpan.FromSeconds(30));
        if (!OpenSelfRepair())
            return;
        await WaitUntil(() => IsSelfRepairOpen && AtkUnitBase.IsAddonReady("Repair"), "WaitRepairOpen", timeout: TimeSpan.FromSeconds(5), abortOnTimeout: false);
        if (!IsSelfRepairOpen)
            return;
        var before = LowestCondition();
        RepairEquippedSelf();
        // no dark matter or too low a crafter level just does nothing
        await WaitUntil(() => { ConfirmRepair(); return LowestCondition() > before; }, "WaitRepaired", timeout: TimeSpan.FromSeconds(10), abortOnTimeout: false);
        await WaitWhile(() => ICondition.Get()[ConditionFlag.Occupied39], "WaitRepairDone", timeout: TimeSpan.FromSeconds(10), abortOnTimeout: false);
        CloseAddon("Repair");
        await WaitWhile(() => AtkUnitBase.IsAddonReady("Repair"), "WaitRepairClosed", timeout: TimeSpan.FromSeconds(5), abortOnTimeout: false);
    }

    private async Task NpcRepair() {
        using var scope = BeginScope(nameof(NpcRepair));
        var vendor = await ApproachBaitVendor();
        await InteractUntil(vendor.GameObjectId, () => TryGetSelectStringText(out _), "WaitMenu", UiSkipOptions.Talk);
        TryGetSelectStringText(out var text);
        SelectString(1, text);
        await WaitUntil(() => { ConfirmPrompts(); return AtkUnitBase.IsAddonReady("Repair"); }, "WaitRepairOpen", timeout: TimeSpan.FromSeconds(10));
        var before = LowestCondition();
        RepairEquippedNpc();
        await WaitUntil(() => { ConfirmRepair(); return LowestCondition() > before; }, "WaitRepaired", timeout: TimeSpan.FromSeconds(10), abortOnTimeout: false);
        CloseAddon("Repair");
        await WaitUntil(() => { ConfirmPrompts(); return !AtkUnitBase.IsAddonReady("Repair") && !IsOccupied(); }, "WaitRepairClosed", timeout: TimeSpan.FromSeconds(10), abortOnTimeout: false);
    }

    #endregion

    #region Bait

    private async Task BuyBait() {
        using var scope = BeginScope(nameof(BuyBait));
        if (InOceanFishing)
            return;

        var wanted = OceanTripConsts.Baits.Select(id => (Id: id, Target: Math.Clamp(Cfg.BaitCount, 1, 999))).ToList();
        if (Cfg.BuyVersatileLure)
            wanted.Add((OceanTripConsts.VersatileLure, Math.Clamp(Cfg.LureCount, 1, 999)));
        var toBuy = wanted.Select(x => (x.Id, Count: x.Target - ItemCount(x.Id))).Where(x => x.Count > 0).ToList();
        if (toBuy.Count == 0)
            return;

        Status = UIStrings.OceanTrip_StatusBuyingBait;
        await OpenBaitShop();
        foreach (var (id, count) in toBuy) {
            var remaining = count;
            while (remaining > 0) {
                var batch = Math.Min(remaining, OceanTripConsts.MaxPurchaseBatch);
                var before = ItemCount(id);
                Log($"Buying {batch}x {id}");
                ErrorIf(!AgentShop.BuyItemFromShop(OceanTripConsts.BaitShopId, id, batch), string.Format(UIStrings.OceanTrip_ErrorBuyFailed, id));
                await WaitUntilSkipping(() => ItemCount(id) > before, "WaitBought", UiSkipOptions.YesNo);
                remaining -= ItemCount(id) - before;
            }
        }
        await CloseShop();
    }

    private async Task OpenBaitShop() {
        using var scope = BeginScope(nameof(OpenBaitShop));
        if (AgentShop.IsShopOpen(OceanTripConsts.BaitShopId))
            return;

        var vendor = await ApproachBaitVendor();
        for (var attempt = 1; ; attempt++) {
            await WaitUntil(() => IObjectTable.Get().LocalPlayer.CanInteract && !IsMoving, "WaitForInteractReady");
            ErrorIf(!AgentShop.OpenShop(vendor.GameObjectId, OceanTripConsts.BaitShopId), UIStrings.OceanTrip_ErrorOpenShopFailed);
            var deadline = DateTime.Now.AddSeconds(5);
            await WaitUntilSkipping(() => ShopReady() || DateTime.Now > deadline && !ICondition.Get()[ConditionFlag.OccupiedInEvent], "WaitShopOpen", UiSkipOptions.Talk);
            if (ShopReady())
                return;
            ErrorIf(attempt >= 3, UIStrings.OceanTrip_ErrorOpenShopFailed);
            Log($"Shop didn't open, retrying ({attempt})");
        }

        static bool ShopReady() => AgentShop.IsShopOpen(OceanTripConsts.BaitShopId) && AtkUnitBase.IsAddonReady("Shop");
    }

    private Task<IGameObject> ApproachBaitVendor() => ApproachNpc(OceanTripConsts.BaitVendorId);

    private async Task<IGameObject> ApproachNpc(uint baseId) {
        if (FindObject(baseId) == null)
            await MoveTo(OceanTripConsts.DockTerritory, OceanTripConsts.DryskthotaPosition, MovementConfig.Default.WithTolerance(4f));
        var vendor = FindObject(baseId) ?? throw new InvalidOperationException(UIStrings.OceanTrip_ErrorVendorNotFound);
        if (!vendor.IsInInteractRange())
            await MoveToDirectly(vendor.Position, vendor.IsInInteractRange);
        await WaitWhile(() => IsMoving, "WaitStopped");
        return vendor;
    }

    private async Task CloseShop() {
        using var scope = BeginScope(nameof(CloseShop));
        if (!AgentShop.IsShopOpen())
            return;
        AgentShop.CloseShop();
        await WaitWhile(() => AgentShop.IsShopOpen() || IsOccupied(), "WaitShopClosed", timeout: TimeSpan.FromSeconds(10));
    }

    #endregion

    #region Scrip exchange

    private async Task BuyScripBait() {
        using var scope = BeginScope(nameof(BuyScripBait));
        if (InOceanFishing)
            return;
        var target = Math.Clamp(Cfg.ScripBaitCount, 1, 999);
        var toBuy = OceanScripShop.BaitsFor(Voyage.RouteId).Select(id => (Id: id, Count: target - ItemCount(id))).Where(x => x.Count > 0).ToList();
        if (toBuy.Count == 0)
            return;

        Status = UIStrings.OceanTrip_StatusBuyingScripItems;
        await OpenScripShop();
        foreach (var (id, count) in toBuy)
            await BuyScripItem(id, count);
        await CloseScripShop();
    }

    private async Task SpendScrips() {
        using var scope = BeginScope(nameof(SpendScrips));
        var purchases = Cfg.ScripPurchases.Where(p => p.ItemId != 0 && (p.Keep <= 0 || ItemCount(p.ItemId) < p.Keep)).ToList();
        if (purchases.Count == 0)
            return;

        Status = UIStrings.OceanTrip_StatusBuyingScripItems;
        await OpenScripShop();
        // in list order: the first one gets everything it wants before the next is looked at
        foreach (var purchase in purchases)
            await BuyScripItem(purchase.ItemId, purchase.Keep <= 0 ? int.MaxValue : purchase.Keep - ItemCount(purchase.ItemId), Math.Max(Cfg.ScripReserve, 0));
        await CloseScripShop();
    }

    private async Task OpenScripShop() {
        using var scope = BeginScope(nameof(OpenScripShop));
        if (OceanScripShop.IsReady)
            return;
        var vendor = await ApproachNpc(OceanScripShop.VendorId);
        await InteractUntil(vendor.GameObjectId, () => { ConfirmPrompts(); return TryGetSelectStringText(out _) || OceanScripShop.IsOpen; }, "WaitMenu");
        if (TryGetSelectStringText(out var text))
            SelectString(OceanScripShop.MenuIndex, text);
        await WaitUntil(() => { ConfirmPrompts(); return OceanScripShop.IsReady; }, "WaitShopOpen", timeout: TimeSpan.FromSeconds(10));
    }

    private async Task CloseScripShop() {
        using var scope = BeginScope(nameof(CloseScripShop));
        OceanScripShop.Close();
        await WaitUntil(() => { ConfirmPrompts(); return !OceanScripShop.IsOpen && !IsOccupied(); }, "WaitShopClosed", timeout: TimeSpan.FromSeconds(10), abortOnTimeout: false);
    }

    private async Task BuyScripItem(uint itemId, int count, int reserve = 0) {
        using var scope = BeginScope(nameof(BuyScripItem));
        if (OceanScripShop.Find(itemId) is not { } entry) {
            Warning($"{itemId} isn't sold at the scrip exchange");
            return;
        }
        if (!await ShowScripItem(itemId)) {
            Warning($"Couldn't find {entry.Name} in the scrip exchange");
            return;
        }

        var remaining = count;
        var perExchange = (int)entry.ReceiveCount;
        while (remaining > 0) {
            var index = OceanScripShop.ListIndex(itemId, out var max);
            var wanted = remaining == int.MaxValue ? int.MaxValue : (remaining + perExchange - 1) / perExchange;
            var amount = Math.Min(Math.Min(wanted, OceanScripShop.Affordable(itemId, reserve)), OceanTripConsts.MaxPurchaseBatch);
            if (max > 0)
                amount = Math.Min(amount, max);
            if (index < 0 || amount <= 0)
                break;

            var before = ItemCount(itemId);
            Log($"Buying {amount}x {entry.Name} ({entry.Cost} scrips each)");
            OceanScripShop.Buy(index, amount);
            await WaitUntil(() => OceanScripShop.ConfirmOpen, "WaitConfirm", timeout: TimeSpan.FromSeconds(5), abortOnTimeout: false);
            if (!OceanScripShop.ConfirmOpen) {
                Warning($"No exchange prompt for {entry.Name}");
                break;
            }
            OceanScripShop.Confirm();
            await WaitUntil(() => ItemCount(itemId) > before, "WaitBought", timeout: TimeSpan.FromSeconds(10), abortOnTimeout: false);
            var bought = ItemCount(itemId) - before;
            if (bought <= 0) {
                Warning($"Buying {entry.Name} didn't go through");
                break;
            }
            if (remaining != int.MaxValue)
                remaining -= bought;
            await WaitUntil(() => OceanScripShop.IsReady && !OceanScripShop.ConfirmOpen, "WaitShopReady", timeout: TimeSpan.FromSeconds(5), abortOnTimeout: false);
        }
    }

    // picks the dropdown entry and subcategory tab listing the item
    private async Task<bool> ShowScripItem(uint itemId) {
        if (OceanScripShop.ListIndex(itemId, out _) >= 0)
            return true;
        foreach (var category in OceanScripShop.CategoryOrder(itemId)) {
            if (OceanScripShop.SelectedCategory != category) {
                OceanScripShop.SelectCategory(category);
                await DelayMs(300);
            }
            // the subcategories load in after picking the category
            await WaitUntil(() => OceanScripShop.IsReady && OceanScripShop.FindTab(itemId) >= 0, "WaitCategory", timeout: TimeSpan.FromSeconds(3), abortOnTimeout: false);
            var tab = OceanScripShop.FindTab(itemId);
            if (tab < 0)
                continue;
            if (OceanScripShop.SelectedTab != tab) {
                OceanScripShop.SelectTab(tab);
                await DelayMs(300);
            }
            await WaitUntil(() => OceanScripShop.IsReady && OceanScripShop.ListIndex(itemId, out _) >= 0, "WaitTab", timeout: TimeSpan.FromSeconds(3), abortOnTimeout: false);
            if (OceanScripShop.ListIndex(itemId, out _) >= 0)
                return true;
        }
        return false;
    }

    #endregion

    #region Voyage

    private async Task WaitForBoardTime() {
        using var scope = BeginScope(nameof(WaitForBoardTime));
        if (InOceanFishing)
            return;
        Status = UIStrings.OceanTrip_StatusWaitingToBoard;
        while (Now < BoardTime)
            await DelayMs(1000);
    }

    private async Task Register() {
        using var scope = BeginScope(nameof(Register));
        if (InOceanFishing)
            return;

        var closes = Voyage.BoardingStart + OceanSchedule.BoardingSeconds;
        ErrorIf(Now >= closes - 15, UIStrings.OceanTrip_ErrorBoardingClosed);

        Status = UIStrings.OceanTrip_StatusRegistering;
        // world state, presets and the zone start event all need the plugin on, and the first zone can start soon after boarding
        Configuration.C.PluginEnabled = true;
        if (FindObject(OceanTripConsts.DryskthotaId) is not { } npc) {
            Error(UIStrings.OceanTrip_ErrorDryskthotaNotFound);
            return;
        }

        // dryskthota: talk -> "Register to board." -> route menu (indigo first) -> yes/no -> queued until departure -> duty pop
        var menusSeen = 0;
        var routeIndex = OceanSchedule.IsIndigoRoute(Voyage.RouteId) ? 0 : 1;
        var deadline = Environment.TickCount64 + 60_000;
        await InteractUntil(npc.GameObjectId, () => TryGetSelectStringText(out _) || AtkUnitBase.IsAddonReady("Talk") || AtkUnitBase.IsAddonReady("SelectYesno")
            || InOceanFishing || ContentsFinderConfirmReady || OceanTripService.QueuedOceanContent() != 0, "WaitTalk");
        while (!InOceanFishing && !ContentsFinderConfirmReady && OceanTripService.QueuedOceanContent() == 0) {
            ErrorIf(Environment.TickCount64 > deadline, UIStrings.OceanTrip_ErrorRegisterTimeout);
            if (TryGetSelectStringText(out var text)) {
                SelectString(menusSeen == 0 ? 0 : routeIndex, text);
                menusSeen++;
                // wait for the menu to close or be replaced by the next one
                await WaitWhile(() => TryGetSelectStringText(out var t) && t == text, "WaitMenuChange", timeout: TimeSpan.FromSeconds(3), abortOnTimeout: false);
                continue;
            }
            if (AtkUnitBase.IsAddonReady("Talk"))
                AddonTalk.Progress();
            if (AtkUnitBase.IsAddonReady("SelectYesno"))
                AddonSelectYesno.Yes();
            await NextFrame(5);
        }

        // queued until the pop: when the boat fills up, or at :15 at the latest, even if nobody else joined
        Status = UIStrings.OceanTrip_StatusRegistered;
        await WaitUntil(() => {
            if (InOceanFishing)
                return true;
            if (ContentsFinderConfirmReady) {
                Status = UIStrings.OceanTrip_StatusBoarding;
                CommenceDuty();
            }
            return false;
        }, "WaitBoarded", checkFrequency: 10, timeout: TimeSpan.FromSeconds(Math.Max(closes - Now, 0) + 180));
    }

    private async Task RideVoyage() {
        using var scope = BeginScope(nameof(RideVoyage));
        Configuration.C.PluginEnabled = true;
        await WaitUntil(() => Player.Interactable && !IsOccupied() || _pendingZone != null, "WaitAboard", timeout: TimeSpan.FromMinutes(2), abortOnTimeout: false);

        var fishingZone = -1;
        var nextMidZoneCheck = 0L;
        while (InOceanFishing) {
            var ocean = Ws.OceanFishing;
            if (ocean.Status == InstanceContentOceanFishing.OceanFishingStatus.Fishing) {
                fishingZone = (int)ocean.CurrentZone;
                if (Cfg.LeaveEarly && Environment.TickCount64 >= nextMidZoneCheck) {
                    nextMidZoneCheck = Environment.TickCount64 + 2000;
                    if (ShouldLeaveMidZone(fishingZone, ocean)) {
                        await LeaveEarly(fishingZone, midZone: true);
                        break;
                    }
                }
            }
            else if (fishingZone >= 0 && ocean != OceanFishingState.Empty) {
                // a stop just ended
                var ended = fishingZone;
                fishingZone = -1;
                if (Cfg.LeaveEarly && ShouldLeaveAfter(ended)) {
                    await LeaveEarly(ended, midZone: false);
                    break;
                }
            }

            if (_pendingZone is { } zone) {
                _pendingZone = null;
                Status = string.Format(UIStrings.OceanTrip_StatusFishingStop, zone + 1);
                await AutoOceanFish.RunZone(this, zone);
            }
            else {
                Status = Ws.OceanFishing == OceanFishingState.Empty ? UIStrings.OceanTrip_StatusWaitingToDepart : string.Format(UIStrings.OceanTrip_StatusFishingStop, Ws.OceanFishing.CurrentZone + 1);
            }
            await NextFrame(10);
        }

        Status = UIStrings.OceanTrip_StatusReturningToDock;
        Configuration.C.PluginEnabled = _pluginWasEnabled;
        await WaitUntil(() => !InOceanFishing && Player.Interactable && !IsOccupied(), "WaitBackAshore", timeout: TimeSpan.FromMinutes(3));
    }

    // the last stop is never left early, its rewards come at the end
    private bool ShouldLeaveAfter(int stop) {
        if (stop >= 2 || RouteFishUnknown)
            return false;
        // achievements need the whole voyage
        if (Configuration.C.AutoOceanFishGoal == OceanFishGoalKind.Achievement && OceanGoalCatalog.GetEligibleAchievementIds(Voyage.RouteId).Count > 0)
            return false;
        var wanted = OceanRouteFishCatalog.GetFish(Voyage.RouteId).Where(f => f.Stop > stop && !OceanGoalCatalog.IsLegendaryCaught(f.FishParameterId)).ToList();
        Log($"After stop {stop + 1}: {wanted.Count} uncaught fish on later stops");
        return wanted.Count == 0;
    }

    // the stop's spectral current is over and all that's left uncaught here is spectral-only, so the rest of the stop is pointless
    private bool ShouldLeaveMidZone(int stop, OceanFishingState ocean) {
        if (ocean.SpectralCurrentActive || !Ws.SpectralHistory.Any(r => r.ZoneIndex == stop && r.Ended != null))
            return false;
        var here = OceanRouteFishCatalog.GetFish(Voyage.RouteId).Where(f => f.Stop == stop && !OceanGoalCatalog.IsLegendaryCaught(f.FishParameterId)).ToList();
        if (here.Any(f => !f.Spectral))
            return false;
        return ShouldLeaveAfter(stop);
    }

    private bool RouteFishUnknown => OceanRouteFishCatalog.GetFish(Voyage.RouteId).Count == 0;

    private async Task LeaveEarly(int stop, bool midZone) {
        using var scope = BeginScope(nameof(LeaveEarly));
        Log(midZone
            ? $"Spectral current over and nothing else left to catch from stop {stop + 1} on, leaving the voyage"
            : $"Nothing left to catch after stop {stop + 1}, leaving the voyage");
        Status = UIStrings.OceanTrip_StatusLeavingEarly;
        if (Ws.Fishing.FishingState != FishingState.None)
            FishingSessionManager.Get().Rod.RequestHardQuit();
        await WaitUntil(() => Ws.Fishing.FishingState == FishingState.None, "WaitStoppedFishing", timeout: TimeSpan.FromSeconds(30), abortOnTimeout: false);
        // the hard quit's auto start lock would otherwise outlive the voyage
        RodFishingModule.ClearAutoStartLock();

        // forced works mid-cutscene and skips the confirmation prompt
        var nextAttempt = 0L;
        await WaitUntil(() => {
            if (!InOceanFishing)
                return true;
            if (Environment.TickCount64 >= nextAttempt) {
                nextAttempt = Environment.TickCount64 + 5000;
                EventFramework.LeaveCurrentContent(true);
            }
            return false;
        }, "WaitLeft", checkFrequency: 10, timeout: TimeSpan.FromMinutes(1));
    }

    #endregion

    #region Cleanup

    private async Task Cleanup() {
        using var scope = BeginScope(nameof(Cleanup));
        switch (Cfg.Cleanup) {
            case OceanTripCleanup.Desynth when OceanFishCleanup.DesynthUnlocked:
                await DesynthFish();
                break;
            case OceanTripCleanup.Desynth:
                Warning("Desynthesis isn't unlocked, skipping");
                break;
            case OceanTripCleanup.Sell:
                await SellFish();
                break;
        }
    }

    private async Task DesynthFish() {
        using var scope = BeginScope(nameof(DesynthFish));
        Status = UIStrings.OceanTrip_StatusDesynthing;
        foreach (var slot in OceanFishCleanup.FindFish(OceanFishCleanup.CanDesynth)) {
            while (OceanFishCleanup.SlotHolds(slot)) {
                await WaitUntil(() => !IsOccupied() && !AtkUnitBase.IsAddonReady("SalvageResult"), "WaitIdle", timeout: TimeSpan.FromSeconds(10));
                ErrorIf(!OceanFishCleanup.Desynth(slot), string.Format(UIStrings.OceanTrip_ErrorDesynthFailed, slot));
                await WaitUntil(() => AtkUnitBase.IsAddonReady("SalvageDialog"), "WaitDialog", timeout: TimeSpan.FromSeconds(5));
                ClickDesynthesize();
                await WaitUntil(() => AtkUnitBase.IsAddonReady("SalvageResult"), "WaitResult", timeout: TimeSpan.FromSeconds(10));
                CloseAddon("SalvageResult");
                await WaitWhile(() => AtkUnitBase.IsAddonReady("SalvageResult") || ICondition.Get()[ConditionFlag.Occupied39], "WaitResultClosed", timeout: TimeSpan.FromSeconds(10));
            }
        }
    }

    private async Task SellFish() {
        using var scope = BeginScope(nameof(SellFish));
        var slots = OceanFishCleanup.FindFish(OceanFishCleanup.CanSell);
        if (slots.Count == 0)
            return;
        ErrorIf(!OceanFishCleanup.SellItemAvailable, UIStrings.OceanTrip_ErrorSellFunctionMissing);

        Status = UIStrings.OceanTrip_StatusSelling;
        await OpenBaitShop();
        foreach (var slot in slots) {
            if (!OceanFishCleanup.Sell(slot)) {
                Warning($"Failed to sell {slot}");
                continue;
            }
            await WaitUntilSkipping(() => !OceanFishCleanup.SlotHolds(slot), "WaitSold", UiSkipOptions.YesNo);
            await DelayMs(200);
        }
        await CloseShop();
    }

    #endregion

    #region Post steps

    private async Task RunPostSteps() {
        using var scope = BeginScope(nameof(RunPostSteps));
        Status = UIStrings.OceanTrip_StatusPostSteps;
        foreach (var step in Cfg.PostSteps.ToList()) {
            Log($"Post step {step.Kind} '{step.Arg}'");
            switch (step.Kind) {
                case OceanTripStepKind.ReturnToOrigin:
                    await ReturnToOrigin(step.Home);
                    break;
                case OceanTripStepKind.StartAutoRetainerMulti when Source is not (OceanTripSource.ArPostprocess or OceanTripSource.ArMultiMode): // it resumes by itself
                    if (!AutoRetainerIPC.Get().Available) {
                        Warning("AutoRetainer isn't loaded, skipping multi mode");
                        break;
                    }
                    AutoRetainerIPC.Get().EnableMultiMode();
                    await DelayMs(1000);
                    break;
                case OceanTripStepKind.GoToInn:
                    await LifestreamTravel("Going to the inn", () => LifestreamIPC.Get().EnqueueInn());
                    break;
                case OceanTripStepKind.GoHome:
                    await LifestreamTravel("Going home", () => LifestreamIPC.Get().EnqueueProperty((int)step.Home));
                    break;
                case OceanTripStepKind.ChatCommand:
                    if (!string.IsNullOrWhiteSpace(step.Arg)) {
                        Chat.SendMessage(step.Arg.Trim());
                        await DelayMs(1000);
                    }
                    break;
            }
        }
    }

    private async Task ReturnToOrigin(OceanTripHome housing) {
        using var scope = BeginScope(nameof(ReturnToOrigin));
        if (Origin.GearsetIndex >= 0 && CurrentGearset != Origin.GearsetIndex) {
            await WaitUntil(() => !IsOccupied(), "WaitUnoccupied", timeout: TimeSpan.FromSeconds(30));
            EquipGearset(Origin.GearsetIndex);
            await DelayMs(500);
        }

        switch (Origin.Kind) {
            case OceanTripOriginKind.Inn:
                await LifestreamTravel("Returning to the inn", () => LifestreamIPC.Get().EnqueueInn());
                break;
            case OceanTripOriginKind.Housing:
                await LifestreamTravel("Returning home", () => LifestreamIPC.Get().EnqueueProperty((int)housing));
                break;
            default:
                // back to the exact spot, also within the dock's own zone
                if (Origin.Territory != 0 && (IClientState.Get().TerritoryType != Origin.Territory || Player.DistanceTo(Origin.Position) > 3f))
                    await MoveTo(Origin.Territory, Origin.Position, MovementConfig.Default.WithTolerance(3f));
                break;
        }
    }

    private async Task LifestreamTravel(string what, Func<bool> enqueue) {
        using var scope = BeginScope(nameof(LifestreamTravel));
        if (!enqueue()) {
            Warning($"Lifestream is needed for \"{what}\", skipping");
            return;
        }
        await WaitUntil(() => LifestreamIPC.Get().IsBusy, "WaitLifestreamStart", timeout: TimeSpan.FromSeconds(5), abortOnTimeout: false);
        await WaitWhile(() => LifestreamIPC.Get().IsBusy, "WaitLifestream", checkFrequency: 10, timeout: TimeSpan.FromMinutes(5));
        await WaitUntil(() => Player.Interactable && !IsOccupied(), "WaitArrived", timeout: TimeSpan.FromSeconds(30), abortOnTimeout: false);
    }

    #endregion

    #region Helpers

    private static long Now => Framework.GetServerTime();

    public static bool InOceanFishing => TerritoryType.TryGetRow(IClientState.Get().TerritoryType, out var t) && t.TerritoryIntendedUse.RowId == OceanFishingIntendedUse;

    private static bool InOtherDuty
        => OceanTripService.InOtherDutyQueue
           || !InOceanFishing && (ICondition.Get()[ConditionFlag.BoundByDuty] || ICondition.Get()[ConditionFlag.BoundByDuty56] || ICondition.Get()[ConditionFlag.BoundByDuty95]);

    private static uint CurrentJob => IObjectTable.Get().LocalPlayer?.ClassJob.RowId ?? 0;

    private static bool IsMoving => IObjectTable.Get().LocalPlayer is { } p && p.IsMoving;

    private static int ItemCount(uint itemId) {
        unsafe {
            return InventoryManager.Instance()->GetInventoryItemCount(itemId);
        }
    }

    private static IGameObject? FindObject(uint baseId)
        => IObjectTable.Get().Where(o => o.BaseId == baseId && o.IsTargetable).OrderBy(o => Vector3.DistanceSquared(o.Position, IObjectTable.Get().LocalPlayer?.Position ?? default)).FirstOrDefault();

    private static OceanTripOrigin CaptureOrigin() {
        var territory = IClientState.Get().TerritoryType;
        var kind = TerritoryType.TryGetRow(territory, out var t) ? t.TerritoryIntendedUse.RowId switch {
            2 => OceanTripOriginKind.Inn,
            13 or 14 => OceanTripOriginKind.Housing,
            _ => OceanTripOriginKind.Other,
        } : OceanTripOriginKind.Other;
        return new OceanTripOrigin(kind, territory, IObjectTable.Get().LocalPlayer?.Position ?? default, CurrentGearset);
    }

    private static unsafe int CurrentGearset {
        get {
            var module = RaptureGearsetModule.Instance();
            return module == null ? -1 : module->CurrentGearsetIndex;
        }
    }

    // 0-based gearset index, -1 if there's no fisher gearset
    public static unsafe int FindFisherGearset() {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return -1;
        for (var i = 0; i < 100; i++) {
            if (!module->IsValidGearset(i))
                continue;
            var entry = module->GetGearset(i);
            if (entry != null && entry->ClassJob == FisherJobId)
                return i;
        }
        return -1;
    }

    public static unsafe string GearsetName(int index) {
        var module = RaptureGearsetModule.Instance();
        var entry = module == null ? null : module->GetGearset(index);
        return entry == null ? "" : entry->NameString;
    }

    public static unsafe int LowestCondition() {
        var equipped = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipped == null)
            return 100;
        var lowest = 100;
        for (var i = 0; i < equipped->Size; i++) {
            var item = equipped->GetInventorySlot(i);
            if (item != null && item->ItemId != 0)
                lowest = Math.Min(lowest, item->Condition / 300);
        }
        return lowest;
    }

    private static void ConfirmPrompts() {
        if (AtkUnitBase.IsAddonReady("Talk"))
            AddonTalk.Progress();
    }

    private static void ConfirmRepair() {
        ConfirmPrompts();
        if (IsRepairYesno())
            AddonSelectYesno.Yes();
    }

    private static unsafe bool IsRepairYesno() {
        if (!TryGetAddonByName<AtkUnitBase>("SelectYesno", out var addon) || !IsAddonReady(addon))
            return false;
        var id = (uint)addon->Id;
        return RaptureAtkModule.Instance()->AddonCallbackMapping.TryGetValuePointer(in id, out var entry) && entry != null && entry->AgentInterface == (AgentInterface*)AgentRepair.Instance();
    }

    private static unsafe bool IsSelfRepairOpen => AgentRepair.Instance()->IsSelfRepairOpen;
    private static unsafe bool OpenSelfRepair() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 6);
    private static unsafe void RepairEquippedSelf() => RepairManager.Instance()->RepairEquipped(false);
    private static void RepairEquippedNpc() => GameMain.ExecuteCommand(CommandFlag.RepairEquippedItemsNPC, (int)InventoryType.EquippedItems);

    private static unsafe bool SetupRecommended() {
        var mod = RecommendEquipModule.Instance();
        return mod != null && mod->SetupForClassJob((byte)FisherJobId);
    }

    private static unsafe bool IsRecommendUpdating() {
        var mod = RecommendEquipModule.Instance();
        return mod != null && mod->IsUpdating;
    }

    private static unsafe void EquipRecommendedGear() {
        var mod = RecommendEquipModule.Instance();
        if (mod != null)
            mod->EquipRecommendedGear();
    }

    private static unsafe bool AllRecommendedEquipped() {
        var mod = RecommendEquipModule.Instance();
        var equipped = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        if (mod == null || equipped == null)
            return true;
        foreach (var rec in mod->RecommendedItems) {
            var recommended = rec.Value;
            if (recommended == null || recommended->ItemId == 0)
                continue;
            var found = false;
            for (var i = 0; i < equipped->Size && !found; i++)
                found = equipped->Items[i].ItemId == recommended->ItemId;
            if (!found)
                return false;
        }
        return true;
    }

    private static unsafe void UpdateCurrentGearset() {
        var module = RaptureGearsetModule.Instance();
        if (module != null && module->IsValidGearset(module->CurrentGearsetIndex))
            module->UpdateGearset(module->CurrentGearsetIndex);
    }

    private static unsafe void EquipGearset(int index) => RaptureGearsetModule.Instance()->EquipGearset(index);

    private static bool InDutyQueue => ICondition.Get()[ConditionFlag.InDutyQueue];

    private static unsafe bool ContentsFinderConfirmReady => AtkUnitBase.IsAddonReady("ContentsFinderConfirm");

    private static unsafe void CommenceDuty() {
        if (TryGetAddonByName<AtkUnitBase>("ContentsFinderConfirm", out var addon) && IsAddonReady(addon))
            new AddonMaster.ContentsFinderConfirm(addon).Commence();
    }

    private static unsafe bool TryGetSelectStringText(out string text) {
        text = "";
        if (!TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) || !IsAddonReady(addon))
            return false;
        text = new AddonMaster.SelectString(addon).Text;
        return true;
    }

    private unsafe void SelectString(int index, string text) {
        if (!TryGetAddonByName<AtkUnitBase>("SelectString", out var addon))
            return;
        var menu = new AddonMaster.SelectString(addon);
        Log($"SelectString '{text}' ({menu.EntryCount} entries), picking {index}");
        AddonSelectString.Select(Math.Clamp(index, 0, Math.Max(menu.EntryCount - 1, 0)));
    }

    private static unsafe void ClickDesynthesize() {
        if (TryGetAddonByName<AtkUnitBase>("SalvageDialog", out var addon) && IsAddonReady(addon))
            Callback.Fire(addon, false, 0, false);
    }

    private static unsafe void CloseAddon(string name) {
        if (TryGetAddonByName<AtkUnitBase>(name, out var addon))
            addon->Close(true);
    }

    #endregion
}
