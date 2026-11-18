using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHook.Presets.AutoCasts;

public sealed class AutoFood : BaseActionCast //todo
{
    public float SecondsRemaining = 0;

    public AutoFood() : base(0, priority: 7, actionType: ActionType.Item) { }

    public override string GetName() => UIStrings.Food_Buff;

    protected override bool HasOptions => false;

    public override bool CastCondition(WorldState ws) => ws.Player.GetStatusTime(IDs.Status.FoodBuff) <= SecondsRemaining;
}
