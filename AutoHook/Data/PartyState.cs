namespace AutoHook.Data;

public sealed class PartyState {
    public IReadOnlyList<ulong> ContentIds { get; set; } = [];
    public IReadOnlyList<ulong> QueuedWithContentIds { get; set; } = [];

    public bool InInstanceContent { get; set; }

    public IEnumerable<WorldState.Operation> CompareToInitial() {
        if (ContentIds.Count > 0)
            yield return new OpMembers(ContentIds);
        if (QueuedWithContentIds.Count > 0)
            yield return new OpQueuedWith(QueuedWithContentIds);
        if (InInstanceContent)
            yield return new OpInInstanceContent(true);
    }

    public sealed record OpMembers(IReadOnlyList<ulong> ContentIds) : WorldState.Operation {
        protected override void Exec(WorldState ws) => ws.Party.ContentIds = ContentIds;

        public override void Write(Replay.ReplayOutput output) {
            output.EmitFourCC("PRTY").Emit(ContentIds.Count);
            foreach (var id in ContentIds)
                output.Emit(id);
        }
    }

    public sealed record OpQueuedWith(IReadOnlyList<ulong> ContentIds) : WorldState.Operation {
        protected override void Exec(WorldState ws) => ws.Party.QueuedWithContentIds = ContentIds;

        public override void Write(Replay.ReplayOutput output) {
            output.EmitFourCC("QWIT").Emit(ContentIds.Count);
            foreach (var id in ContentIds)
                output.Emit(id);
        }
    }

    public sealed record OpInInstanceContent(bool InInstance) : WorldState.Operation {
        protected override void Exec(WorldState ws) => ws.Party.InInstanceContent = InInstance;

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("INST").Emit(InInstance);
    }
}
