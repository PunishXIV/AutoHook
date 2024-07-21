using System.Collections.Generic;
using AutoHook.Classes;
using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using AutoHook.SeFunctions;
using Dalamud.Plugin.Services;
using Dalamud;
using AutoHook.Configurations;
using Dalamud.Game.ClientState.Objects;
using ECommons.Automation.NeoTaskManager;

namespace AutoHook;

public class Service
{
    public static void Initialize(IDalamudPluginInterface pluginInterface)
        => pluginInterface.Create<Service>();

    public const string PluginName = "AutoHook";
    
    public const string GlobalPresetName = "Global Preset";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] public static IChatGui Chat { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static ICommandManager Commands { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] public static IPluginLog  PluginLog { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get;private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;
    
    public static BaitManager BaitManager { get; set; } = null!;
    public static Configuration Configuration { get; set; } = null!;
    public static WindowSystem WindowSystem { get; } = new(PluginName);
    public static SeTugType TugType { get; set; } = null!;
    public static ClientLanguage Language { get; set; }

    public static string Status = @"";
    
    public static BaitFishClass LastCatch { get; set; } = new(@"-", -1);

    public static readonly TaskManager TaskManager = new TaskManager()
    {
        DefaultConfiguration = { TimeLimitMS = 5000 }
    };

    
    public static void Save()
    {
        Configuration.Save();
    }

    private const int MaxLogSize = 50;
    public static Queue<string> LogMessages = new();
    public static bool OpenConsole;
    public static void PrintDebug(string msg)
    {
        if (LogMessages.Count >= MaxLogSize)
        {
            LogMessages.Dequeue(); 
        }
       
        LogMessages.Enqueue(msg);
        PluginLog.Debug(msg);
    }
    
    public static void PrintVerbose(string msg)
    {
        if (LogMessages.Count >= MaxLogSize)
        {
            LogMessages.Dequeue(); 
        }
       
        LogMessages.Enqueue(msg);
        PluginLog.Verbose(msg);
    }
    
    public static void PrintChat(string msg)
    {
        PrintDebug(msg);

        if (Configuration.ShowChatLogs)
            Chat.Print(msg);
    }
}
