using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.Throttlers;
using Lumina.Excel.Sheets;

namespace AutoHook.Fishing;

public partial class FishingManager
{
    // ReSharper disable once UnusedMember.Local
    private void CheckFishingState()
    {
#if (DEBUG)
        if (!EzThrottler.Throttle(@"FishingState", 500))
            return;

        Service.PrintDebug(
            @$"[HookManager] Fishing State: {Service.BaitManager.FishingState}, LastStep: {_lastStep}");
#endif
    }

    private static void ResetAfkTimer()
    {
        if (!Service.Configuration.ResetAfkTimer)
            return;

        if (!InputUtil.TryFindGameWindow(out var windowHandle)) return;

        // Virtual key for Right Winkey. Can't be used by FFXIV normally, and in tests did not seem to cause any
        // unusual interference.
        InputUtil.SendKeycode(windowHandle, 0x5C);
    }

    private void AnimationCancel()
    {
        if (GetAutoCastCfg().RecastAnimationCancel)
            PlayerRes.CastAction(IDs.Actions.Collect);

        if (PlayerRes.HasStatus(IDs.Status.Salvage) && GetAutoCastCfg().ChumAnimationCancel)
            PlayerRes.CastAction(IDs.Actions.Salvage);
    }

    private void OnMessageDelegate(IHandleableChatMessage message)
    {
        try
        {
            if (message.LogKind is XivChatType.Gathering)
            {
                var text = message.Message.TextValue;
                if (GetHookCfg().GetHookset().CastLures.LureTarget != LureTarget.NotSpecial)
                {
                    // Check if a special fish is found
                    _lureSuccess = GameRes.LureFishes.FirstOrDefault(f => f.LureMessage == text) != null;
                    if (_lureSuccess)
                        return;
                }
                if (GetHookCfg().GetHookset().CastLures.LureTarget is LureTarget.Any or LureTarget.NotSpecial)
                {
                    _lureSuccess = FindRow<LogMessage>(x => x.Text.ToString() == text) is { RowId: XivChatLog.AmbLureSuccess or XivChatLog.ModLureSuccess };
                }

                if (FindRow<LogMessage>(x => x.Text.ToString() == text) is { RowId: XivChatLog.CantFish })
                    Service.Status = UIStrings.CantFishHere;
            }
        }
        catch (Exception e)
        {
            Svc.Log.Error(e.Message);
        }
    }

    // This is my stupid way of handling the counter for stop/quit fishing and bait/preset swap
    public static class FishingHelper
    {
        public static Dictionary<Guid, int> FishCount = [];
        public static List<Guid> FishPresetSwapped = [];
        public static List<Guid> FishBaitSwapped = [];

        public static List<Guid> ToBeRemoved = [];

        public static void AddFishCount(Guid guid)
        {
            FishCount.TryAdd(guid, 0);
            FishCount[guid]++;

            GetFishCount(guid);
        }

        public static void AddBaitSwap(Guid guid)
        {
            if (!FishBaitSwapped.Contains(guid))
                FishBaitSwapped.Add(guid);
        }

        public static void AddPresetSwap(Guid guid)
        {
            if (!FishPresetSwapped.Contains(guid))
                FishPresetSwapped.Add(guid);
        }

        public static void RemovePresetSwap(Guid guid)
        {
            if (SwappedPreset(guid))
                FishPresetSwapped.Remove(guid);
        }

        public static int GetFishCount(Guid guid)
        {
            return !FishCount.ContainsKey(guid) ? 0 : FishCount[guid];
        }

        public static bool SwappedBait(Guid guid)
        {
            return FishBaitSwapped.Any(g => g == guid);
        }

        public static bool SwappedPreset(Guid guid)
        {
            return FishPresetSwapped.Any(g => g == guid);
        }

        public static void RemoveId(Guid guid)
        {
            if (FishCount.ContainsKey(guid))
                FishCount.Remove(guid);

            if (SwappedPreset(guid))
                FishPresetSwapped.Remove(guid);

            if (SwappedBait(guid))
                FishBaitSwapped.Remove(guid);
        }

        public static void RemoveGuidQueue()
        {
            foreach (var guid in ToBeRemoved)
            {
                if (FishCount.ContainsKey(guid))
                    FishCount.Remove(guid);

                if (SwappedPreset(guid))
                    FishPresetSwapped.Remove(guid);

                if (SwappedBait(guid))
                    FishBaitSwapped.Remove(guid);
            }

            ToBeRemoved.Clear();
        }

        public static void Reset()
        {
            FishCount = [];
            FishPresetSwapped = [];
            FishBaitSwapped = [];
        }
    }
}
