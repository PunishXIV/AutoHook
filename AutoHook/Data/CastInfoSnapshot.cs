namespace AutoHook.Data;

// since some values are snapshotted on the moment you cast, whether or not they expire by the time you hook doesn't matter so for eval purposes we should check against a snapshot and not live values
public sealed class CastInfoSnapshot {
    public bool Active { get; private set; }

    public IntuitionStatus IntuitionStatus { get; private set; }
    public uint CurrentModifiedWeatherId { get; private set; }
    public uint CurrentWeatherId { get; private set; }
    public uint PreviousWeatherId { get; private set; }
    public uint NextWeatherId { get; private set; }
    public TimeOnly EorzeaTime { get; private set; }
    public SpectralCurrentStatus SpectralCurrentStatus { get; private set; }

    public void Capture(WorldState ws) {
        EorzeaTime = ws.EorzeaTime;
        IntuitionStatus = ws.Fishing.Intuition.Status;
        CurrentModifiedWeatherId = ws.CurrentModifiedWeatherId;
        CurrentWeatherId = ws.CurrentWeatherId;
        PreviousWeatherId = ws.PreviousWeatherId;
        NextWeatherId = ws.NextWeatherId;
        SpectralCurrentStatus = ws.SpectralCurrentStatus;
        Active = true;
    }

    public void Invalidate() => Active = false;
}
