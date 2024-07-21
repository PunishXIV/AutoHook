using System;
using System.Diagnostics;
using System.Linq;
using AutoHook.Classes;
using AutoHook.Configurations;
using AutoHook.Data;
using AutoHook.Enums;
using AutoHook.Resources.Localization;
using AutoHook.SeFunctions;
using AutoHook.Utils;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using ECommons.EzHookManager;
using ECommons.Logging;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AutoHook.Fishing;

public partial class FishingManager : IDisposable
{
    // todo: refactor this entire class
    private static readonly FishingPresets Presets = Service.Configuration.HookPresets;

    private double _timeout;
    private readonly Stopwatch _fishingTimer = new();

    private FishingState _lastState = FishingState.NotFishing;
    private FishingSteps _lastStep = 0;

    private BaitFishClass? _lastCatch;

    public static IntuitionStatus IntuitionStatus { get; private set; } = IntuitionStatus.NotActive;

    private SpectralCurrentStatus _spectralCurrentStatus = SpectralCurrentStatus.NotActive;

    private bool _isMooching;
    private bool _lureSuccess;

    private delegate bool UseActionDelegate(IntPtr manager, ActionType actionType, uint actionId, GameObjectId targetId,
        uint a4, uint a5,
        uint a6, IntPtr a7);

    private Hook<UseActionDelegate>? _useActionHook;

    public delegate void UpdateCatchDelegate(IntPtr module, uint fishId, bool large, ushort size, byte amount,
        byte level, byte unk7, byte unk8, byte unk9, byte unk10,
        byte unk11, byte unk12);

    public Hook<UpdateCatchDelegate>? UpdateCatch = null!;

    public FishingManager()
    {
        try
        {
            Service.TaskManager.EnqueueDelay(200);
            Service.TaskManager.Enqueue(() => CreateDalamudHooks());
            //CreateDalamudHooks();
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(@$"{e.Message}");
        }
    }

    public void Dispose()
    {
        Disable();
        _useActionHook?.Dispose();
        UpdateCatch?.Dispose();
    }

    public unsafe void CreateDalamudHooks()
    {
        UpdateCatch = Service.GameInteropProvider.HookFromSignature<UpdateCatchDelegate>(
            @"40 55 56 41 54 41 56 41 57 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 F7",
            UpdateCatchDetour);
        var hookPtr = (IntPtr)ActionManager.MemberFunctionPointers.UseAction;
        _useActionHook = Service.GameInteropProvider.HookFromAddress<UseActionDelegate>(hookPtr, OnUseAction);

        Enable();
    }

    private void Enable()
    {
        Service.Framework.Update += OnFrameworkUpdate;
        Service.Chat.CheckMessageHandled += OnMessageDelegate;
        UpdateCatch?.Enable();
        _useActionHook?.Enable();
    }

    private void Disable()
    {
        Service.Framework.Update -= OnFrameworkUpdate;
        Service.Chat.CheckMessageHandled -= OnMessageDelegate;
        _useActionHook?.Disable();
        UpdateCatch?.Disable();
    }

    public void StartFishing()
    {
        if (!PlayerRes.IsCastAvailable())
        {
            Service.PrintChat(@"[AutoHook] You can't cast right now.");
            return;
        }

        var extraCfg = GetExtraCfg();
        if (extraCfg is { ForceBaitSwap: true, Enabled: true })
        {
            var result = Service.BaitManager.ChangeBait((uint)extraCfg.ForcedBaitId);

            if (result == BaitManager.ChangeBaitReturn.Success)
            {
                Service.PrintChat(
                    @$"[AutoHook] Starting with bait: {MultiString.GetItemName(extraCfg.ForcedBaitId)}");
                Service.Save();
            }
        }

        _lastStep = FishingSteps.StartedCasting;
        UseAutoCasts();
        //Service.TaskManager.Enqueue(() => UseAutoCasts());
    }

    private int GetCurrentBaitMoochId()
    {
        if (_isMooching)
            return _lastCatch?.Id ?? 0;

        if (Service.BaitManager.CurrentSwimBait is { } fishId)
            return (int)fishId;

        return (int)Service.BaitManager.Current;
    }

