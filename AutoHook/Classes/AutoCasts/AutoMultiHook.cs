using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHook.Classes.AutoCasts;

public class AutoMultiHook : BaseActionCast
{
    /// <summary>Auto Casts: only use when Identical Cast status is active. Fish Caught: only use when Identical Cast is the action being cast.</summary>
    public bool OnlyUseWhenIdenticalCastActive;

    public AutoMultiHook() : base(UIStrings.Multihook, IDs.Actions.MultiHook) { }

    public override int Priority { get; set; } = 0;
    public override bool IsExcludedPriority { get; set; } = true;
    public override unsafe bool CastCondition()
    {
        if (DutyActionManager.GetInstanceIfReady() is not null and var dm)
            for (var i = 0; i < dm->NumValidSlots; i++)
                if (dm->ActionId[i] is IDs.Actions.MultiHook && dm->CurCharges[i] > 0)
                    return !OnlyUseWhenIdenticalCastActive || PlayerRes.HasStatus(IDs.Status.IdenticalCast);
        return false;
    }

    public override string GetName() => Name = UIStrings.Multihook;

    protected override DrawOptionsDelegate DrawOptions => () =>
    {
        if (DrawUtil.Checkbox(UIStrings.OnlyUseWhenIdenticalCastIsActive, ref OnlyUseWhenIdenticalCastActive))
            Service.Save();
    };
}
