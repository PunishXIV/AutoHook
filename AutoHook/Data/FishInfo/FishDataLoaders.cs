using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace AutoHook.Data.FishInfo;

// spot_pools.json (generate_research_data.py): Teamcraft catches and bite times per spot
public static class SpotPoolLoader {
    public static Dictionary<int, SpotObservations> Load(string path) {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<int, SpotObservations>();
        foreach (var spotProp in doc.RootElement.GetProperty("Spots").EnumerateObject()) {
            var spot = new SpotObservations(int.Parse(spotProp.Name));

            // [bait, fish, aLure, mLure, count]
            foreach (var row in spotProp.Value.GetProperty("Catches").EnumerateArray())
                spot.Catches.Add(new CatchResult(row[0].GetUInt32(), row[1].GetInt32(), row[2].GetByte(), row[3].GetByte(), row[4].GetInt32()));

            // [bait, fish, sec, count, sec, count, ...]; Teamcraft doesn't record chum
            foreach (var row in spotProp.Value.GetProperty("BiteTimes").EnumerateArray()) {
                var histogram = spot.GetOrAddBites(new BiteKey(row[0].GetUInt32(), row[1].GetInt32(), null));
                for (var i = 2; i + 1 < row.GetArrayLength(); i += 2)
                    histogram.Add(row[i].GetInt32(), row[i + 1].GetInt32());
            }

            result[spot.SpotId] = spot;
        }

        return result;
    }
}

// fish_tracker.json (generate_research_data.py): Carbuncle Plushy's data
public static class FishTrackerLoader {
    public static Dictionary<int, CarbunclePlushyFish> Load(string path) {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<int, CarbunclePlushyFish>();
        foreach (var prop in doc.RootElement.GetProperty("Fish").EnumerateObject()) {
            var f = prop.Value;
            var id = int.Parse(prop.Name);
            result[id] = new CarbunclePlushyFish {
                ItemId = id,
                Spot = OptInt(f, "Spot"),
                StartHour = OptDouble(f, "StartHour") ?? 0,
                EndHour = OptDouble(f, "EndHour") ?? 24,
                WeathersFrom = UIntList(f, "WeathersFrom"),
                Weathers = UIntList(f, "Weathers"),
                BestCatchPath = [.. f.GetProperty("BestCatchPath").EnumerateArray().Select(step => (IReadOnlyList<int>)[.. step.EnumerateArray().Select(x => x.GetInt32())])],
                Predators = [.. f.GetProperty("Predators").EnumerateArray().Select(p => (p[0].GetInt32(), p[1].GetInt32()))],
                IntuitionSeconds = OptInt(f, "IntuitionSeconds"),
                Hookset = OptString(f, "Hookset"),
                Tug = OptString(f, "Tug"),
                Lure = OptString(f, "Lure"),
                Snagging = OptBool(f, "Snagging"),
                FishEyes = OptBool(f, "FishEyes") ?? false,
                BigFish = OptBool(f, "BigFish") ?? false,
                Folklore = OptInt(f, "Folklore"),
                Patch = OptDouble(f, "Patch"),
            };
        }

        return result;
    }

    private static bool Has(JsonElement e, string name, out JsonElement value)
        => e.TryGetProperty(name, out value) && value.ValueKind is not JsonValueKind.Null;

    private static int? OptInt(JsonElement e, string name) => Has(e, name, out var v) ? (int)v.GetDouble() : null;
    private static double? OptDouble(JsonElement e, string name) => Has(e, name, out var v) ? v.GetDouble() : null;
    private static string? OptString(JsonElement e, string name) => Has(e, name, out var v) ? v.GetString() : null;
    private static bool? OptBool(JsonElement e, string name) => Has(e, name, out var v) ? v.GetBoolean() : null;
    private static IReadOnlyList<uint> UIntList(JsonElement e, string name)
        => Has(e, name, out var v) ? [.. v.EnumerateArray().Select(x => x.GetUInt32())] : [];
}

// please for the love of god otter do not change the format of this
public static class GatherBuddyRecordReader {
    public const byte SupportedVersion = 1;
    public const int RecordLength = 32;

    private const ushort MinBiteMs = 1000;
    private const ushort MaxBiteMs = 65000;