    // The current config is updates two times: When we began fishing (to get the config based on the mooch/bait) and when we hooked the fish (in case the user updated their configs).
    private void UpdateStatusAndTimer()
    {
        ResetAfkTimer();

        var selected = GetHookCfg();
        var hookset = selected.GetHookset();
        if (selected.Enabled)
        {
            _timeout = PlayerRes.HasStatus(IDs.Status.Chum)
                ? hookset.ChumTimeoutMax
                : hookset.TimeoutMax;
        }
        else
            _timeout = 0;

        if (Service.Configuration.ShowStatus)
        {
            string buffStatus = "";

            if (hookset.RequiredStatus != 0)
            {
                buffStatus = MultiString.GetStatusName(hookset.RequiredStatus);
                buffStatus = @$"({buffStatus})";
            }

            var hookCfgName = GetPresetName();

            string message = !selected.Enabled
                ? @$"No config found. Not hooking"
                : @$"Hook Found: {hookCfgName} {buffStatus}";

            Service.Status = message;
            Service.PrintDebug(@$"[HookManager] {message}");
        }
    }

    public string GetPresetName()
    {
        var customHook = Presets.SelectedPreset?.GetCfgById(GetCurrentBaitMoochId());

        var globalHook = _isMooching
            ? Presets.DefaultPreset.ListOfMooch.FirstOrDefault()
            : Presets.DefaultPreset.ListOfBaits.FirstOrDefault();

        var presetName = customHook?.Enabled ?? false
            ? @$"{customHook.BaitFish.Name} ({Presets.SelectedPreset?.PresetName})"
            : globalHook?.Enabled ?? false
                ? @$"{(_isMooching ? UIStrings.All_Mooches : UIStrings.All_Baits)} ({Presets.DefaultPreset.PresetName})"
                : @"None";

        return presetName;
    }

    public HookConfig GetHookCfg()
    {
        var globalHook = Presets.SelectedPreset?.GetCfgById(GetCurrentBaitMoochId());

        var defaultHook = _isMooching
            ? Presets.DefaultPreset.ListOfMooch.FirstOrDefault()
            : Presets.DefaultPreset.ListOfBaits.FirstOrDefault();

        var currentHook = globalHook?.Enabled ?? false ? globalHook : defaultHook!;

        return currentHook;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var currentState = Service.BaitManager.FishingState;

        if (!Service.Configuration.PluginEnabled || currentState == FishingState.NotFishing)
            return;

        if (currentState != FishingState.Quit && _lastStep.HasFlag(FishingSteps.Quitting))
        {
            if (PlayerRes.IsCastAvailable())
            {
                PlayerRes.CastActionDelayed(IDs.Actions.Quit, ActionType.Action, @"Quit");
                currentState = FishingState.Quit;
            }
        }

        //CheckFishingState();

        if (!_lastStep.HasFlag(FishingSteps.Quitting) && currentState == FishingState.PoleReady)
            CheckPluginActions();

        if (currentState == FishingState.NormalFishing || currentState == FishingState.LureFishing)
        {
            CheckWhileFishingActions();
            CheckTimeout();
        }

        if (_lastState == currentState)
            return;

        if (currentState == FishingState.PoleReady)
            Service.Status = @$"";

        _lastState = currentState;

        switch (currentState)
        {
            case FishingState.PullPoleIn: // If a hook is manually used before a bite, don't use auto cast
                if (_lastStep.HasFlag(FishingSteps.BeganFishing))
                    _lastStep = FishingSteps.None;
                else AnimationCancel();
                _fishingTimer.Reset();
                break;
            case FishingState.PoleOut:
                if (!_fishingTimer.IsRunning) _fishingTimer.Start();
                break;
            case FishingState.Bite:
                if (!_lastStep.HasFlag(FishingSteps.FishBit)) Service.TaskManager.Enqueue(OnBite);
                break;
            case FishingState.Quit:
                OnFishingStop();
                break;
        }
    }

