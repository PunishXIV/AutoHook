using AutoHook.Spearfishing;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using SFEnums = AutoHook.Spearfishing.Enums;

namespace AutoHook.Modules.Gig;

public sealed class GigComponent(GigFishingModule module) : GigFishingComponent(module) {
    private static SpearFishingPresets GigCfg => Configuration.C.AutoGigConfig;

    public override void Update() {
        if (!Configuration.C.PluginEnabled || !GigCfg.AutoGigEnabled)
            return;
        if (ActionExecutor.Get().IsBusy)
            return;
        TryGigFromWorldState();
    }

    public BaseGig? FindGigForFish(AddonSpearFishing.FishInfo info) {
        if (GigCfg.IsCatchAllActive)
            return GigCfg.CatchAllConditionSet.PassesOrUnconfigured(Ws) ? new BaseGig(0) { Enabled = true } : null;

        var notebookId = Ws.Spearfishing.Spot.NotebookId;
        return GigCfg.SelectedPreset?.FindGigForPool(notebookId, (SFEnums.SpearfishSpeed)info.Speed, (SFEnums.SpearfishSize)info.Size);
    }

    public bool ShouldGig(BaseGig? fish)
        => fish is { Enabled: true } && fish.GigConditionSet.PassesOrUnconfigured(Ws);

    public void ContributeConditionsGigHints(ActionHints hints, BaseGig fish, bool useCatchAll) {
        var naturesBounty = useCatchAll ? GigCfg.CatchAllNaturesBountyAction : fish.NaturesBounty;
        if (naturesBounty.IsAvailableToCast(Ws)) {
            hints.AddCast(HintPriority.GigNaturesBounty, new ActionRequest(naturesBounty.Id, naturesBounty.ActionType, naturesBounty.GetName()), HintSource.Gig, detail: "Natures Bounty before gig", context: DecisionContext.AutoCast);
        }
    }

    public void ContributeGigHints(ActionHints hints, BaseGig fish, bool useCatchAll) {
        Gig.LastGigEntryId = useCatchAll ? Guid.Empty : fish.UniqueId;
        hints.AddCast(HintPriority.Gig, new ActionRequest(IDs.Actions.Gig, ActionType.Action, AutoGig.Gig), HintSource.Gig, detail: useCatchAll ? "Catch all" : fish.Fish?.Name ?? "Gig", context: DecisionContext.AutoCast);
    }

    public void TryGigFromWorldState() {
        var sf = Ws.Spearfishing;
        if (!sf.WindowOpen || sf.LaneLayout.IsEmpty)
            return;

        var useCatchAll = GigCfg.IsCatchAllActive;
        for (var i = 0; i < 3; i++) {
            if (ActionExecutor.Get().IsBusy)
                return;

            var layout = sf.GetFishLayout(i);
            if (!layout.Available)
                continue;

            var fish = FindGigForFish(layout.Fish);
            if (fish == null || !ShouldGig(fish))
                continue;

            Gig.Hints.Clear();
            ContributeConditionsGigHints(Gig.Hints, fish, useCatchAll);
            Gig.Resolver.Resolve(Ws, Gig.Hints);

            if (!IsFishInHitbox(layout, fish, sf.LaneLayout))
                continue;

            Gig.Hints.Clear();
            ContributeGigHints(Gig.Hints, fish, useCatchAll);
            Gig.Resolver.Resolve(Ws, Gig.Hints);
        }
    }

    public static bool IsFishInHitbox(SpearFishLayout fishLayout, BaseGig fish, SpearLaneLayout lane) {
        var uiScale = lane.UiScale;
        var laneOriginX = lane.LaneX * uiScale;
        var centerX = laneOriginX + lane.LaneWidth * lane.LaneScaleX * uiScale / 2f;
        var anchor = fishLayout.Fish.InverseDirection ? 0.5f + fish.RightOffset / 10 : 0.4f - fish.LeftOffset / 10;
        var fishHitbox = laneOriginX + fishLayout.FishNodeX * uiScale + fishLayout.FishNodeWidth * fishLayout.FishNodeScaleX * uiScale * anchor;
        var gigHitbox = GigCfg.ActiveHitboxSize;
        return fishHitbox >= centerX - gigHitbox && fishHitbox <= centerX + gigHitbox;
    }
}
