using clib.TaskSystem;
using System.Numerics;

namespace AutoHook.Tasks;

public sealed class AutoOceanFish(FishingManager fishingManager, uint zoneIndex) : TaskBase {
    public uint ZoneIndex { get; } = zoneIndex;
    private static readonly Random Rng = new();

    internal static readonly FishingSpotRegion[] ValidFishingRegions = [
        new("Left A", 7f, 7.25f, 6.711f, -12f, -4f),
        new("Left B", 7f, 7.25f, 6.711f, -2f, 3f),
        new("Right", -7.25f, -7f, 6.711f, -12f, 3.5f),
    ];

    protected override async Task Execute() {
        Service.PrintDebug($"[AutoOceanFish] Task execute zone={ZoneIndex + 1}, walkToRailing={ZoneIndex == 0}");

        if (ZoneIndex == 0) {
            Status = "Walking to railing";
            Service.PrintDebug("[AutoOceanFish] Walking to railing");
            await WalkToRailing();
        }

        Status = "Starting fishing";
        var ws = Service.WorldState;
        await WaitUntil(() => (Svc.Objects.LocalPlayer?.IsTargetable ?? false) && ws.IsCastAvailable(), nameof(Execute), checkFrequency: 5);
        Service.PrintDebug("[AutoOceanFish] Calling StartFishing");
        fishingManager.StartFishing();
        Service.PrintDebug("[AutoOceanFish] StartFishing returned");
    }

    // https://github.com/Knightmore/Henchman/blob/4aa8cf33b6164536acca81afefa0df5da6740e89/Henchman/Features/OnABoat/OnABoat.cs#L120
    internal static Vector3 GetFishingPosition() {
        if (Rng.Next(2) == 0)
            return ValidFishingRegions[Rng.Next(2)].Sample(Rng);
        return ValidFishingRegions[2].Sample(Rng);
    }

    private async Task WalkToRailing() {
        var position = GetFishingPosition();
        var rotation = position.X > 0 ? 1.5f : -1.5f;
        await MoveToDirectly(position, () => Player.Object.WithinRange(position, 1) && Service.WorldState.IsCastAvailable());
        await NextFrame(500);
        unsafe {
            Svc.Objects.LocalPlayer?.Character->SetRotation(rotation);
        }
    }
}

internal readonly record struct FishingSpotRegion(string Name, float MinX, float MaxX, float Y, float MinZ, float MaxZ) {
    public Vector3 Sample(Random rng) => new(MinX + rng.NextSingle() * (MaxX - MinX), Y, MinZ + rng.NextSingle() * (MaxZ - MinZ));

    public bool ContainsXZ(Vector3 pos, float margin = 0f)
        => pos.X >= MinX - margin && pos.X <= MaxX + margin && pos.Z >= MinZ - margin && pos.Z <= MaxZ + margin;

    public Vector3 Centroid => new((MinX + MaxX) * 0.5f, Y, (MinZ + MaxZ) * 0.5f);

    public Vector3[] Corners => [
        new(MinX, Y, MinZ),
        new(MaxX, Y, MinZ),
        new(MaxX, Y, MaxZ),
        new(MinX, Y, MaxZ),
    ];
}
