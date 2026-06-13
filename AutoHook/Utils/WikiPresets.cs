using ECommons.Throttlers;
using HtmlAgilityPack;
using System.Net.Http;
using System.Text.RegularExpressions;
using static FFXIVClientStructs.FFXIV.Client.LayoutEngine.LayoutManager;
using static FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkHistory.Delegates;

namespace AutoHook.Utils;

public static partial class WikiPresets
{
    private const string BaseUrl = "https://github.com/PunishXIV/AutoHook/wiki";
    private const string RawWiki = "https://raw.githubusercontent.com/wiki/PunishXIV/AutoHook";
    private static readonly HttpClient httpClient = new(); // Reuse HttpClient

    [GeneratedRegex("```\\s*(AH(?:[1-4]|FOLDER)\\s*[\\s\\S]*?)\\s*```", RegexOptions.Multiline)] public static partial Regex Ah();
    [GeneratedRegex("```\\s*(AHSF1\\s*[\\s\\S]*?)\\s*```", RegexOptions.Multiline)] public static partial Regex Ahsf();

    public static Dictionary<string, List<(PresetFolder? folder, List<CustomPresetConfig> Presets)>> Presets = [];
    public static Dictionary<string, List<AutoGigConfig>> PresetsSf = [];

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

                Func<string, (PresetFolder? Folder, List<CustomPresetConfig> Presets)> selector = x =>
                {
                    if (x.StartsWith(Configuration.ExportPrefixFolder))
                    {
                        return Configuration.ImportFolder(x) ?? throw new Exception("Failed to import"); // Kill wiki shouldn't have broken presets
                    }
                    var presets = Configuration.ImportPreset(x) ?? throw new Exception("Failed to import");

                    return (null, [(CustomPresetConfig)presets]);
                };
                var list = base64.presets.Select(selector).ToList();
                var listsf = base64.presetsSf.Select(Configuration.ImportPreset).OfType<AutoGigConfig>().ToList();



                Presets.Add(mdUrl.Replace(@"-", @" "), list);
                PresetsSf.Add(mdUrl.Replace(@"-", @" "), listsf);
            }
            catch (Exception e)
            {
                Svc.Log.Debug($"Can probably ignore: {e.Message}");
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
        var presets = Ah().Matches(wikiPageContent)
            .Select(match => match.Groups[1].Value)
            .ToList();

        var presetsSf = Ahsf().Matches(wikiPageContent)
            .Select(match => match.Groups[1].Value)
            .ToList();

        return (presets, presetsSf);
    }
}
