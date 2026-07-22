using System.ComponentModel;

namespace AutoHook.FishSolver.Models;

public enum TugType {
    Unknown = 0,
    Weak = 36,
    Strong = 37,
    Legendary = 38,
}

public enum HooksetType {
    Unknown = 0,
    Normal = 1,
    Precision = 4179,
    Powerful = 4103,
}

public enum AcquisitionType {
    [Description("Caught directly on bait with no mooch or intuition")]
    StraightCatch,

    [Description("One mooch step from a live catch")]
    SingleMooch,

    [Description("Multiple sequential mooch steps")]
    MoochChain,

    [Description("Mooch that can loop")]
    MoochLoop,

    [Description("Requires Fisher's Intuition from predator catches, then straight catch")]
    IntuitionStraight,

    [Description("Requires Fisher's Intuition, then a mooch (or mooch bait) to land the target")]
    IntuitionMooch,

    [Description("Spearfishing")]
    Spearfishing,
}

public enum StrategyArchetype {
    [Description("Default grind: Surface Slap common junk and Chum until the target bites")]
    SlapAndChum,

    [Description("Slap a slower pool competitor so failed casts resolve faster into the target window")]
    FailFaster,

    [Description("Target has a short bite window; Rest (or lure) early when the timer passes it")]
    ShortBiteReset,

    [Description("Catch and hold a live mooch fish as the window opener")]
    PreMoochOpener,

    [Description("Multi-step mooch chain into the target")]
    MoochChain,

    [Description("Self-sustaining mooch loop")]
    MoochLoop,

    [Description("Intuition with few triggers: farm most ahead, land the last trigger as the window opens")]
    IntuitionZeroTime,

    [Description("Hold intuition progress via collectable dialog (Stethacanthus-class); not fully wired yet")]
    IntuitionCollectableHold,

    [Description("Many intuition triggers: farm N-1, Identical Cast the last so it sits at 0s until window open")]
    IntuitionRebuild,

    [Description("Bank mooch fish as swimbait with Spareful Hand, spend them in the window")]
    SwimbaitBank,

    [Description("Stack Ambitious Lure in a multi-!!! pool instead of Rest spam")]
    LureStack,

    [Description("Use Modest/Ambitious Lure to reroll bites instead of full Rest/recast")]
    LureReroll,

    [Description("Store intuition progress across windows and walk away (stow-safe)")]
    CrossWindowJail,
}

public enum PrepHoldMode {
    [Description("No special hold - fish when the window is up")]
    None,

    [Description("Catch remaining triggers immediately at window open (no pre-hold)")]
    Immediate,

    [Description("Identical Cast pins the last trigger at 0s bite time until released at window open")]
    IdenticalCastZeroTime,

    [Description("Hold on the collectable confirm dialog")]
    CollectableDialogHold, // e.g. sculptor

    [Description("Hold a live mooch fish on the line until the window")]
    MoochHold,

    [Description("Bank swimbait of the mooch fish via Spareful Hand for in-window use")]
    SwimbaitBank,

    [Description("Time the final prep catch so it lands at window open")]
    TimedLastCatch,

    [Description("Jail intuition progress across windows")]
    CrossWindowJail,
}

public enum PrepPhaseKind {
    [Description("Work done before the window (farm, slap, GP regen, hold)")]
    Prep,

    [Description("The last action timed to window open (IC release, mooch, swimbait pick)")]
    WindowSync,

    [Description("Actions during the open weather/time window")]
    Window,
}

public enum PrepActionKind {
    [Description("Catch a fish and apply Surface Slap to it")]
    CatchAndSlap,

    [Description("Catch intuition predator / trigger fish")]
    CatchTriggers,

    [Description("Apply Identical Cast on the last trigger")]
    IdenticalCast,

    [Description("Build Angler's Art stacks")]
    BuildAnglerArt,

    [Description("Wait for GP to regenerate")]
    GpRegen, // and cordials

    [Description("Idle hold while waiting for the window with state already set")]
    HoldWait,

    [Description("Cast the last intuition trigger as the window opens")]
    CastLastTrigger,

    [Description("Answer No on collectable confirm to keep the hold")]
    ConfirmCollectableNo,

    [Description("Mooch the fish currently held on the line")]
    MoochHeldBait,

    [Description("Select / use a stored swimbait")]
    SwimbaitSelect,

    [Description("Apply Chum")]
    Chum,

    [Description("Apply Surface Slap")]
    SurfaceSlap,
}

// fallback is just if you're missing skills
public enum RouteVariant {
    Optimal,
    Fallback,
}

// when histogram is missing
public enum RateTier {
    Unknown,
    Common,
    Uncommon,
    Rare,
    VeryRare,
}

public enum HookActionKind {
    Hook,
    PrecisionHookset,
    PowerfulHookset,
    Rest,
    LetGo,
    AmbitiousLure,
    ModestLure,
}

public enum FisherSkill {
    MoochII,
    PatienceI,
    PatienceII,
    SurfaceSlap,
    IdenticalCast,
    DoubleHook,
    TripleHook,
    PrizeCatch,
    MakeshiftBait,
    ThaliaksFavor,
    SparefulHand,
    AmbitiousLure,
    ModestLure,
    BigGameFishing,
    FishEyes,
    CollectorsGlove,
    Chum,
}

// things preserved when the rod is stowed
public enum StowState {
    IntuitionProgress,
    IntuitionBuff,
    AnglersArt,
    MakeshiftBait,
}

public enum CordialKind {
    WateredCordial,
    WateredCordialHq,
    Cordial,
    CordialHq,
    HiCordial,
}
