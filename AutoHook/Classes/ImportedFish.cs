using AutoHook.Spearfishing.Enums;

namespace AutoHook.Classes;

public class ImportedFish
{
    public int ItemId { get; set; }
    public HookType HookType { get; set; }
    public BiteType BiteType { get; set; }
    public int InitialBait { get; set; }
    public List<int> Mooches { get; set; } = [];
    public List<FishPredator> Predators { get; set; } = [];
    public List<int> Nodes { get; set; } = [];
    public bool IsSpearFish { get; set; } = new();
    public SpearfishSize Size { get; set; } = new();
    public SpearfishSpeed Speed { get; set; } = new();

    public int SurfaceSlap { get; set; } = new();
    public bool OceanFish { get; set; } = new();
    public FishInterval Interval { get; set; } = new();
    public List<int> SpotIds { get; set; } = [];
    public List<int> Weathers { get; set; } = [];
    public List<int> WeathersFrom { get; set; } = [];
    public double? Spawn { get; set; }
    public double? Duration { get; set; }
    public string? Time { get; set; }
    public int? MinGathering { get; set; }
    public bool Snagging { get; set; }
    public int MLure { get; set; }
    public int ALure { get; set; }
    public int? OceanFishingTime { get; set; }
    public string? FruityVideo { get; set; }
    public double BiteTimeMin { get; set; }
    public double BiteTimeMax { get; set; }

    public string Name => MultiString.GetItemName(ItemId);

    public bool IsLureFish => GameRes.LureFishes.Any(f => f.Id == ItemId);

    public class FishPredator
    {
        public int ItemId { get; set; }
        public int Quantity { get; set; }
    }

    public class FishInterval
    {
        public int OnTime { get; set; }
        public int OffTime { get; set; }
        public int ShiftTime { get; set; }
    }
}