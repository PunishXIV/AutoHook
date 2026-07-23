using FFXIVClientStructs.FFXIV.Client.Game;
using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoMooch : BaseActionCast {
    public AutoMooch2 Mooch2 = new();

    public override bool RequiresTimeWindow() => true;

    public AutoMooch() : base(IDs.Actions.Mooch, ActionType.Action) { }

    [NonSerialized] public bool UseAlwaysMoochLabel;
    [NonSerialized] public bool SuppressHelpText;

    public override string GetName() => UseAlwaysMoochLabel ? UIStrings.Always_Mooch : UIStrings.AutoMooch;

    public override string GetHelpText() => SuppressHelpText ? string.Empty : UIStrings.AutoMooch_HelpText;

    public override bool CastCondition() {
        if (!EvaluateConditionSet())
            return false;

        if (Mooch2.IsAvailableToCast()) {
            Service.PrintDebug(@$"Mooch2 Available, casting mooch2");
            Id = IDs.Actions.Mooch2;
            return true;
        }

        if (Service.WorldState.ActionAvailable(IDs.Actions.Mooch)) {
            Service.PrintDebug(@$"Mooch Available, casting normal mooch");
            Id = IDs.Actions.Mooch;
            return true;
        }

        return false;
    }

    protected override DrawOptionsDelegate DrawOptions => () => {
        Mooch2.DrawConfig(null);
        DrawAutoCastConditions();
    };

    [DefaultValue(10)]
    public override int Priority { get; set; } = 10;
    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;
}