    [Flags]
    private enum Effects : uint {
        Chum = 0x00000002,
        IdenticalCast = 0x00000010,
        SurfaceSlap = 0x00000020,
        Valid = 0x00008000,
        AmbitiousLure1 = 0x00040000,
        AmbitiousLure2 = 0x00080000,
        ModestLure1 = 0x00100000,
        ModestLure2 = 0x00200000,
        ValidLure = 0x00400000,
    }

    // these change which fish bite, so they'd skew catch rates (bite times are still used)
    private const Effects RateSkewing = Effects.IdenticalCast | Effects.SurfaceSlap;

    public sealed record Result(GatherbuddyRecordStatus Status, Dictionary<int, SpotObservations> Spots, int RecordCount, string? Error = null);

    public static Result Read(string path) {
        if (!File.Exists(path))
            return new Result(GatherbuddyRecordStatus.NotFound, [], 0);

        try {
            // in case GB is saving at the same time
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
            }

            if (bytes.Length == 0 || bytes[0] != SupportedVersion || (bytes.Length - 1) % RecordLength != 0)
                return new Result(GatherbuddyRecordStatus.UnsupportedFormat, [], 0);

            return new Result(GatherbuddyRecordStatus.Loaded, Parse(bytes), (bytes.Length - 1) / RecordLength);
        }
        catch (Exception e) {
            return new Result(GatherbuddyRecordStatus.Error, [], 0, e.Message);
        }
    }

    private static Dictionary<int, SpotObservations> Parse(ReadOnlySpan<byte> bytes) {
        var spots = new Dictionary<int, SpotObservations>();
        var catchCounts = new Dictionary<(int Spot, uint Bait, int Fish, byte A, byte M), int>();

        for (var offset = 1; offset + RecordLength <= bytes.Length; offset += RecordLength) {
            var r = bytes.Slice(offset, RecordLength);
            var bait = BinaryPrimitives.ReadUInt32LittleEndian(r);
            var caught = BinaryPrimitives.ReadUInt32LittleEndian(r[4..]);
            var flags = (Effects)BinaryPrimitives.ReadUInt32LittleEndian(r[16..]);
            var biteMs = BinaryPrimitives.ReadUInt16LittleEndian(r[20..]);
            var spotId = BinaryPrimitives.ReadUInt16LittleEndian(r[28..]);

            if (!flags.HasFlag(Effects.Valid) || spotId == 0 || bait == 0)
                continue;

            var fish = caught == 0 ? CatchResult.MissFishId : (int)caught;

            // records from before GB tracked lures have no lure info; lures didn't exist then either
            byte ambitious = 0, modest = 0;
            if (flags.HasFlag(Effects.ValidLure)) {
                ambitious = Stacks(flags, Effects.AmbitiousLure1, Effects.AmbitiousLure2);
                modest = Stacks(flags, Effects.ModestLure1, Effects.ModestLure2);
            }

            if ((flags & RateSkewing) == 0) {
                var key = ((int)spotId, bait, fish, ambitious, modest);
                catchCounts[key] = catchCounts.GetValueOrDefault(key) + 1;
            }
            else {
                GetSpot(spots, spotId).ExcludedCasts++;
            }

            if (fish != CatchResult.MissFishId && biteMs is >= MinBiteMs and <= MaxBiteMs) {
                var spot = GetSpot(spots, spotId);
                spot.GetOrAddBites(new BiteKey(bait, fish, flags.HasFlag(Effects.Chum))).AddExact(biteMs / 1000.0);
            }
        }

        foreach (var ((spotId, bait, fish, a, m), count) in catchCounts)
            GetSpot(spots, spotId).Catches.Add(new CatchResult(bait, fish, a, m, count));

        return spots;
    }

    // GB stores 1-3 stacks in two bits: 01 = 1, 10 = 2, 11 = 3
    private static byte Stacks(Effects flags, Effects one, Effects two)
        => (byte)((flags.HasFlag(one) ? 1 : 0) + (flags.HasFlag(two) ? 2 : 0));

    private static SpotObservations GetSpot(Dictionary<int, SpotObservations> spots, int spotId) {
        if (!spots.TryGetValue(spotId, out var spot))
            spots[spotId] = spot = new SpotObservations(spotId);
        return spot;
    }
}
