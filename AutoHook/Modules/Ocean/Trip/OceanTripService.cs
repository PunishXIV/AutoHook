using AutoHook.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Excel.Sheets;

namespace AutoHook.Modules.Ocean;

public sealed class OceanTripService : IPluginService, IDisposable {
    public const int LatestStartMinute = 13;
    private const long LoginLeadSeconds = 60;

    private readonly HashSet<OceanVoyage> _handled = [];
    private readonly Dictionary<uint, (DalamudLinkPayload Payload, OceanVoyage Voyage)> _links = [];
    private OceanVoyage? _arVoyage;
    private (OceanVoyage Voyage, long Deadline, bool MultiWasEnabled)? _login;

    private static OceanTripConfig Cfg => Configuration.C.OceanTrip;

    public string LastMessage { get; private set; } = "";

    public static string ChatPrefix => $"[{UIStrings.AutoOceanFish}] ";

    public OceanTripService() {
        AutoRetainerIPC.Get().CharacterAdditionalTask += OnArCharacterAdditionalTask;
        AutoRetainerIPC.Get().CharacterReadyForPostprocess += OnArReadyForPostprocess;
    }

    public void Dispose() {
        AutoRetainerIPC.Get().CharacterAdditionalTask -= OnArCharacterAdditionalTask;
        AutoRetainerIPC.Get().CharacterReadyForPostprocess -= OnArReadyForPostprocess;
        IChatGui.Get().RemoveChatLinkHandler();
        _links.Clear();
    }

    public static OceanTripTask? Running => Svc.Automation.CurrentTask as OceanTripTask;

    public bool TryStart(OceanVoyage voyage, OceanTripSource source, System.Action? onFinished = null) {
        if (!CanStart(voyage, source, out var reason)) {
            LastMessage = string.Format(UIStrings.OceanTrip_NotStarted, reason);
            IChatGui.Get().PrintError(ChatPrefix + LastMessage);
            return false;
        }

        _handled.Add(voyage);
        LastMessage = string.Format(UIStrings.OceanTrip_Started, source, RouteName(voyage.RouteId));
        IChatGui.Get().PrintStatus(ChatPrefix + string.Format(UIStrings.OceanTrip_Departing, RouteName(voyage.RouteId)));
        var ran = 0; // tasks' finally and automation completion callback both call this, so first wins
        System.Action? once = onFinished == null ? null : () => {
            if (System.Threading.Interlocked.Exchange(ref ran, 1) == 0)
                onFinished();
        };
        Svc.Automation.Start(new OceanTripTask(voyage, source, once), once);
        return true;
    }

    public static void Stop() {
        if (Running != null)
            Svc.Automation.Stop();
    }

    public bool CanStart(OceanVoyage voyage, OceanTripSource source, out string reason) {
        var now = Framework.GetServerTime();
        reason = "";
        if (!IClientState.Get().IsLoggedIn || IObjectTable.Get().LocalPlayer == null)
            reason = UIStrings.OceanTrip_ReasonNotLoggedIn;
        else if (!Configuration.C.AutoOceanFish)
            reason = UIStrings.OceanTrip_ReasonAutoOceanFishDisabled;
        else if (Svc.Automation.Running)
            reason = string.Format(UIStrings.OceanTrip_ReasonAlreadyRunning, Svc.Automation.Name);
        else if (ICondition.Get()[ConditionFlag.BoundByDuty] && !OceanTripTask.InOceanFishing
                 || source != OceanTripSource.ArPostprocess && IsOccupied())
            reason = UIStrings.OceanTrip_ReasonBusy;
        else if (!OceanTripTask.InOceanFishing && now >= LatestStart(voyage))
            reason = UIStrings.OceanTrip_ReasonMissedBoat;
        return reason == "";
    }

