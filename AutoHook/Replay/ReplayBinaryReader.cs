using System.IO;
using System.Threading;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;

namespace AutoHook.Replay;

internal sealed class ReplayBinaryReader(Stream stream, FishingReplay replay, CancellationToken cancel) : IDisposable {
    private readonly BinaryReader _reader = new(stream);
    private DateTime _tsStart;
    private ulong _qpcStart;
    private double _invQpf = 1.0 / TimeSpan.TicksPerSecond;
    private DateTime _lastTimestamp;

    public float Progress { get; private set; }
    public long StreamLength { get; } = stream.CanSeek ? stream.Length : 1;

    public void Dispose() => _reader.Dispose();

    public void ParseAll() {
        while (true) {
            cancel.ThrowIfCancellationRequested();
            if (_reader.BaseStream.CanSeek && StreamLength > 0)
                Progress = (float)_reader.BaseStream.Position / StreamLength;

            uint tag;
            try {
                tag = _reader.ReadUInt32();
            }
            catch (EndOfStreamException) {
                break;
            }

            var op = ParseOp(tag);
            if (op == null)
                continue;

            if (op.Timestamp == default)
                op.Timestamp = _lastTimestamp;
            replay.Ops.Add(op);
        }

        Progress = 1f;
    }

    private WorldState.Operation? ParseOp(uint tag) {
        var id = ReplayLogFormatMagic.FromFourCC(tag);
        return id switch {
            "VER " => ParseVer(),
            "META" => ParseMeta(),
            "PSNP" => ParsePsnp(),
            "FRAM" => ParseFrame(),
            "EORZ" => new WorldState.OpEorzeaTime(ParseTimeOnly()),
            "TRTY" => new WorldState.OpTerritory(_reader.ReadUInt32()),
            "WTHR" => new WorldState.OpWeather(_reader.ReadByte(), _reader.ReadByte(), _reader.ReadByte(), _reader.ReadByte()),
            "ZONE" => new WorldState.OpZone(_reader.ReadByte(), _reader.ReadUInt32()),
            "GP  " => new PlayerInfo.OpGp(_reader.ReadUInt32(), _reader.ReadUInt32()),
            "LVL " => new PlayerInfo.OpLevel(_reader.ReadByte()),
            "STAT" => ParseStatuses(),
            "INVT" => ParseInventory(),
            "INVS" => new PlayerInfo.OpInventoryStats(_reader.ReadInt32(), _reader.ReadInt32()),
            "POT " => new PlayerInfo.OpPotCooldown(_reader.ReadBoolean()),
            "CLCD" => ParseCooldowns(),
            "ACTS" => ParseActionStates(),
            "DTYA" => ParseDutyActions(),
            "BLKC" => new WorldState.OpSetBlockCasting(_reader.ReadBoolean()),
            "FISH" => ParseFishingState(),
            "BITE" => new FishingInfo.OpBiteContext(_reader.ReadDouble(), _reader.ReadBoolean()),
            "INTU" => new FishingInfo.OpIntuition(new IntuitionInfo((IntuitionStatus)_reader.ReadByte(), _reader.ReadSingle())),
            "CTCH" => ParseCatch(),
            "FSTP" => new FishingInfo.OpSetFishingStep((FishingSteps)_reader.ReadUInt32(), _reader.ReadBoolean()),
            "FCLR" => new FishingInfo.OpClearFishingStepFlag((FishingSteps)_reader.ReadUInt32()),
            "ACTU" => new FishingInfo.OpPlayerUsedAction(new UsedAction(_reader.ReadUInt32(), (ActionType)_reader.ReadByte())),
            "LURE" => new FishingInfo.OpSetLureSuccess(_reader.ReadBoolean()),
            "LCBT" => ParseLastLureCastBiteTime(),
            "PFST" => new FishingInfo.OpSetPreviousFishingState((FishingState)_reader.ReadByte()),
            "FCNT" => new FishingInfo.OpAddFishCaught(_reader.ReadUInt32(), _reader.ReadByte()),
            "FCRS" => new FishingInfo.OpResetFishCaught(),
            "FSCC" => new FishingInfo.OpClearSessionCatches(),
            "TUG " => new FishingInfo.OpTugType((FishingHookStrength)_reader.ReadUInt16()),
            "FHND" => ParseFishingHandler(),
            "SWIM" => ParseSwimbait(),
            "CSNP" => new FishingInfo.OpUpdateCastSnapshot((FishingState)_reader.ReadByte()),
            "OCNF" => ParseOcean(),
            "SPTM" => new OceanFishInfo.OpSpectralTimer(new OceanSpectralTimerInfo(_reader.ReadSingle(), _reader.ReadBoolean(), _reader.ReadSingle())),
            "WKST" => ParseWks(),
            "DECN" => ParseDecision(),
            "FBGN" => new WorldState.OpBeganSession(),
            "FEND" => new WorldState.OpEndedSession(),
            "OZON" => new WorldState.OpOceanZoneStarted(_reader.ReadUInt32()),
            "SPCH" => new WorldState.OpSpectralCurrentChanged((SpectralCurrentChange)_reader.ReadByte()),
            "ACHP" => new WorldState.OpAchievementProgress(_reader.ReadUInt32(), _reader.ReadUInt32(), _reader.ReadUInt32()),
            "PRTY" => ParseContentIdList(ids => new PartyState.OpMembers(ids)),
            "QWIT" => ParseContentIdList(ids => new PartyState.OpQueuedWith(ids)),
            "INST" => new PartyState.OpInInstanceContent(_reader.ReadBoolean()),
            _ => null,
        };
    }

