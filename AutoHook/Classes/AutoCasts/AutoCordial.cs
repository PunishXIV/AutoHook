using System;
using System.Collections.Generic;
using AutoHook.Data;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHook.Classes.AutoCasts;

public class AutoCordial : BaseActionCast
{
    private const uint CordialHiRecovery = 400;
    private const uint CordialHqRecovery = 350;
    private const uint CordialRecovery = 300;
    private const uint CordialHqWateredRecovery = 200;
    private const uint CordialWateredRecovery = 150;

    public bool InvertCordialPriority;
    
    public bool AllowOvercapIC;
    
    public bool IgnoreTimeWindow;
    
    public override bool RequiresTimeWindow() => !IgnoreTimeWindow;

    [NonSerialized]
    public readonly List<(uint, uint)> _cordialList = new()
    {
        (IDs.Item.HiCordial,        CordialHiRecovery),
        (IDs.Item.HQCordial,        CordialHqRecovery),
        (IDs.Item.Cordial,          CordialRecovery),
        (IDs.Item.HQWateredCordial, CordialHqWateredRecovery), 
        (IDs.Item.WateredCordial,   CordialWateredRecovery)
    };

    [NonSerialized]
    private readonly List<(uint, uint)> _invertedList = new()
    {
        (IDs.Item.WateredCordial,   CordialWateredRecovery),
        (IDs.Item.HQWateredCordial, CordialHqWateredRecovery),
        (IDs.Item.Cordial,          CordialRecovery),
        (IDs.Item.HQCordial,        CordialHqRecovery),
        (IDs.Item.HiCordial,        CordialHiRecovery)
    };
    
    

    public AutoCordial(bool isSpearFishing = false) : base(UIStrings.Cordial, IDs.Item.Cordial, ActionType.Item)
    {
        IsSpearFishing = isSpearFishing;
    }
    
    public override string GetName()
        => Name = UIStrings.Cordial;
    public override bool CastCondition()
    {
        var cordialList = _cordialList;
        
        if (InvertCordialPriority)
            cordialList = _invertedList;
        
        foreach (var (id, recovery) in cordialList)
        {
            if (!PlayerRes.HaveCordialInInventory(id))
                continue;
            
            Id = id;
            
            return CheckNotOvercaped(recovery);
        }

        return false;
    }

    public override void SetThreshold(int newCost)
    {
        if (newCost <= 0)
            GpThreshold = 0;
        else
            GpThreshold = newCost;
    }
    
    private bool CheckNotOvercaped(uint recovery)
    {
        if (AllowOvercapIC && PlayerRes.HasStatus(IDs.Status.IdenticalCast))
            return true;
        
        return PlayerRes.GetCurrentGp() + recovery <= PlayerRes.GetMaxGp(); 
    }

    protected override DrawOptionsDelegate DrawOptions => () =>
    {
        if (DrawUtil.Checkbox(UIStrings.AutoCastCordialPriority, ref InvertCordialPriority))
        {
            Service.Save();
        }

        if (!IsSpearFishing)
        {
            if (DrawUtil.Checkbox(UIStrings.Allow_Gp_Overcap, ref AllowOvercapIC))
            {
                Service.Save(); 
            }
        
            if (DrawUtil.Checkbox(UIStrings.CordialOutsideTimeWindow, ref IgnoreTimeWindow, UIStrings.CordialOutsideTimeWindowHelpText))
            {
                Service.Save();
            }
        }
    };

    public override int Priority { get; set; } = 4;
    public override bool IsExcludedPriority { get; set; } = false;
}
