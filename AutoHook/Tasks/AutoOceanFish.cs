using clib.TaskSystem;
using Dalamud.Game.ClientState.Objects.SubKinds;
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
        using var scope = BeginScope(nameof(AutoOceanFish));
        Service.PrintDebug($"[AutoOceanFish] Task execute zone={ZoneIndex + 1}, walkToRailing={ZoneIndex == 0}");

        if (ZoneIndex == 0) {
            Status = "Walking to railing";
            Service.PrintDebug("[AutoOceanFish] Walking to railing");
            await WalkToRailing();
        }

        Status = "Starting fishing";
        await WaitUntil(() => Service.WorldState.Fishing.CanFish, "WaitForCanFish", checkFrequency: 50);
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
        using var scope = BeginScope(nameof(WalkToRailing));
        var position = GetFishingPosition();
        var rotation = position.X > 0 ? 1.5f : -1.5f;
        await MoveToDirectly(position, 0.25f);
        unsafe {
            Svc.Objects.LocalPlayer?.Character->SetRotation(rotation);
        }
        await AvoidStacking(rotation);
    }

    private const float MinFishingSpotDistance = 0.6f;
    private const float NudgeStepDistance = 1.2f;
    private async Task AvoidStacking(float rotation, int maxAttempts = 3) {
        using var scope = BeginScope(nameof(AvoidStacking));
        for (var attempt = 0; attempt < maxAttempts; attempt++) {
            var blockers = Svc.Objects.OfType<IPlayerCharacter>().Where(x => x.EntityId != Player.Object?.GameObjectId).Where(x => Vector3.Distance(Player.Position, x.Position) < MinFishingSpotDistance).ToList();
            if (blockers.Count == 0) return;

            var centroid = blockers.Aggregate(Vector3.Zero, (sum, x) => sum + x.Position) / blockers.Count;
            var away = Player.Position - centroid;
            if (away.LengthSquared() < 0.01f) away = new Vector3(1, 0, 0);

            var onLeft = Player.Position.X > 0;
            var step = Player.Position + Vector3.Normalize(away) * NudgeStepDistance;
            ClampToValidFishingRegions(ref step, onLeft);

            await MoveToDirectly(step, 0.1f);
            unsafe {
                Svc.Objects.LocalPlayer?.Character->SetRotation(rotation);
            }
        }
    }

    private static void ClampToValidFishingRegions(ref Vector3 step, bool onLeft) {
        var regions = onLeft ? [.. ValidFishingRegions.Where(r => r.MinX > 0)] : ValidFishingRegions.Where(r => r.MaxX < 0).ToArray();
        if (regions.Length == 0)
            return;

        step.X = Math.Clamp(step.X, regions.Min(r => r.MinX), regions.Max(r => r.MaxX));

        var z = step.Z;
        var nearest = regions.MinBy(r => {
            if (z < r.MinZ) return r.MinZ - z;
            if (z > r.MaxZ) return z - r.MaxZ;
            return 0f;
        });
        step.Z = Math.Clamp(z, nearest.MinZ, nearest.MaxZ);
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
