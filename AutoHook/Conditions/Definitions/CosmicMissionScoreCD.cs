namespace AutoHook.Conditions.Definitions;

public sealed class CosmicMissionScoreCD : IntCompareConditionDefinition {
    public override string Id => nameof(CosmicMissionScoreCD);
    public override string Name => "Cosmic mission score";
    public override ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;
    protected override string ComboId => "##cosmic_mission_score_op";
    protected override string ValueLabel => "Current Score";
    protected override float ValueWidth => 90f;
    protected override Func<int, int>? Clamp => static v => Math.Max(0, v);

    protected override int ReadValue(WorldState world, IReadOnlyDictionary<string, object> parameters)
        => (int)world.WKS.CurrentScore;
}
