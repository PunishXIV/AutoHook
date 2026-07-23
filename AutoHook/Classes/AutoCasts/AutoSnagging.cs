using FFXIVClientStructs.FFXIV.Client.Game;
using System.ComponentModel;

namespace AutoHook.Classes.AutoCasts;

public sealed class AutoSnagging : BaseActionCast {
    [DefaultValue(3)]
    public override int Priority { get; set; } = 3;
    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;

    public AutoSnagging() : base(IDs.Actions.Snagging, ActionType.Action) { }

    public override string GetName() => UIStrings.Snagging;

    public override string GetHelpText() => UIStrings.SnaggingHelpText;

    public override bool CastCondition() => !Service.WorldState.HasStatus(IDs.Status.Snagging);
}
