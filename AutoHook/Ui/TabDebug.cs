using Dalamud.Bindings.ImGui;
using Dalamud.Hooking;
using Dalamud.Interface.Utility.Raii;
using ECommons.Automation.NeoTaskManager;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using HtmlAgilityPack;
using Lumina.Excel.Sheets;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace AutoHook.Ui;

public class TabDebug : BaseTab
{
    public override OpenWindow Type => OpenWindow.Debug;

    private delegate byte ExecuteCommandDelegate(int id, int unk1, uint baitId, int unk2, int unk3);

    private Hook<ExecuteCommandDelegate>? _executeCommandHook;
    public TabDebug()
    {
        //_taskManager.DefaultConfiguration.OnTaskTimeout += RepairFailed;
        //CreateDalamudHooks();
        //taskManager.DefaultConfiguration.OnTaskCompletion
    }

    private unsafe void CreateDalamudHooks()
    {
        _executeCommandHook = Svc.Hook.HookFromSignature<ExecuteCommandDelegate>(
            SignaturePatterns.ExecuteCommand,
            ExecuteCommandDetour);
        _executeCommandHook?.Enable();
    }

    private unsafe byte ExecuteCommandDetour(int id, int unk1, uint baitId, int unk2, int unk3)
    {
        Svc.Log.Debug($"ExecuteCommandDetour: {id} {unk1} {baitId} {unk2} {unk3}");
        return _executeCommandHook!.Original(id, unk1, baitId, unk2, unk3);
    }

    private readonly TaskManager _taskManager = new()
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

    private unsafe uint FishCaught => PlayerState.Instance()->NumFishCaught;

    public override unsafe void Draw()
    {
        try
        {
            if (ImGui.Selectable($"Revert Plugin Version: {Service.Configuration.Version}"))
                Service.Configuration.Version = 4;

            if (Player.Available)
            {
                ImGui.Text($"Fish Caught: {FishCaught}");
                ImGui.Text($"Current Bait: {Service.BaitManager.Current}");
                ImGui.Text($"Current Swimbait: {Service.BaitManager.CurrentSwimBait}");
                ImGui.Text($"Current BaitSwimbait: {Service.BaitManager.CurrentBaitSwimBait}");
                ImGui.Text($"Is Mooching (Swimbait): {Service.BaitManager.IsMooching()}");
                ImGui.Text($"Last Catch: {Service.LastCatch?.Name ?? "None"} (ID: {Service.LastCatch?.Id ?? -1})");
                ImGui.Text($"Current Swimbait: {string.Join(", ", Service.BaitManager.SwimbaitIds.ToArray())}");
            }

            if (ImGui.Selectable($" {Service.Configuration.HookPresets.Folders.Count} Folders"))
            {
                Service.Configuration.HookPresets.Folders.Clear();
                Service.Configuration.Save();
            }

            if (ImGui.CollapsingHeader("Testing buttons (scary)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ImGui.Button("Try repair"))
                {
                    repairStauts = RepairStatus.Repairing;
                    _taskManager.Enqueue(ProcessRepair, "Repair");
                }

                if (ImGui.Button("Scan Offsets"))
                {
                    Checkoffsets();
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

            ImGui.InputInt("Swimbait Id", ref _swimbaitId);

            if (ImGui.Button("Swap Swimbait"))
            {
                Service.BaitManager.ChangeBait((uint)_swimbaitId);
            }

            if (ImGui.Button($"copy mooches"))
                ImGui.SetClipboardText(string.Join("\n", FindRows<FishingBaitParameter>(x => x.Item.Value.ItemUICategory.RowId != 33).Select(x => $"[{x.Item.RowId}] {x.Item.Value.Singular}")));

            if (ImGui.CollapsingHeader("Get Wiki presets", ImGuiTreeNodeFlags.DefaultOpen))
            {
                using (ImRaii.Group())
                {
                    if (ImGui.Button($"Get Wiki info (cd: {EzThrottler.GetRemainingTime("WikiUpdate")})"))
                    {
                        _ = WikiPresets.ListWikiPages();
                    }

                    //ImGui.InputTextWithHint("", "regex", ref regex, 500);

                    foreach (var preset in WikiPresets.Presets)
                    {
                        ImGui.TextWrapped($"Preset: {preset.Key}, Qtd: {preset.Value.Count}");
                        foreach (var item in preset.Value)
                            ImGui.TextWrapped($"-> {item.Presets.FirstOrDefault()?.PresetName ?? "No preset name"}");
                        DrawUtil.SpacingSeparator();
                    }
                }
            }
        }
        catch (Exception e)
        {
            Svc.Log.Error(e.Message);
        }
    }

    //private static string regexold = @"```\s*AH\s*([\s\S]*?)\s*```";
    private static readonly string regex = @"```\s*(AH\s*[\s\S]*?)\s*```";

    private static bool ProcessRepair()
    {/*
        var s = RepairManager.ProcessRepair();

        if (s)
            repairStauts = RepairStatus.Success;*/

        return false;
    }

    private void RepairFailed(TaskManagerTask task, ref long ms)
    {
        repairStauts = RepairStatus.Failed;
    }

    private static readonly HttpClient client = new();

    private static readonly Dictionary<string, List<string>> Presets = [];
    private int _swimbaitId = 45949;

    //public static async Task UpdateWiki()
    //{
    //    if (!EzThrottler.Throttle("WikiUpdate", 10000))
    //    {
    //    }

    //    // Example usage:
    //    string wikiPageUrl = "https://raw.githubusercontent.com/wiki/PunishXIV/AutoHook/Scrip-Farming-%5BUpdated-to-DT%5D.md"; // Replace with the actual URL
    //    //presets = await ExtractBase64FromWikiPage(wikiPageUrl);

    //    // Print the extracted base64 codes
    //}

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
        return [.. Regex.Matches(wikiPageContent, regex).Select(match => match.Groups[1].Value)];
    }

    public override void Dispose()
    {
        _executeCommandHook?.Dispose();
        _taskManager.Dispose();
    }

    public unsafe void Checkoffsets()
    {
        Svc.Log.Debug($"Initializing WKSManager offset scan");
        var cosmicManager = WKSManager.Instance();
        if (cosmicManager == null)
        {
            Svc.Log.Debug("WKSManager pointer is null.");
            return;
        }
        for (int offset = 1; offset <= 10000; offset++)
        {
            uint value = *(uint*)((byte*)cosmicManager + offset);
            if (value == 45949)
            {
                Svc.Log.Debug($"Match found at offset 0x{offset:X}: {value}");
            }

            // else Svc.Log.Debug($"Offset 0x{offset:X}: {value}");
        }
    }
}
