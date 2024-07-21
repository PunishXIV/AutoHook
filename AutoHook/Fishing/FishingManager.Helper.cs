using System;
using System.Collections.Generic;
using System.Linq;
using AutoHook.Data;
using AutoHook.Enums;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.Throttlers;
using Lumina.Excel.GeneratedSheets;

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
    }
    
    private const XivChatType FishingMessage = (XivChatType)2243;
    private const XivChatType SystemAlert = (XivChatType)2115; //idk what to call this

    
    private void OnMessageDelegate(XivChatType type, int timeStamp, ref SeString sender, ref SeString messageSe,
        ref bool isHandled)
    {
        try
        {
            if (type is FishingMessage)
            {
                var text = messageSe.TextValue;
                var logId = Service.DataManager.GetExcelSheet<LogMessage>()?.FirstOrDefault(x => x.Text.ToString() == text)?.RowId;

                // Check if a special fish is found
                _lureSuccess = GameRes.LureFishes.FirstOrDefault(f => f.LureMessage == text) != null;

                if (_lureSuccess)
                    return;

                if (GetHookCfg().GetHookset().CastLures.LureTarget == LureTarget.Any)
                {
                    _lureSuccess = logId is XivChatLog.AmbLureSuccess or XivChatLog.ModLureSuccess;
                }
                
            }
            else if (type is SystemAlert)
            {
                var text = messageSe.TextValue;
                var logId = Service.DataManager.GetExcelSheet<LogMessage>()?.FirstOrDefault(x => x.Text.ToString() == text)?.RowId;
                
                if (logId is XivChatLog.CantFish)
                    Service.Status = UIStrings.CantFishHere;
                
            }
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(e.Message);
        }
    }
    
    // This is my stupid way of handling the counter for stop/quit fishing and bait/preset swap
    public static class FishingHelper
    {
        public static Dictionary<Guid, int> FishCount = new();
        public static List<Guid> FishPresetSwapped = new();
        public static List<Guid> FishBaitSwapped = new();

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

        public static void Reset()
        {
            FishCount = new Dictionary<Guid, int>();
            FishPresetSwapped = [];
            FishBaitSwapped = [];
        }
    }
}