    private void CheckPluginActions()
    {
        if (!EzThrottler.Throttle(@"CheckPluginActions", 500))
            return;

        var lastCatch = GetLastCatchConfig();
        var extraCfg = GetExtraCfg();

        if (_lastStep.HasFlag(FishingSteps.FishCaught))
            CheckStopCondition();

        // the order matters
        CheckExtraActions(extraCfg);

        bool casted = false;
        if (_lastStep.HasFlag(FishingSteps.FishCaught) && !_lastStep.HasFlag(FishingSteps.Quitting))
        {
            casted = UseFishCaughtActions(lastCatch);
            CheckFishCaughtSwap(lastCatch);
        }

        if (!casted)
            UseAutoCasts();
    }

    private void OnBeganFishing(bool mooching)
    {
        if (_lastStep.HasFlag(FishingSteps.BeganFishing) &&
            (_lastState != FishingState.PoleReady || _lastState != FishingState.NotFishing))
            return;

        _isMooching = mooching;
        _lureSuccess = false;

        var baitname = MultiString.GetItemName(GetCurrentBaitMoochId());
        if (!_isMooching)
        {
            _isMooching = Service.BaitManager.CurrentSwimBait != null;
            Service.PrintDebug(@$"Started fishing with {(_isMooching ? @"Swimbait" : @"normal bait")}: {baitname}");
        }
        else
            Service.PrintDebug(@$"Started mooching with {baitname}");

        _lastStep = FishingSteps.BeganFishing;

        Service.TaskManager.EnqueueDelay(2500);
        Service.TaskManager.Enqueue(CastCollect);

        UpdateStatusAndTimer();
    }

    private void CheckTimeout()
    {
        if (!_fishingTimer.IsRunning)
            _fishingTimer.Start();

        double maxTime = Math.Truncate(_timeout * 100) / 100;

        var currentTime = Math.Truncate(_fishingTimer.ElapsedMilliseconds / 1000.0 * 100) / 100;

        if (!(maxTime > 0) || !(currentTime > maxTime) || _lastStep.HasFlag(FishingSteps.TimeOut) ||
            _lastStep.HasFlag(FishingSteps.Reeling))
            return;

        Service.PrintDebug(@"[HookManager] Timeout. Hooking fish.");
        PlayerRes.CastActionDelayed(IDs.Actions.Rest, ActionType.Action, UIStrings.Hook);
        _lastStep = FishingSteps.TimeOut;
    }

    private void OnBite()
    {
        UpdateStatusAndTimer();
        var currentHook = GetHookCfg();
        _fishingTimer.Stop();

        _lastCatch = null;
        _lastStep = FishingSteps.FishBit;
        HookFish(Service.TugType?.Bite ?? BiteType.Unknown, currentHook);
    }

    private void HookFish(BiteType bite, HookConfig currentHook)
    {
        var delay = new Random().Next(Service.Configuration.DelayBetweenHookMin,
            Service.Configuration.DelayBetweenHookMax);

        if (!currentHook.Enabled)
            return;

        var timePassed = Math.Truncate(_fishingTimer.ElapsedMilliseconds / 1000.0 * 100) / 100;

        var hook = currentHook.GetHook(bite, timePassed);

        if (hook is null or HookType.None)
        {
            delay = new Random().Next(Service.Configuration.DelayBeforeCancelMin,
                Service.Configuration.DelayBeforeCancelMax);

            Service.TaskManager.EnqueueDelay(delay);
            Service.TaskManager.Enqueue(() => PlayerRes.CastAction(IDs.Actions.Rest));
            //_lastStep = FishingSteps.Reeling;
            Service.PrintDebug(@$"[HookManager] No hook found, using Rest");
            return;
        }

        Service.TaskManager.EnqueueDelay(delay);
        Service.TaskManager.Enqueue(() =>
            PlayerRes.CastActionDelayed((uint)hook, ActionType.Action, @$"{hook.ToString()}"));
        Service.PrintDebug(@$"[HookManager] Using {hook.ToString()} hook. (Bite: {bite})");
    }

    private void OnCatch(uint fishId, uint amount)
    {
        _lastCatch = GameRes.Fishes.FirstOrDefault(fish => fish.Id == fishId) ?? new BaitFishClass(@"-", -1);
        var lastFishCatchCfg = GetLastCatchConfig();

        Service.LastCatch = _lastCatch;

        Service.PrintDebug(@$"[HookManager] Caught {_lastCatch.Name} (id {_lastCatch.Id})");

        _lastStep = FishingSteps.FishCaught;

        if (lastFishCatchCfg != null)
        {
            for (var i = 0; i < amount; i++)
            {
                FishingHelper.AddFishCount(lastFishCatchCfg.UniqueId);
            }
        }

        var hook = GetHookCfg();
        if (hook.Enabled)
            FishingHelper.AddFishCount(hook.UniqueId);
    }

