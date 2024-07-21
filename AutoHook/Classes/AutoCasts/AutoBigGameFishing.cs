using System;
using AutoHook.Data;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHook.Classes.AutoCasts;

public class AutoBigGameFishing : BaseActionCast
{
    public int AnglersStacks = 2;
    
    public bool WithIdenticalC = false;
    public bool WithSlap = false;
    
    public AutoBigGameFishing() : base(UIStrings.BigGameFishing, IDs.Actions.BigGameFishing)
    {
        
    }

    public override string GetName()
        => Name = UIStrings.BigGameFishing;

    public override bool CastCondition()
    {
        if (PlayerRes.HasStatus(IDs.Status.BigGameFishing))
            return false;
        
        if ((WithIdenticalC && !PlayerRes.HasStatus(IDs.Status.IdenticalCast)) && (WithSlap &&
            !PlayerRes.HasStatus(IDs.Status.SurfaceSlap)))
            return false;
        
        bool hasStacks = PlayerRes.HasAnglersArtStacks(AnglersStacks);
        
        return hasStacks;
    }

    protected override DrawOptionsDelegate DrawOptions => () =>
    {
        var stack = AnglersStacks;
        if (DrawUtil.EditNumberField(UIStrings.TabAutoCasts_DrawExtraOptionsThaliaksFavor_, ref stack,"", 1))
        {
            AnglersStacks = Math.Max(2, Math.Min(stack, 10));
            Service.Save();
        }
        
        DrawUtil.Checkbox(UIStrings.UseIcActive, ref WithIdenticalC);
        DrawUtil.Checkbox(UIStrings.UseSlapActive, ref WithSlap);
    };

    public override int Priority { get; set; } = 18;
    public override bool IsExcludedPriority { get; set; } = false;
}