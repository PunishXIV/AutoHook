using System.Linq;
using System.Runtime.InteropServices;
using AutoHook.Classes;
using AutoHook.Enums;
using AutoHook.Utils;
using Dalamud.Game;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace AutoHook.SeFunctions;

public unsafe class BaitManager
{
    public BaitManager()
    {
        Service.GameInteropProvider.InitializeFromAttributes(this);
    }

    private delegate byte ExecuteCommandDelegate(int id, int unk1, uint baitId, int unk2, int unk3);

    [Signature("E8 ?? ?? ?? ?? 41 C6 04 24")]
    private readonly ExecuteCommandDelegate _executeCommand = null!;

    private const int FishingManagerOffset = 0x70;

    internal FishingManagerStruct* FishingMan
    {
        get
        {
            var managerPtr = (nint)EventFramework.Instance() + FishingManagerOffset;

            if (managerPtr == nint.Zero)
                return null;

            if (managerPtr == nint.Zero)
                return null;

            return *(FishingManagerStruct**)managerPtr;
        }
    }

    public FishingState FishingState
    {
        get
        {
            var ptr = FishingMan;
            return ptr != null ? ptr->FishingState : FishingState.NotFishing;
        }
    }

    public uint? CurrentSwimBait
    {
        get
        {
            var ptr = FishingMan;
            if (ptr == null)
                return null;

            return ptr->CurrentSelectedSwimBait switch
            {
                0x00 when ptr->SwimBaitId1 != 0 => ptr->SwimBaitId1,
                0x01 when ptr->SwimBaitId2 != 0 => ptr->SwimBaitId2,
                0x02 when ptr->SwimBaitId3 != 0 => ptr->SwimBaitId3,
                _ => null,
            };
        }
    }

    public uint Current => PlayerState.Instance()->FishingBait;
    
    public uint CurrentBaitSwimBait => CurrentSwimBait ?? Current;

    public ChangeBaitReturn ChangeBait(uint baitId)
    {
        if (baitId == Current)
            return ChangeBaitReturn.AlreadyEquipped;

        if (baitId == 0 || GameRes.Baits.All(b => b.Id != baitId))
            return ChangeBaitReturn.InvalidBait;

        if (PlayerRes.HasItem(baitId) <= 0)
            return ChangeBaitReturn.NotInInventory;

        return _executeCommand(701, 4, baitId, 0, 0) == 1 ? ChangeBaitReturn.Success : ChangeBaitReturn.UnknownError;
    }


    public ChangeBaitReturn ChangeSwimbait(uint id)
    {
        if (id > 2)
            return ChangeBaitReturn.InvalidBait;

        return _executeCommand(701, 25, id, 0, 0) == 1 ? ChangeBaitReturn.Success : ChangeBaitReturn.UnknownError;
    }

    public ChangeBaitReturn ChangeBait(BaitFishClass bait)
    {
        if (bait.Id == Current)
        {
            Service.PrintChat($"Bait \"{bait.Name}\" is already equipped.");
            return ChangeBaitReturn.AlreadyEquipped;
        }

        if (bait.Id == 0 || GameRes.Baits.All(b => b.Id != bait.Id))
        {
            Service.PrintChat($"Bait \"{bait.Name}\" is not a valid bait.");
            return ChangeBaitReturn.InvalidBait;
        }

        if (PlayerRes.HasItem((uint)bait.Id) <= 0)
        {
            Service.PrintChat($"Bait \"{bait.Name}\" is not in your inventory.");
            return ChangeBaitReturn.NotInInventory;
        }

        return _executeCommand(701, 4, (uint)bait.Id, 0, 0) == 1
            ? ChangeBaitReturn.Success
            : ChangeBaitReturn.UnknownError;
    }

    public enum ChangeBaitReturn
    {
        Success,
        AlreadyEquipped,
        NotInInventory,
        InvalidBait,
        UnknownError,
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct FishingManagerStruct
    {
        [FieldOffset(0x220)] public FishingState FishingState;

        [FieldOffset(0x234)] public byte CurrentSelectedSwimBait;

        [FieldOffset(0x238)] public uint SwimBaitId1;

        [FieldOffset(0x23C)] public uint SwimBaitId2;

        [FieldOffset(0x240)] public uint SwimBaitId3;
    }
}