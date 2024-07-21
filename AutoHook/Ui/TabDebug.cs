using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoHook.Utils;
using ECommons.Automation.NeoTaskManager;
using ECommons.Throttlers;
using ImGuiNET;
using HtmlAgilityPack;
using System.Linq;
using AutoHook.Enums;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace AutoHook.Ui;

public class TabDebug : BaseTab
{
    public override OpenWindow Type => OpenWindow.Debug;

    public TabDebug()
    {
        _taskManager.DefaultConfiguration.OnTaskTimeout += RepairFailed;
        //taskManager.DefaultConfiguration.OnTaskCompletion
    }

    
    private TaskManager _taskManager = new TaskManager()
    {
        DefaultConfiguration = { TimeLimitMS = 10000 }
    };
    
    public override string TabName => "Debug";
    public override bool Enabled => true;

    private static RepairStatus repairStauts = RepairStatus.Idle;

    public override void DrawHeader()
    {
        DrawUtil.TextV($"Theres no debug here its just random stuff i add to see what happens");

        DrawUtil.TextV($"AutoRepair Status: {repairStauts}");
    }

    enum RepairStatus
    {
        Idle,
        Repairing,
        Success,
        Failed
    }

    private unsafe uint fishCaught => PlayerState.Instance()->NumFishCaught;

    public override void Draw()
    {
        try
        {
            if (ImGui.Selectable($"Revert Plugin Version: {Service.Configuration.Version}"))
                Service.Configuration.Version = 4;

            if (Player.Available)
            {
                ImGui.Text($"Fish Caught: {fishCaught}");
            }

            if (ImGui.CollapsingHeader("Testing buttons (scary)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ImGui.Button("Try repair"))
                {
                    repairStauts = RepairStatus.Repairing;
                    _taskManager.Enqueue(ProcessRepair, "Repair");
                }

                if (ImGui.Button("Export fish ids"))
                {
                    var fishList = GameRes.Fishes;

                    string allKeys = $"[{string.Join(", ", fishList.Select(f => f.Id))}]";
                    ImGui.SetClipboardText(allKeys);
                }

                if (ImGui.Button("Fix Global Preset"))
                {
                    Service.Configuration.HookPresets.DefaultPreset.PresetName = Service.GlobalPresetName;
                }
            }


            if (ImGui.CollapsingHeader("Get Wiki presets", ImGuiTreeNodeFlags.DefaultOpen))
            {
                using (ImRaii.Group())
                {
                    if (ImGui.Button($"Get Wiki info (cd: {EzThrottler.GetRemainingTime("WikiUpdate")})"))
                    {
                        WikiPresets.ListWikiPages();
                    }

                    //ImGui.InputTextWithHint("", "regex", ref regex, 500);

                    foreach (var preset in WikiPresets.Presets)
                    {
                        ImGui.TextWrapped($"Preset: {preset.Key}, Qtd: {preset.Value.Count}");
                        foreach (var item in preset.Value)
                            ImGui.TextWrapped($"-> {item.PresetName}");
                        DrawUtil.SpacingSeparator();
                    }
                }
            }
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(e.Message);
        }
    }

    private static string regexold = @"```\s*AH\s*([\s\S]*?)\s*```";
    private static string regex = @"```\s*(AH\s*[\s\S]*?)\s*```";
    

    private static bool ProcessRepair()
    {
        var s = RepairManager.ProcessRepair();

        if (s)
            repairStauts = RepairStatus.Success;

        return s;
    }

    private void RepairFailed(TaskManagerTask task, ref long ms)
    {
        repairStauts = RepairStatus.Failed;
    }

    private static readonly HttpClient client = new HttpClient();

    private static Dictionary<string, List<string>> Presets = new();

    public static async Task UpdateWiki()
    {
        if (!EzThrottler.Throttle("WikiUpdate", 10000))
        {
        }

        // Example usage:
        string wikiPageUrl =
            "https://raw.githubusercontent.com/wiki/PunishXIV/AutoHook/Scrip-Farming-%5BUpdated-to-DT%5D.md"; // Replace with the actual URL
        //presets = await ExtractBase64FromWikiPage(wikiPageUrl);

        // Print the extracted base64 codes
    }

    private const string BaseUrl = "https://github.com/PunishXIV/AutoHook/wiki";
    private const string RawWiki = "https://raw.githubusercontent.com/wiki/PunishXIV/AutoHook";
    private static readonly HttpClient httpClient = new(); // Reuse HttpClient

    public static async Task ListWikiPages()
    {
        var mdUrls = await GetWikiPageUrls(BaseUrl);
        Service.PrintDebug($"Size1: {mdUrls.Count}");

        foreach (var mdUrl in mdUrls)
        {
            var preset = await ExtractBase64FromWikiPage($"{RawWiki}/{mdUrl}.md");
            Presets.Add(mdUrl.Replace(@"-", @" "), preset);
        }
    }

    static async Task<List<string>> GetWikiPageUrls(string url)
    {
        var pageUrls = new List<string>();
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(await httpClient.GetStringAsync(url));

        var pageLinks = htmlDoc.DocumentNode
            ?.SelectSingleNode("//nav[contains(@class, 'wiki-pages-box')]")
            ?.SelectNodes(".//a[@href]")
            ?.Skip(1) // Skip the first link (usually the Home link)
            ?.Select(link => $"{link.Attributes["href"]?.Value?.Replace(@"/PunishXIV/AutoHook/wiki/", "")}");

        if (pageLinks != null)
            pageUrls.AddRange(pageLinks);


        return pageUrls;
    }

    static async Task<List<string>> ExtractBase64FromWikiPage(string url)
    {
        string wikiPageContent = await httpClient.GetStringAsync(url);
        return Regex.Matches(wikiPageContent, TabDebug.regex)
            .Select(match => match.Groups[1].Value)
            .ToList();
    }
}