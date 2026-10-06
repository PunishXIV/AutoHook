using Dalamud.Hooking;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;
using System.Runtime.InteropServices;
using AchievementStruct = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;

namespace AutoHook.World.UpdateModules;

public sealed class HooksUpdateModule : IAsyncDisposable {
    private const byte GpGain = 13;

    private delegate void EffectResultDetourDelegate(uint targetId, byte* packet, byte replaying);

    private readonly Action _markInventoryDirty;
    private readonly Hook<ActionManager.Delegates.UseAction>? _useActionHook;
    private readonly Hook<AgentCatch.Delegates.UpdateCatch>? _updateCatchHook;
    private readonly Hook<FishingEventHandler.Delegates.PlayAnimation>? _playAnimationHook;
    private readonly Hook<PacketDispatcher.Delegates.HandleActorControlPacket>? _handleActorControlPacketHook;
    private readonly Hook<AchievementStruct.Delegates.ReceiveAchievementProgress>? _receiveAchievementProgressHook;
    private readonly Hook<ActionEffectHandler.Delegates.Receive>? _receiveActionEffectHook;
    private readonly Hook<EffectResultDetourDelegate>? _effectResultHook;
    private readonly Dictionary<(uint Seq, byte TargetIndex), int> _pendingGp = [];

    public bool HasPendingGp => _pendingGp.Count > 0;

    public unsafe HooksUpdateModule(Action markInventoryDirty) {
        _markInventoryDirty = markInventoryDirty;
        _updateCatchHook = IGameInteropProvider.Get().HookFromAddress<AgentCatch.Delegates.UpdateCatch>((nint)AgentCatch.MemberFunctionPointers.UpdateCatch, UpdateCatchDetour);
        _useActionHook = IGameInteropProvider.Get().HookFromAddress<ActionManager.Delegates.UseAction>((nint)ActionManager.MemberFunctionPointers.UseAction, UseActionDetour);
        _playAnimationHook = IGameInteropProvider.Get().HookFromAddress<FishingEventHandler.Delegates.PlayAnimation>((nint)FishingEventHandler.StaticVirtualTablePointer->PlayAnimation, PlayAnimationDetour);
        _handleActorControlPacketHook = IGameInteropProvider.Get().HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>((nint)PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket, HandleActorControlPacketDetour);
        _receiveAchievementProgressHook = IGameInteropProvider.Get().HookFromAddress<AchievementStruct.Delegates.ReceiveAchievementProgress>((nint)AchievementStruct.MemberFunctionPointers.ReceiveAchievementProgress, ReceiveAchievementProgressDetour);
        _receiveActionEffectHook = IGameInteropProvider.Get().HookFromAddress<ActionEffectHandler.Delegates.Receive>((nint)ActionEffectHandler.MemberFunctionPointers.Receive, ActionEffectDetour);
        _effectResultHook = IGameInteropProvider.Get().HookFromSignature<EffectResultDetourDelegate>("48 8B C4 44 88 40 18 89 48 08", EffectResultDetour);
        _updateCatchHook?.Enable();
        _useActionHook?.Enable();
        _playAnimationHook?.Enable();
        _handleActorControlPacketHook?.Enable();
        _receiveAchievementProgressHook?.Enable();
        _receiveActionEffectHook?.Enable();
        _effectResultHook?.Enable();
    }

    public async ValueTask DisposeAsync() {
        await _useActionHook.DisposeAsync();
        await _updateCatchHook.DisposeAsync();
        await _playAnimationHook.DisposeAsync();
        await _handleActorControlPacketHook.DisposeAsync();
        await _receiveAchievementProgressHook.DisposeAsync();
        await _receiveActionEffectHook.DisposeAsync();
        await _effectResultHook.DisposeAsync();
    }

