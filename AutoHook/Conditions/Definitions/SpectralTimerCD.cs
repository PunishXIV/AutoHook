namespace AutoHook.Conditions.Definitions;

public sealed class SpectralTimerCD : IntCompareConditionDefinition {
    public override string Name => "Spectral time";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;
    protected override string ValueKey => "sec";
    protected override string ValueLabel => "Seconds";
    protected override Func<int, int>? Clamp => static v => Math.Max(0, v);

    protected override bool? InactiveResult(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var args = GetIntCompareParams(parameters, valueKey: ValueKey);
        return !world.SpectralTimer.IsActive ? args.Invert : null;
    }

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => (int)Math.Floor(world.SpectralTimeRemaining);
}