    private WorldState.Operation ParseContentIdList(Func<IReadOnlyList<ulong>, WorldState.Operation> factory) {
        var count = _reader.ReadInt32();
        var ids = new ulong[count];
        for (var i = 0; i < count; i++)
            ids[i] = _reader.ReadUInt64();
        return factory(ids);
    }

    private WorldState.Operation? ParseVer() {
        _ = _reader.ReadInt32();
        replay.QPF = _reader.ReadUInt64();
        replay.GameVersion = _reader.ReadString();
        _tsStart = new DateTime(_reader.ReadInt64(), DateTimeKind.Utc);
        _invQpf = replay.QPF > 0 ? 1.0 / replay.QPF : 1.0 / TimeSpan.TicksPerSecond;
        _lastTimestamp = _tsStart;
        return null;
    }

    private WorldState.Operation? ParseMeta() {
        replay.Metadata = new ReplayMetadata {
            PresetName = _reader.ReadString(),
            PluginVersion = _reader.ReadString(),
            TerritoryId = _reader.ReadUInt32(),
            PresetSnapshotJson = replay.Metadata.PresetSnapshotJson,
        };
        return null;
    }

    private WorldState.Operation? ParsePsnp() {
        replay.Metadata.PresetSnapshotJson = _reader.ReadString();
        return null;
    }

    private WorldState.OpFrameStart ParseFrame() {
        var qpc = _reader.ReadUInt64();
        var index = _reader.ReadUInt32();
        var durationRaw = _reader.ReadSingle();
        var duration = _reader.ReadSingle();
        var tickSpeed = _reader.ReadSingle();

        var ts = _qpcStart == 0
            ? _tsStart
            : _tsStart + TimeSpan.FromSeconds((qpc - _qpcStart) * _invQpf);
        if (_qpcStart == 0)
            _qpcStart = qpc;

        _lastTimestamp = ts;
        return new WorldState.OpFrameStart(new FrameState(ts, qpc, index, durationRaw, duration, tickSpeed));
    }

    private TimeOnly ParseTimeOnly() => new(_reader.ReadInt64());

    private PlayerInfo.OpStatuses ParseStatuses() {
        var count = _reader.ReadUInt16();
        var dict = new Dictionary<uint, (float, int)>();
        for (var n = 0; n < count; n++)
            dict[_reader.ReadUInt32()] = (_reader.ReadSingle(), _reader.ReadInt32());
        return new PlayerInfo.OpStatuses(dict);
    }

    private PlayerInfo.OpItemCounts ParseInventory() {
        var count = _reader.ReadUInt16();
        var dict = new Dictionary<uint, int>();
        for (var n = 0; n < count; n++)
            dict[_reader.ReadUInt32()] = _reader.ReadInt32();
        return new PlayerInfo.OpItemCounts(dict);
    }

    private PlayerInfo.OpCooldown ParseCooldowns() {
        var reset = _reader.ReadBoolean();
        var count = _reader.ReadByte();
        var changes = new List<(int, Cooldown)>();
        for (var n = 0; n < count; n++)
            changes.Add((_reader.ReadByte(), new Cooldown(_reader.ReadSingle(), _reader.ReadSingle())));
        return new PlayerInfo.OpCooldown(reset, changes);
    }

    private PlayerInfo.OpActionStates ParseActionStates() {
        var count = _reader.ReadUInt16();
        var statuses = new Dictionary<ulong, uint>();
        var groups = new Dictionary<ulong, int>();
        for (var n = 0; n < count; n++) {
            var key = _reader.ReadUInt64();
            statuses[key] = _reader.ReadUInt32();
            groups[key] = _reader.ReadInt32();
        }
        return new PlayerInfo.OpActionStates(statuses, groups);
    }

    private PlayerInfo.OpDutyActions ParseDutyActions() {
        var active = _reader.ReadBoolean();
        var count = _reader.ReadByte();
        var charges = new Dictionary<uint, ushort>();
        for (var n = 0; n < count; n++)
            charges[_reader.ReadUInt32()] = _reader.ReadUInt16();
        return new PlayerInfo.OpDutyActions(active, charges);
    }

