using System.Collections.Generic;
using System.Linq;
using AutoHook.Enums;
using AutoHook.Spearfishing.Enums;
using AutoHook.Utils;

namespace AutoHook.Classes;

public class ImportedFish
{
    public int ItemId { get; set; }
    public HookType HookType { get; set; }
    public BiteType BiteType { get; set; }
    public int InitialBait { get; set; }
    public List<int> Mooches { get; set; } = new();
    public List<FishPredator> Predators { get; set; } = new();
    public List<int> Nodes { get; set; } = new();
    public bool IsSpearFish { get; set; } = new();
    public SpearfishSize Size { get; set; } = new();
    public SpearfishSpeed Speed { get; set; } = new();

    public int SurfaceSlap { get; set; } = new();
    public bool OceanFish { get; set; } = new();
    public FishInterval Interval { get; set; } = new();

    public string Name => MultiString.GetItemName(ItemId);
    
    public bool IsLureFish => GameRes.LureFishes.Any(f => f.Id == ItemId);

    public class FishPredator
    {
        public int itemId { get; set; }
        public int qtd { get; set; }
    }

    public class FishInterval
    {
        public int OnTime { get; set; }
        public int OffTime { get; set; }
        public int ShiftTime { get; set; }
    }
}