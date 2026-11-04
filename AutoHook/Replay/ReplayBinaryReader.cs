using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Client.UI;
using System.IO;
using System.Threading;

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
            "WTHR" => ParseWeather(),
            "ZONE" => new WorldState.OpZone(_reader.ReadByte(), _reader.ReadUInt32()),
            "GP  " => new PlayerState.OpGp(_reader.ReadUInt32(), _reader.ReadUInt32()),
            "LVL " => new PlayerState.OpLevel(_reader.ReadByte()),
            "STAT" => ParseStatuses(),
            "INVT" => ParseInventory(),
            "INVS" => new PlayerState.OpInventoryStats(_reader.ReadInt32(), _reader.ReadInt32()),
            "POT " => new PlayerState.OpPotCooldown(_reader.ReadBoolean()),
            "CLCD" => ParseCooldowns(),
            "ACTS" => ParseActionStates(),
            "DTYA" => ParseDutyActions(),
            "BLKC" => new WorldState.OpSetBlockCasting(_reader.ReadBoolean()),
            "FISH" => ParseFishingState(),
            "BITE" => new RodState.OpBiteContext(_reader.ReadDouble(), _reader.ReadBoolean()),
            "INTU" => new RodState.OpIntuition(new IntuitionInfo((IntuitionStatus)_reader.ReadByte(), _reader.ReadSingle())),
            "CTCH" => ParseCatch(),
            "FSTP" => new RodState.OpSetFishingStep((FishingSteps)_reader.ReadUInt32(), _reader.ReadBoolean()),
            "FCLR" => new RodState.OpClearFishingStepFlag((FishingSteps)_reader.ReadUInt32()),
            "ACTU" => new RodState.OpPlayerUsedAction(new UsedAction(_reader.ReadUInt32(), (ActionType)_reader.ReadByte())),
            "LURE" => new RodState.OpSetLureSuccess(_reader.ReadBoolean()),
            "SLAP" => new RodState.OpSetSlappedFish(_reader.ReadUInt32()),
            "CWIN" => new RodState.OpSetCollectableWindowOpen(_reader.ReadBoolean()),
            "LCBT" => ParseLastLureCastBiteTime(),
            "PFST" => new RodState.OpSetPreviousFishingState((FishingState)_reader.ReadByte()),
            "FCNT" => new RodState.OpAddFishCaught(_reader.ReadUInt32(), _reader.ReadByte()),
            "FCRS" => new RodState.OpResetFishCaught(),
            "FSCC" => new RodState.OpClearSessionCatches(),
            "TUG " => new RodState.OpTugType((FishingHookStrength)_reader.ReadUInt16()),
            "FHND" => ParseFishingHandler(),
            "SWIM" => ParseSwimbait(),
            "CSNP" => new RodState.OpUpdateCastSnapshot((FishingState)_reader.ReadByte()),
            "CSNI" => new RodState.OpInvalidateCastSnapshot(),
            "OCNF" => ParseOcean(),
            "SPTM" => new OceanState.OpSpectralTimer(new OceanSpectralTimerInfo(_reader.ReadSingle(), _reader.ReadBoolean(), _reader.ReadSingle())),
            "WKST" => ParseWks(),
            "SPFN" => new SpearfishingState.OpHud(_reader.ReadBoolean(), _reader.ReadInt32(), _reader.ReadInt32()),
            "SPSA" => new SpearfishingState.OpSessionActive(_reader.ReadBoolean()),
            "SPST" => new SpearfishingState.OpSpot(new SpearfishingSpotState(_reader.ReadUInt32(), _reader.ReadUInt32(), _reader.ReadUInt32(), _reader.ReadBoolean())),
            "SPFL" => ParseSpearfishingFishLanes(),
            "SPLO" => ParseSpearfishingFishLayout(),
            "SPFC" => new SpearfishingState.OpAddFishCaught(_reader.ReadUInt32(), _reader.ReadByte()),
            "SPLC" => new SpearfishingState.OpSetLastCatch(_reader.ReadUInt32(), _reader.ReadByte()),
            "SPRS" => new SpearfishingState.OpResetFishCaught(),
            "SPES" => new SpearfishingState.OpEndSession(),
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
        replay.FormatVersion = _reader.ReadInt32();
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

        var ts = _qpcStart == 0 ? _tsStart : _tsStart + TimeSpan.FromSeconds((qpc - _qpcStart) * _invQpf);
        if (_qpcStart == 0)
            _qpcStart = qpc;

        _lastTimestamp = ts;
        return new WorldState.OpFrameStart(new FrameState(ts, qpc, index, durationRaw, duration, tickSpeed));
    }

    private TimeOnly ParseTimeOnly() => new(_reader.ReadInt64());

    // before v5 only current/previous/next were written, so the modified weather is unknown
    private WorldState.OpWeather ParseWeather()
        => replay.FormatVersion < 5
            ? new(0, _reader.ReadUInt32(), _reader.ReadUInt32(), _reader.ReadUInt32())
            : new(_reader.ReadUInt32(), _reader.ReadUInt32(), _reader.ReadUInt32(), _reader.ReadUInt32());

    private PlayerState.OpStatuses ParseStatuses() {
        var count = _reader.ReadUInt16();
        var dict = new Dictionary<uint, (float, int)>();
        for (var n = 0; n < count; n++)
            dict[_reader.ReadUInt32()] = (_reader.ReadSingle(), _reader.ReadInt32());
        return new PlayerState.OpStatuses(dict);
    }

    private PlayerState.OpItemCounts ParseInventory() {
        var count = _reader.ReadUInt16();
        var dict = new Dictionary<uint, int>();
        for (var n = 0; n < count; n++)
            dict[_reader.ReadUInt32()] = _reader.ReadInt32();
        return new PlayerState.OpItemCounts(dict);
    }

    private PlayerState.OpCooldown ParseCooldowns() {
        var reset = _reader.ReadBoolean();
        var count = _reader.ReadByte();
        var changes = new List<(int, Cooldown)>();
        for (var n = 0; n < count; n++)
            changes.Add((_reader.ReadByte(), new Cooldown(_reader.ReadSingle(), _reader.ReadSingle())));
        return new PlayerState.OpCooldown(reset, changes);
    }

    private PlayerState.OpActionStates ParseActionStates() {
        var count = _reader.ReadUInt16();
        var statuses = new Dictionary<ulong, uint>();
        var groups = new Dictionary<ulong, int>();
        for (var n = 0; n < count; n++) {
            var key = _reader.ReadUInt64();
            statuses[key] = _reader.ReadUInt32();
            groups[key] = _reader.ReadInt32();
        }
        return new PlayerState.OpActionStates(statuses, groups);
    }

    private PlayerState.OpDutyActions ParseDutyActions() {
        var active = _reader.ReadBoolean();
        var count = _reader.ReadByte();
        var charges = new Dictionary<uint, ushort>();
        for (var n = 0; n < count; n++)
            charges[_reader.ReadUInt32()] = _reader.ReadUInt16();
        return new PlayerState.OpDutyActions(active, charges);
    }

    private RodState.OpFishingState ParseFishingState() {
        var state = (FishingState)_reader.ReadByte();
        var baitId = _reader.ReadUInt32();
        var swimbait = _reader.ReadUInt32();
        var moochId = _reader.ReadUInt32();
        var isMooching = _reader.ReadBoolean();
        uint? sw = swimbait == 0 ? null : swimbait;
        return new RodState.OpFishingState(state, new BaitInfo(baitId, sw, moochId, isMooching));
    }

    private RodState.OpSetLastCatch ParseCatch()
        => new(new CatchInfo(
            _reader.ReadUInt32(), _reader.ReadByte(), _reader.ReadBoolean(), _reader.ReadUInt16(),
            _reader.ReadByte(), _reader.ReadByte(), _reader.ReadByte(),
            _reader.ReadBoolean(), _reader.ReadBoolean()));

    private RodState.OpFishingHandlerState ParseFishingHandler()
        => new(
            new PreviousCatchInfo(_reader.ReadBoolean(), _reader.ReadBoolean(), _reader.ReadBoolean(), _reader.ReadBoolean(), _reader.ReadBoolean()),
            _reader.ReadBoolean(), _reader.ReadBoolean(), (FishingBaitFlags)_reader.ReadUInt32(),
            _reader.ReadSByte(), _reader.ReadInt64(), _reader.ReadInt64());

    private RodState.OpSwimbaitIds ParseSwimbait() {
        var count = _reader.ReadByte();
        var ids = new List<uint>();
        for (var n = 0; n < count; n++)
            ids.Add(_reader.ReadUInt32());
        return new RodState.OpSwimbaitIds(ids);
    }

    private SpearfishingState.OpFishLanes ParseSpearfishingFishLanes()
        => new(ParseSpearfishingFishLane(), ParseSpearfishingFishLane(), ParseSpearfishingFishLane());

    private SpearfishingState.OpFishLayout ParseSpearfishingFishLayout() {
        var lane = new SpearLaneLayout(
            _reader.ReadSingle(),
            _reader.ReadSingle(),
            _reader.ReadSingle(),
            _reader.ReadSingle(),
            _reader.ReadSingle(),
            _reader.ReadSingle());
        return new SpearfishingState.OpFishLayout(lane, ParseSpearFishLayout(), ParseSpearFishLayout(), ParseSpearFishLayout());
    }

    private SpearFishLayout ParseSpearFishLayout()
        => new(ParseSpearfishingFishLane(), _reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());

    private AddonSpearFishing.FishInfo ParseSpearfishingFishLane()
        => new() {
            Available = _reader.ReadBoolean(),
            InverseDirection = _reader.ReadBoolean(),
            GuaranteedLarge = _reader.ReadBoolean(),
            Size = (SpearfishSize)_reader.ReadSByte(),
            Speed = _reader.ReadInt16(),
        };

    private RodState.OpSetLastLureCastBiteTime ParseLastLureCastBiteTime() {
        var has = _reader.ReadBoolean();
        return new RodState.OpSetLastLureCastBiteTime(has ? _reader.ReadDouble() : null);
    }

    private OceanState.OpOceanFishing ParseOcean() {
        if (_reader.ReadInt32() == 0)
            return new OceanState.OpOceanFishing(null);

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

        return new OceanState.OpOceanFishing(new OceanFishingState {
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

    private WksState.OpState ParseWks()
        => new(
            _reader.ReadUInt16(), _reader.ReadUInt16(), _reader.ReadUInt16(), _reader.ReadUInt16(),
            _reader.ReadUInt32(), (WKSMissionModule.MissionRank)_reader.ReadByte(),
            _reader.ReadUInt16(), _reader.ReadByte());

    private WorldState.OpDecision ParseDecision()
        => new((DecisionContext)_reader.ReadByte(), _reader.ReadBoolean(), _reader.ReadString(), _reader.ReadString(), _reader.ReadString());
}
