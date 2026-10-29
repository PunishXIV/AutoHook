using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Utility;

namespace AutoHook.Modules.Rod;

public sealed class LureChatComponent(RodFishingModule module) : RodComponent(module) {
    public void AnimationCancel() {
        if (Rod.GetAutoCastCfg().RecastAnimationCancel)
            Rod.ExecuteImmediate(new ActionRequest(IDs.Actions.Collect, UseRaw: true));

        if (Ws.Player.HasStatus(IDs.Status.Salvage) && Rod.GetAutoCastCfg().ChumAnimationCancel)
            Rod.ExecuteImmediate(new ActionRequest(IDs.Actions.Salvage, UseRaw: true));
    }

    public void OnLogMessage(ILogMessage message) {
        var isGenericLure = message.LogMessageId is LogMessageIds.AmbLureSuccess or LogMessageIds.ModLureSuccess;
        var active = Rod.GetHookCfg().GetHookset().CastLures.GetActiveOption(Ws);
        if (active != null && AutoLures.MatchesLureSuccess(active.Value.Target, isGenericLure, isSpecialLure: false))
            Ws.Execute(new RodState.OpSetLureSuccess(true));

        if (message.LogMessageId is LogMessageIds.CantFish)
            PluginUi.Status = UIStrings.CantFishHere;

        if (message.LogMessageId is LogMessageIds.CastLine && message.TryGetIntParameter(0, out var placeNameId) && placeNameId > 0)
            Rod.FishingSpotId = FishSpotCatalog.FindFishingSpot((uint)placeNameId, Ws.TerritoryId);

        if (message.LogMessageId is LogMessageIds.SurfaceSlap or LogMessageIds.SurfaceSlapAlt) {
            if (!message.TryGetIntParameter(0, out var itemId) || itemId <= 0)
                return;
            Ws.Execute(new RodState.OpSetSlappedFish(ItemUtil.GetBaseId((uint)itemId).ItemId));
        }
    }

    public void CheckForSpecialLure(IHandleableChatMessage message) {
        if (message.LogKind is not XivChatType.Gathering) return;
        var isSpecialLure = FishBaitCatalog.Get().LureFishes.FirstOrDefault(f => f.LureMessage == message.Message.TextValue) != null;
        var active = Rod.GetHookCfg().GetHookset().CastLures.GetActiveOption(Ws);
        if (active != null && AutoLures.MatchesLureSuccess(active.Value.Target, isGenericLure: false, isSpecialLure))
            Ws.Execute(new RodState.OpSetLureSuccess(true));
    }
}
