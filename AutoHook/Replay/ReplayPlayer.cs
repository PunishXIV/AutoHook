namespace AutoHook.Replay;

public sealed class ReplayPlayer(FishingReplay replay) {
    public FishingReplay Replay { get; } = replay;
    public WorldState WorldState { get; private set; } = CreateWorldState(replay);

    private static WorldState CreateWorldState(FishingReplay replay)
        => new(replay.QPF, replay.GameVersion);

    public void Reset() {
        WorldState = CreateWorldState(Replay);
        NextOpIndex = 0;
    }

    public bool TickForward() {
        if (NextOpIndex >= Replay.Ops.Count)
            return false;

        var ts = Replay.Ops[NextOpIndex].Timestamp;
        while (NextOpIndex < Replay.Ops.Count && Replay.Ops[NextOpIndex].Timestamp == ts)
            WorldState.Execute(Replay.Ops[NextOpIndex++]);
        return true;
    }

    public void AdvanceTo(DateTime timestamp, Action? update = null) {
        while (NextOpIndex < Replay.Ops.Count && Replay.Ops[NextOpIndex].Timestamp <= timestamp) {
            TickForward();
            update?.Invoke();
        }
    }

    public DateTime CurrTimestamp()
        => NextOpIndex > 0 ? Replay.Ops[NextOpIndex - 1].Timestamp : default;

    public int NextOpIndex { get; private set; }

    public void SeekTo(DateTime timestamp) {
        if (Replay.Ops.Count == 0)
            return;

        if (timestamp < CurrTimestamp())
            Reset();

        AdvanceTo(timestamp);
    }

    public DateTime PrevTimestamp() {
        var curr = CurrTimestamp();
        for (var i = NextOpIndex - 1; i >= 0; i--) {
            if (Replay.Ops[i].Timestamp < curr)
                return Replay.Ops[i].Timestamp;
        }
        return default;
    }
}