    private void CheckStopCondition()
    {
        var lastFishCatchCfg = GetLastCatchConfig();
        var currentHook = GetHookCfg();
        var hookset = currentHook.GetHookset();
        var extra = GetExtraCfg();

        if (lastFishCatchCfg?.StopAfterCaught ?? false)
        {
            var guid = lastFishCatchCfg.UniqueId;
            var total = FishingHelper.GetFishCount(guid);

            if (total >= lastFishCatchCfg.StopAfterCaughtLimit)
            {
                Service.PrintChat(string.Format(UIStrings.Caught_Limited_Reached_Chat_Message,
                    @$"{lastFishCatchCfg.Fish.Name}: {lastFishCatchCfg.StopAfterCaughtLimit}"));

                _lastStep |= lastFishCatchCfg.StopFishingStep;
                if (lastFishCatchCfg.StopAfterResetCount) FishingHelper.RemoveId(guid);
            }
        }

        if (currentHook.Enabled && hookset.StopAfterCaught)
        {
            var guid = currentHook.UniqueId;
            var total = FishingHelper.GetFishCount(guid);

            if (total >= hookset.StopAfterCaughtLimit)
            {
                Service.PrintChat(string.Format(UIStrings.Hooking_Limited_Reached_Chat_Message,
                    @$"{currentHook.BaitFish.Name}: {hookset.StopAfterCaughtLimit}"));

                _lastStep |= hookset.StopFishingStep;
                if (hookset.StopAfterResetCount) FishingHelper.RemoveId(guid);
            }
        }

        if (extra.StopAfterAnglersArt && extra.Enabled)
        {
            if (!PlayerRes.HasAnglersArtStacks(extra.AnglerStackQtd))
                return;

            _lastStep |= extra.AnglerStopFishingStep;
            Service.PrintChat(@$"[Extra] Angler's Stack Reached: Stopping fishing");
        }
    }

    private void OnFishingStop()
    {
        _lastStep = FishingSteps.None;

        if (_fishingTimer.IsRunning)
            _fishingTimer.Reset();

        Service.Status = "";

        FishingHelper.Reset();

        PlayerRes.CastActionNoDelay(IDs.Actions.Quit);
        PlayerRes.DelayNextCast(0);
    }

    private bool OnUseAction(IntPtr manager, ActionType actionType, uint actionId, GameObjectId targetId, uint a4,
        uint a5, uint a6, IntPtr a7)
    {
        try
        {
            if (actionType == ActionType.Action && Service.Configuration.PluginEnabled &&
                PlayerRes.ActionTypeAvailable(actionId))
            {
                switch (actionId)
                {
                    case IDs.Actions.Rest:
                        // till call will make sure Collectors glove is off
                        if (PlayerRes.HasStatus(IDs.Status.CollectorsGlove)) AnimationCancel();
                        _lastStep = FishingSteps.Reeling;
                        break;
                    case IDs.Actions.Cast:
                        OnBeganFishing(false);
                        break;
                    case IDs.Actions.Mooch:
                    case IDs.Actions.Mooch2:
                        OnBeganFishing(true);
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Service.PrintDebug(@$"[HookManager] Error: {e.Message}");
        }

        return _useActionHook!.Original(manager, actionType, actionId, targetId, a4, a5, a6, a7);
    }

    private void UpdateCatchDetour(IntPtr module, uint fishId, bool large, ushort size, byte amount, byte level,
        byte unk7,
        byte unk8, byte unk9, byte unk10, byte unk11, byte unk12)
    {
        UpdateCatch!.Original(module, fishId, large, size, amount, level, unk7, unk8, unk9, unk10, unk11, unk12);

        // Check against collectibles.
        if (fishId > 500000)
            fishId -= 500000;

        OnCatch(fishId, amount);
    }
}