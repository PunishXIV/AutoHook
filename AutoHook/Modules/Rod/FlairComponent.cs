using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace AutoHook.Modules.Rod;

public sealed class FlairComponent(RodFishingModule module) : RodComponent(module) {
    private static readonly Dictionary<ushort, bool> SeatedTimelines = [];

    public void TryUse() {
        var cfg = Configuration.C;
        if (!cfg.AutoSit && !cfg.AutoCastLight)
            return;

        if (Rod.IsBusy || Rod.SpectralRestPending)
            return;

        if (cfg.FlairSkipOceanFishing && Ws.OceanFishing != OceanFishingState.Empty)
            return;

        if (!EzThrottler.Throttle("FishingFlair", 1000))
            return;

        if (cfg.AutoSit && IsSeated() == false && TrySit())
            return;

        if (cfg.AutoCastLight && Rod.FishTimerSecs >= 2 && IsRodLit() == false && Ws.ActionAvailable(IDs.Actions.CastLight, ActionType.Action))
            Rod.ExecuteImmediate(new ActionRequest(IDs.Actions.CastLight, ActionType.Action, "Cast Light", UseRaw: true));
    }

    private static unsafe bool? IsRodLit() {
        var player = Control.GetLocalPlayer();
        if (player == null)
            return null;

        // TODO: swap to player->LifeSkillContainer.ToolVfx == LifeSkillVfx.FishingRodLight when CS updates
        var container = (byte*)&player->LifeSkillContainer;
        return container[0x1C] == 16;
    }

    private static unsafe bool? IsSeated() {
        var player = Control.GetLocalPlayer();
        if (player == null)
            return null;

        var timelineId = player->Timeline.TimelineSequencer.GetSlotTimeline(0);
        if (timelineId == 0)
            return null;

        if (!SeatedTimelines.TryGetValue(timelineId, out var seated)) {
            seated = ActionTimeline.TryGetRow(timelineId, out var row) && row.Key.ToString().StartsWith("fishing_chair/", StringComparison.Ordinal);
            SeatedTimelines[timelineId] = seated;
        }

        return seated;
    }

    private static unsafe bool TrySit() {
        var agent = AgentEmote.Instance();
        if (agent == null)
            return false;

        agent->ExecuteEmote(IDs.Emotes.Sit, null, addToHistory: false, liveUpdateHistory: false);
        return true;
    }
}
