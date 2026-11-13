using System.ComponentModel;

namespace AutoHook.Modules.Ocean;

public enum OceanTripArrival {
    Early, // :00
    Late, // :00..:13
}

public enum OceanTripCleanup {
    None,
    Desynth,
    Sell,
}

[Flags]
public enum OceanTripSafePoint {
    None = 0,
    Inn = 1 << 0,
    Housing = 1 << 1,
    Sanctuary = 1 << 2,
    ArMultiMode = 1 << 3,
    ArPostprocess = 1 << 4,
}

public enum OceanTripHome {
    Home = 1,
    FreeCompany = 2,
    Apartment = 3,
}

public enum OceanTripStepKind {
    ReturnToOrigin,
    GoToInn,
    GoHome,
    ChatCommand,
    StartAutoRetainerMulti = 5,
}

[Serializable]
public sealed class OceanTripStep {
    public OceanTripStepKind Kind = OceanTripStepKind.ReturnToOrigin;
    public string Arg = "";

    public OceanTripHome Home => int.TryParse(Arg, out var h) && Enum.IsDefined((OceanTripHome)h) ? (OceanTripHome)h : OceanTripHome.Home;
}

[Serializable]
public sealed class OceanTripConfig {
    public bool FullAuto = false;
    [DefaultValue(OceanTripSafePoint.Inn | OceanTripSafePoint.Housing)] public OceanTripSafePoint SafePoints = OceanTripSafePoint.Inn | OceanTripSafePoint.Housing;
    [DefaultValue(10)] public int ArLookaheadMinutes = 10; // when to start CharacterPostProcess
    public string ArCharacter = ""; // Name@World

    public bool UseActiveWindow = false;
    [DefaultValue(18 * 60)] public int ActiveFromMinute = 18 * 60;
    [DefaultValue(23 * 60)] public int ActiveToMinute = 23 * 60;

    public bool LeaveEarly = false; // leave after z1/z2 if later stops don't have uncaught fish

    public OceanTripArrival Arrival = OceanTripArrival.Early;
    [DefaultValue(13)] public int LateArrivalMinute = 13;

    [DefaultValue(true)] public bool BuyBait = true;
    [DefaultValue(100)] public int BaitCount = 100;
    [DefaultValue(true)] public bool BuyVersatileLure = true;
    [DefaultValue(5)] public int LureCount = 5;

    public OceanTripCleanup Cleanup = OceanTripCleanup.None;

    public List<OceanTripStep> PostSteps = [new()];
}

public enum OceanAlarmAnchor {
    BoardingOpens,
    BoardingCloses,
}

[Serializable]
public sealed class OceanAlarmReminder {
    public OceanAlarmAnchor Anchor = OceanAlarmAnchor.BoardingOpens;
    public int Minutes = 0;
}