    private unsafe bool UseActionDetour(ActionManager* thisPtr, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted) {
        try {
            if (actionType == ActionType.Action && Configuration.C.PluginEnabled && WorldState.Get().ActionAvailable(actionId, actionType))
                WorldState.Get().Execute(new RodState.OpPlayerUsedAction(new UsedAction(actionId, actionType)));
        }
        catch (Exception e) {
            IPluginLog.Get().Warning(e, "[WorldStateUpdater] UseAction");
        }
        return _useActionHook!.Original(thisPtr, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
    }

    private unsafe void UpdateCatchDetour(AgentCatch* thisPtr, uint itemId, bool isLarge, ushort size, byte amount, byte level, byte stars, byte oceanStars, bool isMoochable, bool isFirstTimeCatch, byte a11, byte a12) {
        _updateCatchHook!.Original(thisPtr, itemId, isLarge, size, amount, level, stars, oceanStars, isMoochable, isFirstTimeCatch, a11, a12);
        if (ItemUtil.GetBaseId(itemId) is { ItemId: > 0 and var id }) {
            WorldState.Get().Execute(new RodState.OpSetLastCatch(new CatchInfo(id, amount, isLarge, size, level, stars, oceanStars, isMoochable, isFirstTimeCatch)));
        }
        WorldState.Get().Execute(new RodState.OpSetFishingStep(FishingSteps.FishCaught));
    }

    private unsafe void ReceiveAchievementProgressDetour(AchievementStruct* thisPtr, uint id, uint current, uint max) {
        _receiveAchievementProgressHook!.Original(thisPtr, id, current, max);
        WorldState.Get().Execute(new WorldState.OpAchievementProgress(id, current, max));
    }

    private unsafe bool PlayAnimationDetour(FishingEventHandler* thisPtr, Character* chara, ushort actionTimelineId, ulong a4) {
        var tugType = (FishingHookStrength)actionTimelineId;
        if (tugType is FishingHookStrength.Weak or FishingHookStrength.Strong or FishingHookStrength.Legendary) {
            WorldState.Get().Execute(new RodState.OpTugType(tugType));
        }
        else {
            WorldState.Get().Execute(new RodState.OpTugType(0));
        }

        return _playAnimationHook!.Original(thisPtr, chara, actionTimelineId, a4);
    }

    private void HandleActorControlPacketDetour(uint entityId, uint category, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, uint arg6, uint arg7, uint arg8, GameObjectId targetId, bool isRecorded) {
        _handleActorControlPacketHook!.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
        switch (category) {
            case 3702: // WKSMissionEnd? there's also 3500 but that triggers on start and end but with a different a1
            case 3501: // WKSMissionItemGain or something. a1 is the itemid
                _markInventoryDirty();
                break;
        }
    }

    private unsafe void ActionEffectDetour(uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds) {
        var me = UIState.Instance()->PlayerState.EntityId;
        for (var i = 0; i < header->NumTargets; i++) {
            var targetId = targetEntityIds[i].ObjectId;
            var te = effects[i];
            for (var j = 0; j < 8; j++) {
                var e = te.Effects[j];
                if (e.Type != GpGain)
                    continue;
                var atSource = (e.Param4 & 0x80) != 0;
                var affectsSelf = atSource ? casterEntityId == me : targetId == me;
                if (!affectsSelf)
                    continue;
                _pendingGp[(header->GlobalSequence, (byte)i)] = e.Value;
            }
        }
        _receiveActionEffectHook!.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }

    private unsafe void EffectResultDetour(uint targetId, byte* packet, byte replaying) {
        if (targetId == UIState.Instance()->PlayerState.EntityId) {
            var count = packet[0];
            var p = (EffectResultEntry*)(packet + 4);
            for (var i = 0; i < count; i++, p++)
                _pendingGp.Remove((p->RelatedActionSequence, p->RelatedTargetIndex));
        }
        _effectResultHook!.Original(targetId, packet, replaying);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct EffectResultEntry {
        public uint RelatedActionSequence;
        public uint ActorID;
        public uint CurHP;
        public uint MaxHP;
        public ushort CurMP;
        public byte RelatedTargetIndex;
        public byte ClassID;
        public byte ShieldValue;
        public byte EffectCount;
        public ushort Pad;
    }
}
