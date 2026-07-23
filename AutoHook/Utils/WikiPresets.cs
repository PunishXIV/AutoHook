using ECommons.Throttlers;
using HtmlAgilityPack;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace AutoHook.Utils;

public sealed class WikiFolderExport {
    public required PresetFolder Root { get; init; }
    public required List<PresetFolder> Folders { get; init; }
    public required List<CustomPresetConfig> Presets { get; init; }
}

public static class WikiPresets {
    private const string BaseUrl = "https://github.com/PunishXIV/AutoHook/wiki";
    private const string RawWiki = "https://raw.githubusercontent.com/wiki/PunishXIV/AutoHook";
    private static readonly HttpClient httpClient = new(); // Reuse HttpClient
    private static readonly Lazy<Regex> PresetBlockRegex = new(BuildPresetBlockRegex);

    public static Dictionary<string, List<(WikiFolderExport? Folder, List<CustomPresetConfig> Presets)>> Presets = [];
    public static Dictionary<string, List<AutoGigConfig>> PresetsSf = [];

    public static async Task ListWikiPages() {
        if (!EzThrottler.Throttle("WikiUpdate", 20000))
            return;

        try {
            var newPresets = new Dictionary<string, List<(WikiFolderExport? Folder, List<CustomPresetConfig> Presets)>>();
            var newPresetsSf = new Dictionary<string, List<AutoGigConfig>>();
            var mdUrls = await GetWikiPageUrls(BaseUrl);

            foreach (var mdUrl in mdUrls) {
                try {
                    var base64 = await ExtractBase64FromWikiPage($"{RawWiki}/{mdUrl}.md");

                    static (WikiFolderExport? Folder, List<CustomPresetConfig> Presets) selector(string x) {
                        if (x.StartsWith(Configuration.ExportPrefixFolder)) {
                            var imported = Configuration.ImportFolder(x) ?? throw new Exception("Failed to import"); // Kill wiki shouldn't have broken presets
                            return (new WikiFolderExport {
                                Root = imported.Folder,
                                Folders = imported.Folders,
                                Presets = imported.Presets
                            }, imported.Presets);
                        }
                        var presets = Configuration.ImportPreset(x) ?? throw new Exception("Failed to import");

                        return (null, [(CustomPresetConfig)presets]);
                    }

                    var list = base64.presets.Select(selector).ToList();
                    var listsf = base64.presetsSf.Select(Configuration.ImportPreset).OfType<AutoGigConfig>().ToList();
                    var key = mdUrl.Replace(@"-", @" ");
                    newPresets.Add(key, list);
                    newPresetsSf.Add(key, listsf);
                }
                catch (Exception e) {
                    Svc.Log.Debug($"Can probably ignore: {e.Message}");
                }
            }

            Presets = newPresets;
            PresetsSf = newPresetsSf;
        }
        catch (Exception e) {
            Svc.Log.Error(e, "Failed to fetch wiki presets.");
        }
    }

    static async Task<List<string>> GetWikiPageUrls(string url) {
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

    static async Task<(List<string> presets, List<string> presetsSf)> ExtractBase64FromWikiPage(string url) {
        var wikiPageContent = await httpClient.GetStringAsync(url);
        var blocks = PresetBlockRegex.Value.Matches(wikiPageContent).Select(match => match.Groups[1].Value.Trim()).ToList();
        var presets = blocks.Where(b => !b.StartsWith(Configuration.ExportPrefixSf)).ToList();
        var presetsSf = blocks.Where(b => b.StartsWith(Configuration.ExportPrefixSf)).ToList();

        return (presets, presetsSf);
    }

    static Regex BuildPresetBlockRegex() {
        var prefixPattern = string.Join("|", Configuration.OldExportPrefixes.OrderByDescending(p => p.Length).Select(Regex.Escape));
        return new Regex($@"```\s*((?:{prefixPattern})[\s\S]*?)\s*```", RegexOptions.Multiline | RegexOptions.Compiled);
    }
}
