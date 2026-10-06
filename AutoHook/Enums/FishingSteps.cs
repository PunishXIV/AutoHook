namespace AutoHook.Enums;

[Flags]
public enum FishingSteps {
    None = 0,
    StopCasting = 0x01,
    CancelPending = 0x10,
    FishCaught = 0x20,
    BaitSwapped = 0x40,
    PresetSwapped = 0x80,
    TimeOut = 0x200,
    QuitRequested = 0x400,
    StartPending = 0x800,
}
