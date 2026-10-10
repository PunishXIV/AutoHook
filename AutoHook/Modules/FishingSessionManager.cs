using AutoHook.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace AutoHook.Modules;

public sealed class FishingSessionManager : IPluginService, IDisposable {
    private const uint FisherJobId = 18;

    public RodFishingModule Rod { get; }
    public GigFishingModule Gig { get; }

    private static WorldState Ws => WorldState.Get();

    public FishingSessionManager() {
        Rod = new RodFishingModule(WorldState.Get());
        Gig = new GigFishingModule(WorldState.Get());
        IFramework.Get().Update += OnFrameworkUpdate;
    }

    public void Dispose() {
        IFramework.Get().Update -= OnFrameworkUpdate;
        Rod.Dispose();
        Gig.Dispose();
    }

    public void StartFishing() => Rod.StartFishing();

    public void RequestStopAfterNextFish() => Rod.RequestStopAfterNextFish();

    private void OnFrameworkUpdate(IFramework _) {
        if (ECommonsMain.Disposed) // check for this cause EC disposes before this and thus EzThrottler disappears
            return;

        if (!Configuration.C.PluginEnabled || !IClientState.Get().IsLoggedIn || IObjectTable.Get().LocalPlayer == null) {
            if (!Configuration.C.PluginEnabled && Svc.Automation.CurrentTask is AutoOceanFish)
                Svc.Automation.Stop();

            var sf = Ws.Spearfishing;
            if (sf.SessionActive || sf.WindowOpen || !sf.Spot.IsEmpty || sf.Wariness != 0)
                Ws.Execute(new SpearfishingState.OpEndSession());
            return;
        }

        WorldStateUpdater.Get().Update();

        if (Svc.Automation.CurrentTask is AutoOceanFish && Ws.Fishing.FishingState != FishingState.None) {
            Ws.Decide(DecisionContext.OceanPreset, true, "Stop task", "already fishing");
            Svc.Automation.Stop();
        }

        if (Player.ClassJob.RowId != FisherJobId) {
            Rod.ClearWorldState();
            return;
        }

        if (Ws.Spearfishing.WindowOpen || Ws.Spearfishing.SessionActive)
            Gig.Update();
        else
            Rod.Update();
    }
}
