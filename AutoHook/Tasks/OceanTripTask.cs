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
using Framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Numerics;

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

    // a click means go now; automatic trips (and AutoRetainer, which can claim early) keep to the arrival setting
    public long BoardTime => Source is OceanTripSource.Manual or OceanTripSource.Alert ? Voyage.BoardingStart : OceanTripService.StartTime(Voyage);

    protected override async Task Execute() {
        using var scope = BeginScope(nameof(OceanTripTask));
        _pluginWasEnabled = Configuration.C.PluginEnabled;
        Origin = CaptureOrigin();
        Log($"Trip for route {Voyage.RouteId} boarding at {Voyage.BoardingStart}, origin {Origin}");

        try {
            await TravelToDock();
            await EnsureFisher();
            if (Cfg.BuyBait)
                await BuyBait();
            await WaitForBoardTime();
            await Register();
            await RideVoyage();
            await Cleanup();
            await RunPostSteps();
            IChatGui.Get().PrintStatus("[Ocean trip] Finished");
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception e) {
            IChatGui.Get().PrintError($"[AutoHook] Ocean trip stopped: {e.Message}");
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
        Status = "Travelling to Limsa Lominsa";
        await WaitUntil(() => !IsOccupied(), "WaitUnoccupied", timeout: TimeSpan.FromSeconds(30));
        await MoveTo(OceanTripConsts.DockTerritory, OceanTripConsts.DryskthotaPosition, MovementConfig.Default.WithTolerance(4f));
    }

    private async Task EnsureFisher() {
        using var scope = BeginScope(nameof(EnsureFisher));
        if (InOceanFishing || CurrentJob == FisherJobId)
            return;

        Status = "Switching to fisher";
        var gearset = FindFisherGearset();
        ErrorIf(gearset < 0, "No fisher gearset found");
        await WaitUntil(() => !IsOccupied(), "WaitUnoccupied", timeout: TimeSpan.FromSeconds(30));
        await TryUntil(() => EquipGearset(gearset), () => CurrentJob == FisherJobId, "EquipFisher", timeoutSeconds: 3f, maxRetries: 3);
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

        Status = "Buying bait";
        await OpenBaitShop();
        foreach (var (id, count) in toBuy) {
            var remaining = count;
            while (remaining > 0) {
                var batch = Math.Min(remaining, 99);
                var before = ItemCount(id);
                Log($"Buying {batch}x {id}");
                ErrorIf(!AgentShop.BuyItemFromShop(OceanTripConsts.BaitShopId, id, batch), $"Failed to buy item {id}");
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

        if (FindObject(OceanTripConsts.BaitVendorId) == null)
            await MoveTo(OceanTripConsts.DockTerritory, OceanTripConsts.DryskthotaPosition, MovementConfig.Default.WithTolerance(4f));
        if (FindObject(OceanTripConsts.BaitVendorId) is not { } vendor) {
            Error("Bait vendor not found");
            return;
        }
        if (!vendor.IsInInteractRange())
            await MoveToDirectly(vendor.Position, vendor.IsInInteractRange);
        await WaitWhile(() => IsMoving, "WaitStopped");
        ErrorIf(!AgentShop.OpenShop(vendor.GameObjectId, OceanTripConsts.BaitShopId), "Failed to open bait shop");
        await WaitUntilSkipping(() => AgentShop.IsShopOpen(OceanTripConsts.BaitShopId) && AtkUnitBase.IsAddonReady("Shop"), "WaitShopOpen", UiSkipOptions.Talk);
    }

    private async Task CloseShop() {
        using var scope = BeginScope(nameof(CloseShop));
        if (!AgentShop.IsShopOpen())
            return;
        AgentShop.CloseShop();
        await WaitWhile(() => AgentShop.IsShopOpen() || IsOccupied(), "WaitShopClosed", timeout: TimeSpan.FromSeconds(10));
    }

    #endregion

    #region Voyage

    private async Task WaitForBoardTime() {
        using var scope = BeginScope(nameof(WaitForBoardTime));
        if (InOceanFishing)
            return;
        while (Now < BoardTime) {
            Status = $"Waiting to board ({BoardTime - Now}s)";
            await DelayMs(1000);
        }
    }

    private async Task Register() {
        using var scope = BeginScope(nameof(Register));
        if (InOceanFishing)
            return;

        var closes = Voyage.BoardingStart + OceanSchedule.BoardingSeconds;
        ErrorIf(Now >= closes - 15, "Boarding has closed");

        Status = "Registering to board";
        // world state, presets and the zone start event all need the plugin on, and the first zone can start soon after boarding
        Configuration.C.PluginEnabled = true;
        if (FindObject(OceanTripConsts.DryskthotaId) is not { } npc) {
            Error("Dryskthota not found");
            return;
        }

        // dryskthota: talk -> "Register to board." -> route menu (indigo first) -> yes/no -> queued until departure -> duty pop
        var menusSeen = 0;
        var routeIndex = OceanSchedule.IsIndigoRoute(Voyage.RouteId) ? 0 : 1;
        var deadline = Environment.TickCount64 + 60_000;
        await InteractWith(npc);
        while (!InOceanFishing && !ContentsFinderConfirmReady && !InDutyQueue) {
            ErrorIf(Environment.TickCount64 > deadline, "Timed out registering with Dryskthota");
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
        Status = "Registered, waiting for departure";
        await WaitUntil(() => {
            if (InOceanFishing)
                return true;
            if (ContentsFinderConfirmReady) {
                Status = "Boarding";
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
                Status = $"Fishing stop {zone + 1}";
                await AutoOceanFish.RunZone(this, zone);
            }
            else {
                Status = Ws.OceanFishing == OceanFishingState.Empty ? "Waiting to depart" : $"Fishing stop {Ws.OceanFishing.CurrentZone + 1}";
            }
            await NextFrame(10);
        }

        Status = "Returning to the dock";
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
        IChatGui.Get().PrintStatus(midZone
            ? $"[Ocean trip] Spectral current over and nothing else left to catch from stop {stop + 1} on, leaving the voyage"
            : $"[Ocean trip] Nothing left to catch after stop {stop + 1}, leaving the voyage");
        Status = "Leaving early";
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
                IChatGui.Get().PrintError("[AutoHook] Ocean trip: desynthesis isn't unlocked, skipping");
                break;
            case OceanTripCleanup.Sell:
                await SellFish();
                break;
        }
    }

    private async Task DesynthFish() {
        using var scope = BeginScope(nameof(DesynthFish));
        foreach (var slot in OceanFishCleanup.FindFish(OceanFishCleanup.CanDesynth)) {
            while (OceanFishCleanup.SlotHolds(slot)) {
                Status = $"Desynthesising {ItemName(slot.ItemId)}";
                await WaitUntil(() => !IsOccupied() && !AtkUnitBase.IsAddonReady("SalvageResult"), "WaitIdle", timeout: TimeSpan.FromSeconds(10));
                ErrorIf(!OceanFishCleanup.Desynth(slot), $"Failed to desynth {slot}");
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
        ErrorIf(!OceanFishCleanup.SellItemAvailable, "Couldn't find the game's sell function");

        Status = "Selling fish";
        await OpenBaitShop();
        foreach (var slot in slots) {
            Status = $"Selling {ItemName(slot.ItemId)}";
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
        foreach (var step in Cfg.PostSteps.ToList()) {
            Log($"Post step {step.Kind} '{step.Arg}'");
            switch (step.Kind) {
                case OceanTripStepKind.ReturnToOrigin:
                    await ReturnToOrigin(step.Home);
                    break;
                case OceanTripStepKind.StartAutoRetainerMulti when Source is not (OceanTripSource.ArPostprocess or OceanTripSource.ArMultiMode): // it resumes by itself
                    if (!AutoRetainerIPC.Get().Available) {
                        IChatGui.Get().PrintError("[AutoHook] Ocean trip: AutoRetainer isn't loaded, skipping");
                        break;
                    }
                    Status = "Starting AutoRetainer multi mode";
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
                        Status = $"Running {step.Arg}";
                        Chat.SendMessage(step.Arg.Trim());
                        await DelayMs(1000);
                    }
                    break;
                case OceanTripStepKind.Wait:
                    if (int.TryParse(step.Arg, out var seconds) && seconds > 0) {
                        Status = $"Waiting {seconds}s";
                        await DelayMs(seconds * 1000);
                    }
                    break;
            }
        }
    }

    private async Task ReturnToOrigin(OceanTripHome housing) {
        using var scope = BeginScope(nameof(ReturnToOrigin));
        Status = "Returning";
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

    private async Task LifestreamTravel(string status, Func<bool> enqueue) {
        using var scope = BeginScope(nameof(LifestreamTravel));
        Status = status;
        if (!enqueue()) {
            IChatGui.Get().PrintError($"[AutoHook] Ocean trip: Lifestream is needed for \"{status}\", skipping");
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

    private static uint CurrentJob => IObjectTable.Get().LocalPlayer?.ClassJob.RowId ?? 0;

    private static bool IsMoving => IObjectTable.Get().LocalPlayer is { } p && p.IsMoving;

    private static int ItemCount(uint itemId) {
        unsafe {
            return InventoryManager.Instance()->GetInventoryItemCount(itemId);
        }
    }

    private static string ItemName(uint itemId) => Item.TryGetRow(itemId, out var item) ? item.Name.ToString() : $"#{itemId}";

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
