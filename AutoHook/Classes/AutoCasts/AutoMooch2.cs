using FFXIVClientStructs.FFXIV.Client.Game;
using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoMooch2 : BaseActionCast {
    [DefaultValue(11)]
    public override int Priority { get; set; } = 11;
    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;

    public AutoMooch2() : base(IDs.Actions.Mooch2, ActionType.Action) { }

    public override string GetName() => UIStrings.Mooch_II;

    public override string GetHelpText() => UIStrings.AutoMooch_HelpText;

    public override bool CastCondition() => true;
}
