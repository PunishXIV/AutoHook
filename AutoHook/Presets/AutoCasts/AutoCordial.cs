using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.ComponentModel;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoCordial : BaseActionCast {
    private const uint CordialHiRecovery = 400;
    private const uint CordialHqRecovery = 350;
    private const uint CordialRecovery = 300;
    private const uint CordialHqWateredRecovery = 200;
    private const uint CordialWateredRecovery = 150;

    public bool InvertCordialPriority;

    public bool SkipGlobalTimeWindow;

    public ConditionSet? OvercapConditionSet { get; set; }

    public override bool RequiresTimeWindow() => !SkipGlobalTimeWindow;

    [NonSerialized]
    public readonly List<(uint, uint)> _cordialList =
    [
        (IDs.Item.HiCordial,        CordialHiRecovery),
        (IDs.Item.HQCordial,        CordialHqRecovery),
        (IDs.Item.Cordial,          CordialRecovery),
        (IDs.Item.HQWateredCordial, CordialHqWateredRecovery),
        (IDs.Item.WateredCordial,   CordialWateredRecovery)
    ];

    [NonSerialized]
    private readonly List<(uint, uint)> _invertedList =
    [
        (IDs.Item.WateredCordial,   CordialWateredRecovery),
        (IDs.Item.HQWateredCordial, CordialHqWateredRecovery),
        (IDs.Item.Cordial,          CordialRecovery),
        (IDs.Item.HQCordial,        CordialHqRecovery),
        (IDs.Item.HiCordial,        CordialHiRecovery)
    ];

    public AutoCordial(bool isSpearFishing = false) : base(IDs.Item.Cordial, ActionType.Item) {
        IsSpearFishing = isSpearFishing;
    }

    public override string GetName() => UIStrings.Cordial;

    public override bool RestoresGp => true;

    public override bool CastCondition(WorldState ws) {
        if (!EvaluateConditionSet(ws))
            return false;

        var cordialList = _cordialList;

        if (InvertCordialPriority)
            cordialList = _invertedList;

        foreach (var (id, recovery) in cordialList) {
            if (!CheckNotOvercaped(ws, recovery))
                continue;

            // TODO log this in replay and remove
            if (!ws.Player.HaveCordialInInventory(id)) {
                //IPluginLog.Get().Debug($"No cordial (#{id}) in inventory");
                continue;
            }

            Id = id;
            return true;
        }

        return false;
    }

    public override void SetThreshold(int newCost) {
        if (newCost <= 0)
            GpThreshold = 0;
        else
            GpThreshold = newCost;
    }

    private bool CheckNotOvercaped(WorldState ws, uint recovery) {
        if (ConditionSetUtil.EvaluateAllowsOvercap(OvercapConditionSet, ws))
            return true;

        return ws.Player.CurrentGp + recovery <= ws.Player.MaxGp;
    }

    protected override DrawOptionsDelegate DrawOptions => () => {
        DrawUtil.Checkbox(UIStrings.AutoCastCordialPriority, ref InvertCordialPriority);

        if (!IsSpearFishing)
            DrawUtil.Checkbox(UIStrings.CordialOutsideTimeWindow, ref SkipGlobalTimeWindow, UIStrings.CordialOutsideTimeWindowHelpText);

        using (ImRaii.PushId("CastConditions"))
            DrawAutoCastConditions();

        if (!IsSpearFishing) {
            using (ImRaii.PushId("OvercapConditions"))
                OvercapConditionSet = ConditionUi.DrawConditionSet("Overcap conditions", OvercapConditionSet, ConditionScope.AutoCordial, showAdvanced: true);
        }
    };

    [DefaultValue(4)]
    public override int Priority { get; set; } = 4;
    public override bool IsExcludedPriority { get; set; } = false;
}