    public void Update() {
        if (!EzThrottler.Throttle("OceanTripService.Update", 1000))
            return;
        if (_login is { } login) {
            UpdateLogin(login);
            return;
        }
        if (!Cfg.FullAuto || Svc.Automation.Running)
            return;

        var now = Framework.GetServerTime();
        if (NextTripVoyage(now) is not { } voyage)
            return;
        var loggedIn = IClientState.Get().IsLoggedIn;
        if (now < StartTime(voyage) - (loggedIn ? 0 : LoginLeadSeconds))
            return;

        if (now >= LatestStart(voyage)) {
            _handled.Add(voyage);
            IPluginLog.Get().Information($"[OceanTrip] Skipped {RouteName(voyage.RouteId)}: never reached a safe point in time");
            return;
        }

        var ar = AutoRetainerIPC.Get();
        if (!loggedIn) {
            if (CanLoginFromTitleScreen())
                BeginLogin(voyage);
        }
        else if (ar.Available && ar.MultiModeEnabled) {
            // multi mode would relog or move the character mid-trip, so only go through the paused path
            if (Cfg.SafePoints.HasFlag(OceanTripSafePoint.ArMultiMode) && IsArCharacter && IsIdle())
                StartPaused(voyage);
        }
        else if (IsAtSafePoint() && IsIdle()) {
            TryStart(voyage, OceanTripSource.FullAuto);
        }
    }

    // the next voyage full auto would go for: matches the filters, inside the active window, not already handled or skipped
    public OceanVoyage? NextTripVoyage(long now)
        => OceanSchedule.Upcoming(OceanVoyageFilter.Lines, now)
            .Take(OceanSchedule.RotationLength * OceanVoyageFilter.Lines.Length * 2)
            .Where(v => !_handled.Contains(v) && InActiveWindow(StartTime(v)) && OceanVoyageFilter.Matches(v) && OceanPresetReadiness.IsReady(v.RouteId))
            .Cast<OceanVoyage?>()
            .FirstOrDefault();

    public static bool InActiveWindow(long unix) {
        if (!Cfg.UseActiveWindow)
            return true;
        var local = DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime();
        var minute = local.Hour * 60 + local.Minute;
        var from = Cfg.ActiveFromMinute;
        var to = Cfg.ActiveToMinute;
        if (from == to)
            return true;
        // a window like 22:00-02:00 wraps past midnight
        return from < to ? minute >= from && minute < to : minute >= from || minute < to;
    }

    // when a trip for this voyage sets off
    public static long StartTime(OceanVoyage voyage)
        => voyage.BoardingStart + (Cfg.Arrival == OceanTripArrival.Late ? Math.Clamp(Cfg.LateArrivalMinute, 0, LatestStartMinute) * 60L : 0);

    public static long LatestStart(OceanVoyage voyage) => voyage.BoardingStart + LatestStartMinute * 60L;

    public static unsafe bool IsAtSafePoint() {
        var use = TerritoryType.TryGetRow(IClientState.Get().TerritoryType, out var t) ? t.TerritoryIntendedUse.RowId : 0;
        return use == 2 && Cfg.SafePoints.HasFlag(OceanTripSafePoint.Inn)
            || use is 13 or 14 && Cfg.SafePoints.HasFlag(OceanTripSafePoint.Housing)
            || Cfg.SafePoints.HasFlag(OceanTripSafePoint.Sanctuary) && TerritoryInfo.Instance()->InSanctuary;
    }

    public static bool IsIdle()
        => !Svc.Automation.Running && !IsOccupied() && WorldState.Get().Fishing.FishingState == FishingState.None && !AutoRetainerIPC.Get().IsBusy && !LifestreamIPC.Get().IsBusy;

    #region AutoRetainer multi mode

    public static string? CurrentCharacter
        => IObjectTable.Get().LocalPlayer is { HomeWorld.IsValid: true } p ? $"{p.Name}@{p.HomeWorld.Value.Name}" : null;

    public static bool IsArCharacter => Cfg.ArCharacter.Length > 0 && CurrentCharacter == Cfg.ArCharacter;

    private static bool CanLoginFromTitleScreen()
        => Cfg.SafePoints.HasFlag(OceanTripSafePoint.ArMultiMode)
           && !IClientState.Get().IsLoggedIn
           && Cfg.ArCharacter.Length > 0
           && AutoRetainerIPC.Get().MultiModeEnabled
           && AutoRetainerIPC.Get().CanAutoLogin;

    // already on the AutoRetainer character with multi mode idle: pause it in place
    private void StartPaused(OceanVoyage voyage) {
        AutoRetainerIPC.Get().SetSuppressed(true);
        if (!TryStart(voyage, OceanTripSource.ArMultiMode, () => ResumeAutoRetainer(true)))
            ResumeAutoRetainer(true);
    }

