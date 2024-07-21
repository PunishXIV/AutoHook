using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutoHook.Classes;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace AutoHook.Utils;

public static class GameRes
{
    public const uint FishingTackleRow = 30;

    public static List<BaitFishClass> Baits { get; private set; } = new();
    public static List<BaitFishClass> Fishes { get; private set; } = new();
    public static List<BaitFishClass> LureFishes => Fishes.Where(f => f.LureMessage != "").ToList();

    public static List<ImportedFish> ImportedFishes { get; private set; } = new();
    
    public static List<BiteTimers> BiteTimers { get; private set; } = new();
    
    public static void Initialize()
    {
        Baits = Service.DataManager.GetExcelSheet<Item>()?
                    .Where(i => i.ItemSearchCategory.Row == FishingTackleRow)
                    .Select(b => new BaitFishClass(b))
                    .ToList()
                ?? new List<BaitFishClass>();

        Fishes = Service.DataManager.GetExcelSheet<FishParameter>()?
                     .Where(f => f.Item != 0 && f.Item < 1000000)
                     .Select(f => new BaitFishClass(f))
                     .GroupBy(f => f.Id)
                     .Select(group => group.First())
                     .ToList()
                 ?? new List<BaitFishClass>();
        
        try
        {
            var fishList = Path.Combine(Service.PluginInterface.AssemblyLocation.DirectoryName!, $"Data\\FishData\\fish_list.json");
            
            if (File.Exists(fishList))
            {
                var json = File.ReadAllText(fishList);
                
                ImportedFishes = JsonSerializer.Deserialize<List<ImportedFish>>(json)!;
            }
            
            var biteTimers = Path.Combine(Service.PluginInterface.AssemblyLocation.DirectoryName!, $"Data\\FishData\\bitetimers.json");
            
            if (File.Exists(biteTimers))
            {
                var json = File.ReadAllText(biteTimers);
                
                BiteTimers = JsonSerializer.Deserialize<List<BiteTimers>>(json)!;
            }
        }
        catch (Exception e)
        {
            ImGui.SetClipboardText(e.Message);
            Service.PluginLog.Error($"{e.Message}");
        }
    }
}