    private FishingInfo.OpFishingState ParseFishingState() {
        var state = (FishingState)_reader.ReadByte();
        var baitId = _reader.ReadUInt32();
        var swimbait = _reader.ReadUInt32();
        var moochId = _reader.ReadUInt32();
        var isMooching = _reader.ReadBoolean();
        uint? sw = swimbait == 0 ? null : swimbait;
        return new FishingInfo.OpFishingState(state, new BaitInfo(baitId, sw, moochId, isMooching));
    }

    private FishingInfo.OpSetLastCatch ParseCatch()
        => new(new CatchInfo(
            _reader.ReadUInt32(), _reader.ReadByte(), _reader.ReadBoolean(), _reader.ReadUInt16(),
            _reader.ReadByte(), _reader.ReadByte(), _reader.ReadByte(),
            _reader.ReadBoolean(), _reader.ReadBoolean()));

    private FishingInfo.OpFishingHandlerState ParseFishingHandler()
        => new(
            new PreviousCatchInfo(_reader.ReadBoolean(), _reader.ReadBoolean(), _reader.ReadBoolean(), _reader.ReadBoolean(), _reader.ReadBoolean()),
            _reader.ReadBoolean(), _reader.ReadBoolean(), (FishingBaitFlags)_reader.ReadUInt32(),
            _reader.ReadSByte(), _reader.ReadInt64(), _reader.ReadInt64());

    private FishingInfo.OpSwimbaitIds ParseSwimbait() {
        var count = _reader.ReadByte();
        var ids = new List<uint>();
        for (var n = 0; n < count; n++)
            ids.Add(_reader.ReadUInt32());
        return new FishingInfo.OpSwimbaitIds(ids);
    }

    private FishingInfo.OpSetLastLureCastBiteTime ParseLastLureCastBiteTime() {
        var has = _reader.ReadBoolean();
        return new FishingInfo.OpSetLastLureCastBiteTime(has ? _reader.ReadDouble() : null);
    }

    private OceanFishInfo.OpOceanFishing ParseOcean() {
        if (_reader.ReadInt32() == 0)
            return new OceanFishInfo.OpOceanFishing(null);

        var state = new OceanFishingState {
            SpectralCurrentActive = _reader.ReadBoolean(),
            CurrentRoute = _reader.ReadUInt32(),
            TimeOfDay = (TimeOfDay)_reader.ReadByte(),
            CurrentZone = _reader.ReadUInt32(),
            CurrentSpotId = _reader.ReadUInt32(),
            CurrentTimeId = _reader.ReadUInt32(),
            TimeLeftInZone = _reader.ReadSingle(),
            ZoneTimeMax = _reader.ReadSingle(),
            Mission1 = new OceanMission(_reader.ReadUInt32(), _reader.ReadUInt16()),
            Mission2 = new OceanMission(_reader.ReadUInt32(), _reader.ReadUInt16()),
            Mission3 = new OceanMission(_reader.ReadUInt32(), _reader.ReadUInt16()),
            Status = (InstanceContentOceanFishing.OceanFishingStatus)_reader.ReadByte(),
        };

        var fish = new List<InstanceContentOceanFishing.FishDataStruct>();
        var fishCount = _reader.ReadUInt16();
        for (var n = 0; n < fishCount; n++) {
            fish.Add(new InstanceContentOceanFishing.FishDataStruct {
                ItemId = _reader.ReadUInt32(),
                FishParamId = _reader.ReadUInt16(),
                NqAmount = _reader.ReadUInt16(),
                HqAmount = _reader.ReadUInt16(),
                TotalPoints = _reader.ReadUInt32(),
            });
        }

        return new OceanFishInfo.OpOceanFishing(new OceanFishingState {
            SpectralCurrentActive = state.SpectralCurrentActive,
            CurrentRoute = state.CurrentRoute,
            TimeOfDay = state.TimeOfDay,
            CurrentZone = state.CurrentZone,
            CurrentSpotId = state.CurrentSpotId,
            CurrentTimeId = state.CurrentTimeId,
            TimeLeftInZone = state.TimeLeftInZone,
            ZoneTimeMax = state.ZoneTimeMax,
            Mission1 = state.Mission1,
            Mission2 = state.Mission2,
            Mission3 = state.Mission3,
            Status = state.Status,
            FishData = fish,
        });
    }

    private WKSInfo.OpState ParseWks()
        => new(
            _reader.ReadUInt16(), _reader.ReadUInt16(), _reader.ReadUInt16(), _reader.ReadUInt16(),
            _reader.ReadUInt32(), (WKSMissionModule.MissionRank)_reader.ReadByte(),
            _reader.ReadUInt16(), _reader.ReadByte());

    private WorldState.OpDecision ParseDecision() {
        var context = _reader.ReadString();
        var preset = _reader.ReadString();
        var action = _reader.ReadString();
        var detail = _reader.ReadString();
        var count = _reader.ReadInt32();
        var results = new List<(string, bool)>();
        for (var n = 0; n < count; n++)
            results.Add((_reader.ReadString(), _reader.ReadBoolean()));
        return new WorldState.OpDecision(context, preset, action, detail, results);
    }
}
