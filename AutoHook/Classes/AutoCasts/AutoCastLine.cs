using System.ComponentModel;
using AutoHook.Data;
using AutoHook.Resources.Localization;
using AutoHook.Utils;


namespace AutoHook.Classes.AutoCasts;

public class AutoCastLine : BaseActionCast
{
    public bool OnlyCastWithFishEyes = false;
    
    public bool OnlyCastLarge = false;
    
    [DefaultValue(true)] public bool IgnoreMooch = true;

    public override bool DoesCancelMooch() => !IgnoreMooch;

    public override bool RequiresTimeWindow() => true;


    public AutoCastLine() : base(UIStrings.AutoCastLine_Auto_Cast_Line, Data.IDs.Actions.Cast)
    {
        Enabled = true;
        Priority = 1;
    }

    public override int Priority { get; set; } = 0;

    public override bool IsExcludedPriority { get; set; } = true;

    public override bool CastCondition()
    {
        if (OnlyCastWithFishEyes && !PlayerRes.HasStatus(IDs.Status.FishEyes))
            return false;
        
        if (OnlyCastLarge && !PlayerRes.HasAnyStatus([IDs.Status.AnglersFortune, IDs.Status.PrizeCatch]))
            return false;

        return true;
    }

    public override string GetName()
        => Name = UIStrings.AutoCastLine_Auto_Cast_Line;

    protected override DrawOptionsDelegate DrawOptions => () =>
    {
        DrawUtil.Checkbox(UIStrings.AutoCastOnlyUnderFishEyes, ref OnlyCastWithFishEyes,
            UIStrings.AutoCastOnlyUnderFishEyesHelpText);
        
        DrawUtil.Checkbox(UIStrings.OnlyCastLarge, ref OnlyCastLarge);

        DrawUtil.Checkbox(UIStrings.IgnoreMooch, ref IgnoreMooch,
            UIStrings.IgnoreMoochHelpText);
    };
}