    // pause AutoRetainer's multi mode where it waits on the title screen and log in for the boat
    private void BeginLogin(OceanVoyage voyage) {
        _handled.Add(voyage);
        var ar = AutoRetainerIPC.Get();
        var multi = ar.MultiModeEnabled;
        ar.SetSuppressed(true);
        if (!ar.Relog(Cfg.ArCharacter)) {
            ResumeAutoRetainer(multi);
            IPluginLog.Get().Warning($"[OceanTrip] Skipped {RouteName(voyage.RouteId)}: AutoRetainer couldn't log in {Cfg.ArCharacter}");
            return;
        }
        _login = (voyage, Environment.TickCount64 + 180_000, multi);
        IPluginLog.Get().Information($"[OceanTrip] Logging in {Cfg.ArCharacter} for {RouteName(voyage.RouteId)}");
    }

    private void UpdateLogin((OceanVoyage Voyage, long Deadline, bool MultiWasEnabled) login) {
        if (IClientState.Get().IsLoggedIn && Player.Interactable && !IsOccupied()) {
            _login = null;
            if (!IsArCharacter || !TryStart(login.Voyage, OceanTripSource.ArMultiMode, () => ResumeAutoRetainer(login.MultiWasEnabled)))
                ResumeAutoRetainer(login.MultiWasEnabled);
        }
        else if (Environment.TickCount64 > login.Deadline) {
            _login = null;
            ResumeAutoRetainer(login.MultiWasEnabled);
            IPluginLog.Get().Warning($"[OceanTrip] Skipped {RouteName(login.Voyage.RouteId)}: login timed out");
        }
    }

    // a relog through AutoRetainer can turn multi mode off depending on its settings
    private static void ResumeAutoRetainer(bool multiWasEnabled) {
        var ar = AutoRetainerIPC.Get();
        ar.SetSuppressed(false);
        if (multiWasEnabled && !ar.MultiModeEnabled)
            ar.EnableMultiMode();
    }

    #endregion

    #region Alerts

    public SeString BuildAlert(OceanVoyage voyage, string text) {
        DalamudLinkPayload GetLink(OceanVoyage voyage) {
            var id = voyage.RouteId;
            if (_links.TryGetValue(id, out var existing)) {
                _links[id] = existing with { Voyage = voyage };
                return existing.Payload;
            }
            var payload = IChatGui.Get().AddChatLinkHandler(id, (id, _) => {
                if (_links.TryGetValue(id, out var link))
                    TryStart(link.Voyage, OceanTripSource.Alert);
            });
            _links[id] = (payload, voyage);
            return payload;
        }

        var link = GetLink(voyage);
        return new SeStringBuilder()
            .AddText($"{ChatPrefix}{text} ")
            .AddUiForeground(500)
            .Add(link)
            .AddText(UIStrings.OceanTrip_GoLink)
            .Add(RawPayload.LinkTerminator)
            .AddUiForegroundOff()
            .Build();
    }

    #endregion

    #region AutoRetainer postprocess

    private void OnArCharacterAdditionalTask() {
        if (!Cfg.FullAuto || !Cfg.SafePoints.HasFlag(OceanTripSafePoint.ArPostprocess) || !IsArCharacter || Svc.Automation.Running)
            return;

        var now = Framework.GetServerTime();
        if (NextTripVoyage(now) is not { } voyage)
            return;
        // too early
        if (now < StartTime(voyage) - Math.Max(Cfg.ArLookaheadMinutes, 0) * 60L || now >= LatestStart(voyage))
            return;

        _arVoyage = voyage;
        AutoRetainerIPC.Get().RequestCharacterPostprocess();
        IPluginLog.Get().Information($"[OceanTrip] Requested AutoRetainer postprocess for {RouteName(voyage.RouteId)}");
    }

    private void OnArReadyForPostprocess() {
        var voyage = _arVoyage;
        _arVoyage = null;
        if (voyage is not { } v || !TryStart(v, OceanTripSource.ArPostprocess, AutoRetainerIPC.Get().FinishCharacterPostprocess))
            AutoRetainerIPC.Get().FinishCharacterPostprocess();
    }

    #endregion

    public static string RouteName(uint routeId) => IKDRoute.TryGetRow(routeId, out var row) ? row.Name.ToString() : $"#{routeId}";
}
