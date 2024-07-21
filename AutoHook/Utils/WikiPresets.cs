using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoHook.Classes;
using AutoHook.Configurations;
using AutoHook.Ui;
using ECommons.Throttlers;
using HtmlAgilityPack;

namespace AutoHook.Utils;

public class WikiPresets
{
    private const string BaseUrl = "https://github.com/PunishXIV/AutoHook/wiki";
    private const string RawWiki = "https://raw.githubusercontent.com/wiki/PunishXIV/AutoHook";
    private static readonly HttpClient httpClient = new(); // Reuse HttpClient

    private static string regex = @"```\s*(AH\s*[\s\S]*?)\s*```";
    private static string regexSf = @"```\s*(AHSF\s*[\s\S]*?)\s*```";

    public static Dictionary<string, List<CustomPresetConfig>> Presets = new();
    public static Dictionary<string, List<AutoGigConfig>> PresetsSf = new();


    public static async Task ListWikiPages()
    {
        if (!EzThrottler.Throttle("WikiUpdate", 20000))
            return;
        
        Presets.Clear();
        PresetsSf.Clear();
        var mdUrls = await GetWikiPageUrls(BaseUrl);
        foreach (var mdUrl in mdUrls)
        {
            try
            {
                var base64 = await ExtractBase64FromWikiPage($"{RawWiki}/{mdUrl}.md");
            
                var list = base64.presets.Select(Configuration.ImportPreset).OfType<CustomPresetConfig>().ToList();
                var listsf = base64.presetsSf.Select(Configuration.ImportPreset).OfType<AutoGigConfig>().ToList();
            
                Presets.Add(mdUrl.Replace(@"-", @" "), list);
                PresetsSf.Add(mdUrl.Replace(@"-", @" "), listsf);
            }
            catch (Exception e)
            {
                Service.PluginLog.Debug($"Can probably ignore: {e.Message}");
            }
           
        }
    }

    static async Task<List<string>> GetWikiPageUrls(string url)
    {
        var pageUrls = new List<string>();
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(await httpClient.GetStringAsync(url));

        var pageLinks = htmlDoc.DocumentNode
            ?.SelectSingleNode("//nav[contains(@class, 'wiki-pages-box')]")
            ?.SelectNodes(".//a[@href]") // Skip the first link (usually the Home link)
            ?.Select(link => $"{link.Attributes["href"]?.Value?.Replace(@"/PunishXIV/AutoHook/wiki/", "")}");

        if (pageLinks != null)
            pageUrls.AddRange(pageLinks);

        
        return pageUrls;
    }

    static async Task<(List<string> presets, List<string> presetsSf)> ExtractBase64FromWikiPage(string url)
    {
        string wikiPageContent = await httpClient.GetStringAsync(url);
        var presets = Regex.Matches(wikiPageContent, regex) 
            .Select(match => match.Groups[1].Value)
            .ToList();
        
        var presetsSf = Regex.Matches(wikiPageContent, regexSf) 
            .Select(match => match.Groups[1].Value)
            .ToList();

        return (presets, presetsSf);
    }
}