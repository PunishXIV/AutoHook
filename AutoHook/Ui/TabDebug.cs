using AutoHook.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System.Numerics;
using System.Reflection;
using LuminaAction = Lumina.Excel.Sheets.Action;
using StatusSheet = Lumina.Excel.Sheets.Status;

namespace AutoHook.Ui;

public class TabDebug : BaseTab {
    public override OpenWindow Type => OpenWindow.Debug;
    public override string TabName => "Debug";
    public override bool Enabled => true;

    public override void DrawHeader() { }

    public override void Draw() {
        try {
            if (ImGui.CollapsingHeader("WorldState", ImGuiTreeNodeFlags.DefaultOpen))
                DrawWorldState(Service.WorldState);
            if (ImGui.CollapsingHeader("Tools"))
                DrawTools();
        }
        catch (Exception e) {
            Svc.Log.Error(e, "[TabDebug] Draw failed.");
        }
    }

    private static void DrawTools() {
        using (ImRaii.PushIndent()) {
            DrawAutomationTask();
            DrawUtil.SpacingSeparator();
            DrawNotificationMaster();
            DrawUtil.SpacingSeparator();
            DrawWikiPresets();
        }
    }

    internal static void DrawWorldState(WorldState ws, string? headerSuffix = null) {
        if (ws == null)
            return;

        using (ImRaii.PushIndent()) {
            var label = headerSuffix == null ? "Overview" : $"Overview ({headerSuffix})";
            if (ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen)) {
                using (ImRaii.PushIndent()) {
                    DrawKvTable("ws_core", [
                        ("Time", ws.CurrentTime.ToString("O")),
                        ("Eorzea", ws.EorzeaTime.ToString("HH:mm:ss")),
                        ("GP", $"{ws.CurrentGp} / {ws.MaxGp}"),
                        ("Territory", ws.TerritoryId == 0 ? "-" : TerritoryType.GetRow(ws.TerritoryId).PlaceName.Value.Name.ToString()),
                        ("Weather (prev)", ws.PreviousWeatherId == 0 ? "-" : Weather.GetRow(ws.PreviousWeatherId).Name.ToString()),
                        ("Weather (current)", ws.CurrentWeatherId == 0 ? "-" : Weather.GetRow(ws.CurrentWeatherId).Name.ToString()),
                        ("Weather (next)", ws.NextWeatherId == 0 ? "-" : Weather.GetRow(ws.NextWeatherId).Name.ToString()),
                        ("Block casting", ws.BlockCasting.ToString()),
                        ("Fishing state", ws.FishingState.ToString()),
                        ("Prev fishing state", ws.PreviousFishingState.ToString()),
                        ("Fishing step", $"{ws.FishingStep} (0x{(uint)ws.FishingStep:X})"),
                    ]);
                }
            }

            if (ImGui.CollapsingHeader("Fishing")) {
                using (ImRaii.PushIndent()) {
                    var f = ws.Fishing;
                    DrawKvTable("ws_fishing", [
                        ("Bait", f.BaitInfo.BaitId == 0 ? "-" : Item.GetRow(f.BaitInfo.BaitId).Name.ToString()),
                        ("Swimbait", f.BaitInfo.SelectedSwimbaitId is { } sb ? Item.GetRow(sb).Name.ToString() : "-"),
                        ("Mooch", f.BaitInfo.MoochId != 0 ? Item.GetRow(f.BaitInfo.MoochId).Name.ToString() : "-"),
                        ("Mooching", f.BaitInfo.IsMooching.ToString()),
                        ("Bite time", $"{f.BiteInfo.BiteTimeSeconds:F2}s"),
                        ("Tug", f.BiteInfo.TugType.ToString()),
                        ("Chum", ws.ChumActive.ToString()),
                        ("Lure success", ws.LureSuccess.ToString()),
                    ]);

                    var snap = f.CastSnapshot;
                    if (snap.Active) {
                        ImGui.Spacing();
                        ImGui.Text("Cast snapshot");
                        DrawKvTable("ws_cast_snap", [
                            ("Intuition", snap.IntuitionStatus.ToString()),
                            ("Weather (prev / cur / next)",
                                $"{(snap.PreviousWeatherId == 0 ? "-" : Weather.GetRow(snap.PreviousWeatherId).Name.ToString())} / {(snap.CurrentWeatherId == 0 ? "-" : Weather.GetRow(snap.CurrentWeatherId).Name.ToString())} / {(snap.NextWeatherId == 0 ? "-" : Weather.GetRow(snap.NextWeatherId).Name.ToString())}"),
                            ("Modified weather", snap.CurrentModifiedWeatherId == 0 ? "-" : Weather.GetRow(snap.CurrentModifiedWeatherId).Name.ToString()),
                            ("Eorzea", snap.EorzeaTime.ToString("HH:mm")),
                            ("Spectral", snap.SpectralCurrentStatus.ToString()),
                        ]);
                    }

                    if (f.LastUsedAction is { } ua)
                        DrawKvRow("Last action", $"{LuminaAction.GetRow(ua.ActionId).Name} ({ua.ActionType})");
                    if (f.LastLureCastBiteTime is { } lureTime) {
                        var elapsed = f.BiteInfo.BiteTimeSeconds - lureTime;
                        DrawKvRow("Last lure cast", $"{lureTime:F2}s (elapsed {elapsed:F2}s)");
                    }

                    if (f.LastCatch is { } lc)
                        DrawKvRow("Last catch", $"{Item.GetRow(lc.FishId).Name} ×{lc.Amount}");

                    DrawFishCaughtData(f);
                }
            }

            if (ImGui.CollapsingHeader("Intuition & spectral")) {
                using (ImRaii.PushIndent()) {
                    DrawKvTable("ws_intuition", [
                        ("Intuition", $"{ws.Fishing.Intuition.Status} ({ws.Fishing.Intuition.TimeRemaining:F1}s)"),
                        ("Spectral status", ws.SpectralCurrentStatus.ToString()),
                        ("Spectral timer", $"{ws.SpectralTimer.TimeRemaining:F1}s (active={ws.SpectralTimer.IsActive})"),
                        ("Next spectral", $"{ws.SpectralTimer.NextSpectralDuration:F1}s"),
                    ]);
                }
            }

            if (ImGui.CollapsingHeader("Ocean fishing")) {
                using (ImRaii.PushIndent()) {
                    var of = ws.OceanFishing;
                    DrawKvTable("ws_ocean", [
                        ("Spectral active", of.SpectralCurrentActive.ToString()),
                        ("Route / zone", $"{of.CurrentRoute} / {of.CurrentZone}"),
                        ("Time in zone", $"{of.TimeLeftInZone:F1}s"),
                        ("Auto ocean", Service.Configuration.AutoOceanFish.ToString()),
                        ("Mission 1", $"{of.Mission1.Type} ({of.Mission1.Progress})"),
                        ("Mission 2", $"{of.Mission2.Type} ({of.Mission2.Progress})"),
                        ("Mission 3", $"{of.Mission3.Type} ({of.Mission3.Progress})"),
                        ("Fish data", (of.FishData?.Count ?? 0).ToString()),
                    ]);
                    if (Svc.Automation.CurrentTask is AutoOceanFish oceanTask)
                        DrawKvRow("Automation", $"zone {oceanTask.ZoneIndex + 1}, {oceanTask.Status}");
                    if (ws.SpectralHistory.Count > 0)
                        DrawSpectralHistory(ws);
                }
            }

            if (ImGui.CollapsingHeader("Inventory & actions")) {
                using (ImRaii.PushIndent()) {
                    DrawKvTable("ws_inv", [
                        ("Swimbait count", ws.GetSwimbaitCount().ToString()),
                        ("Swimbait full", ws.IsSwimbaitFull().ToString()),
                        ("Swimbait empty", ws.IsSwimbaitEmpty().ToString()),
                        ("Pot ready", ws.Player.IsPotOffCooldown.ToString()),
                        ("Cast avail", ws.IsCastAvailable().ToString()),
                        ("Mooch avail", ws.IsMoochAvailable().ToString()),
                        ("Multihook avail", ws.HasMultihookAvailable().ToString()),
                    ]);
                    DrawKnownItems(ws);
                    DrawKnownActions(ws);
                }
            }

            if (ImGui.CollapsingHeader("Statuses")) {
                using (ImRaii.PushIndent()) {
                    if (ws.Statuses.Count == 0) {
                        ImGui.TextDisabled("(none)");
                    }
                    else {
                        using var table = ImRaii.Table("ws_statuses", 4,
                            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                            new Vector2(0, 160.Scaled()));
                        if (table) {
                            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 50.Scaled());
                            ImGui.TableSetupColumn("Name");
                            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 60.Scaled());
                            ImGui.TableSetupColumn("Stacks", ImGuiTableColumnFlags.WidthFixed, 50.Scaled());
                            ImGui.TableHeadersRow();
                            foreach (var (id, (time, stacks)) in ws.Statuses.OrderBy(s => s.Key)) {
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                ImGui.Text(id.ToString());
                                ImGui.TableNextColumn();
                                ImGui.Text(StatusSheet.GetRow(id).Name.ToString());
                                ImGui.TableNextColumn();
                                ImGui.Text($"{time:F1}s");
                                ImGui.TableNextColumn();
                                ImGui.Text(stacks.ToString());
                            }
                        }
                    }
                }
            }

            DrawFshActionInfo(ws);
        }
    }

    private static void DrawSpectralHistory(WorldState ws) {
        ImGui.Spacing();
        ImGui.Text("Spectral history");
        using var table = ImRaii.Table("ws_spectral_hist", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);
        if (!table)
            return;
        ImGui.TableSetupColumn("Zone");
        ImGui.TableSetupColumn("Planned");
        ImGui.TableSetupColumn("Carried");
        ImGui.TableSetupColumn("Actual");
        ImGui.TableHeadersRow();
        foreach (var rec in ws.SpectralHistory) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text((rec.ZoneIndex + 1).ToString());
            ImGui.TableNextColumn();
            ImGui.Text($"{rec.PlannedDurationSeconds:F0}s");
            ImGui.TableNextColumn();
            ImGui.Text($"{rec.CarriedExtraSeconds:F0}s");
            ImGui.TableNextColumn();
            var dur = rec.ActualDurationSeconds is { } d ? $"{d:F0}s" : "active";
            ImGui.Text(dur);
        }
    }

    private static void DrawFishCaughtData(FishingInfo f) {
        if (f.FishCaughtCounts.Count > 0) {
            ImGui.Spacing();
            ImGui.Text("Preset fish counters");
            using var table = ImRaii.Table("ws_fish_counts", 2,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, 100.Scaled()));
            if (table) {
                ImGui.TableSetupColumn("Fish");
                ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthFixed, 50.Scaled());
                ImGui.TableHeadersRow();
                foreach (var (fishId, count) in f.FishCaughtCounts.OrderByDescending(p => p.Value)) {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(Item.GetRow(fishId).Name.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(count.ToString());
                }
            }
        }

        if (f.SessionCatches.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.Text("Session catches");
        using var catchTable = ImRaii.Table("ws_session_catches", 3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(0, 120.Scaled()));
        if (!catchTable)
            return;
        ImGui.TableSetupColumn("Fish");
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 40.Scaled());
        ImGui.TableSetupColumn("Details");
        ImGui.TableHeadersRow();
        foreach (var c in f.SessionCatches) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Item.GetRow(c.FishId).Name.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(c.Amount.ToString());
            ImGui.TableNextColumn();
            ImGui.Text($"size={c.Size} lvl={c.Level} ★{c.Stars} moochable={c.IsMoochable}");
        }
    }

    private static void DrawKnownItems(WorldState ws) {
        var rows = KnownItemIds.Select(id => (id, Count: ws.GetItemCount(id))).Where(r => r.Count > 0).ToArray();
        if (rows.Length == 0)
            return;
        ImGui.Spacing();
        ImGui.Text("Items in inventory");
        using var table = ImRaii.Table("ws_items", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);
        if (!table)
            return;
        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50.Scaled());
        ImGui.TableHeadersRow();
        foreach (var (id, count) in rows) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Item.GetRow(ItemUtil.GetBaseId(id).ItemId).Name.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(count.ToString());
        }
    }

    private static void DrawKnownActions(WorldState ws) {
        var available = KnownActionIds
            .Where(a => ws.ActionAvailable(a.Id, a.Type))
            .Select(a => a.Label)
            .ToList();
        if (available.Count == 0)
            return;
        ImGui.Spacing();
        ImGui.Text($"Available actions: {string.Join(", ", available)}");
    }

    private static void DrawKvTable(string id, params (string Key, string Value)[] rows) {
        using var table = ImRaii.Table(id, 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;
        ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed, 160.Scaled());
        ImGui.TableSetupColumn("Value");
        foreach (var (key, value) in rows) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(key);
            ImGui.TableNextColumn();
            ImGui.Text(value);
        }
    }

    private static void DrawKvRow(string key, string value) {
        ImGui.Text(key);
        ImGui.SameLine(180.Scaled());
        ImGui.Text(value);
    }

    private static void DrawFshActionInfo(WorldState ws) {
        if (!ImGui.CollapsingHeader("FSH actions"))
            return;

        using (ImRaii.PushIndent()) {
            ImGui.TextDisabled("Status != 0 blocks use");
            using var table = ImRaii.Table("fsh_action_info", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable, new Vector2(0, 280.Scaled()));
            if (!table)
                return;

            ImGui.TableSetupColumn("Action");
            ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, 45.Scaled());
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 55.Scaled());
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 45.Scaled());
            ImGui.TableSetupColumn("CD", ImGuiTableColumnFlags.WidthFixed, 45.Scaled());
            ImGui.TableSetupColumn("Grp", ImGuiTableColumnFlags.WidthFixed, 35.Scaled());
            ImGui.TableSetupColumn("OnCD", ImGuiTableColumnFlags.WidthFixed, 40.Scaled());
            ImGui.TableSetupColumn("Avail", ImGuiTableColumnFlags.WidthFixed, 40.Scaled());
            ImGui.TableHeadersRow();

            foreach (var (field, id, type) in FshActions) {
                uint status;
                float cd;
                int group;
                bool onCd;
                bool avail;
                try {
                    status = PlayerRes.ActionStatus(id, type);
                    cd = PlayerRes.GetCooldown(id, type);
                    group = PlayerRes.GetRecastGroups(id, type);
                    onCd = PlayerRes.ActionOnCoolDown(id, type);
                    avail = ws.ActionAvailable(id, type);
                }
                catch (Exception e) {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text($"{field} ({id})");
                    ImGui.TableNextColumn();
                    ImGui.TextColored(ImGuiColors.DalamudRed, e.Message);
                    continue;
                }

                var name = LuminaAction.GetRow(id).Name.ToString();
                var label = string.IsNullOrEmpty(name) ? field : $"{name}";

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(label);
                ImGui.TableNextColumn();
                ImGui.Text(LuminaAction.GetRow(id).Name.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(type.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(status == 0 ? ImGuiColors.DalamudGrey : ImGuiColors.ParsedOrange, $"{status}");
                ImGui.TableNextColumn();
                ImGui.Text(cd > 0 ? $"{cd:F1}" : "-");
                ImGui.TableNextColumn();
                ImGui.Text(group >= 0 ? $"{group}" : "-");
                ImGui.TableNextColumn();
                ImGui.Text(onCd ? "yes" : "no");
                ImGui.TableNextColumn();
                ImGui.Text(avail ? "yes" : "no");
            }
        }
    }

    private static ActionType GetFishingActionType(uint _) => ActionType.Action;

    private static readonly (string Field, uint Id, ActionType Type)[] FshActions =
        [.. typeof(IDs.Actions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (Field: f.Name, Id: Convert.ToUInt32(f.GetValue(null) ?? 0u)))
            .Where(x => x.Id != IDs.Actions.None)
            .Select(x => (x.Field, x.Id, GetFishingActionType(x.Id)))
            .OrderBy(x => x.Field)];

    private static readonly (uint Id, ActionType Type, string Label)[] KnownActionIds =
    [
        (IDs.Actions.Cast, ActionType.Action, "Cast"),
        (IDs.Actions.Mooch, ActionType.Action, "Mooch"),
        (IDs.Actions.Mooch2, ActionType.Action, "Mooch2"),
        (IDs.Actions.Hook, ActionType.Action, "Hook"),
        (IDs.Actions.Patience, ActionType.Action, "Patience"),
        (IDs.Actions.Chum, ActionType.Action, "Chum"),
        (IDs.Actions.PrizeCatch, ActionType.Action, "PrizeCatch"),
        (IDs.Actions.MultiHook, ActionType.Action, "MultiHook"),
    ];

    private static readonly uint[] KnownItemIds =
    [
        IDs.Item.Cordial,
        IDs.Item.HQCordial,
        IDs.Item.HiCordial,
        IDs.Item.WateredCordial,
        IDs.Item.HQWateredCordial,
    ];

    private static string _nmToastTitle = "AutoHook test";
    private static string _nmToastText = "Debug notification";
    private static string _nmLastResult = "";

    private static readonly NotificationConfig _nmTestConfig = new() {
        Enabled = true,
        BeepOnSuccess = true,
        DisplayToastNotification = true,
    };

    private static void DrawAutomationTask() {
        ImGui.Text("Aetherial reduction");
        using (ImRaii.PushIndent()) {
            var automation = Svc.Automation;
            DrawKvTable("ws_auto", [
                ("Running", automation.Running.ToString()),
                ("Name", automation.Name ?? ""),
                ("Status", automation.Status ?? ""),
            ]);
            using (ImRaii.Disabled(!automation.Running)) {
                if (ImGui.Button("Stop current task"))
                    automation.Stop();
            }
            if (Svc.Automation.CurrentTask is AetherialReduction reductionTask) {
                ImGui.Spacing();
                reductionTask.DrawDebug();
            }
        }
    }

    private static void DrawNotificationMaster() {
        ImGui.Text("NotificationMaster");
        using (ImRaii.PushIndent()) {
            var hasPlugin = Svc.Interface.IsPluginLoaded("NotificationMaster");
            var ipcReady = Service.NotificationMaster.IsIPCReady();
            DrawKvTable("ws_nm", [
                ("Plugin loaded", hasPlugin.ToString()),
                ("IPC ready", ipcReady.ToString()),
            ]);
            if (!hasPlugin)
                ImGui.TextDisabled("Install NotificationMaster to test IPC calls.");

            ImGui.SetNextItemWidth(320.Scaled());
            ImGui.InputText("Toast title", ref _nmToastTitle, 128);
            ImGui.SetNextItemWidth(320.Scaled());
            ImGui.InputText("Toast text", ref _nmToastText, 260);

            if (ImGui.Button("IsIPCReady"))
                SetNmResult(Service.NotificationMaster.IsIPCReady());
            ImGui.SameLine();
            if (ImGui.Button("Tray notification"))
                SetNmResult(Service.NotificationMaster.DisplayTrayNotification(_nmToastTitle, _nmToastText));
            ImGui.SameLine();
            if (ImGui.Button("Flash taskbar"))
                SetNmResult(Service.NotificationMaster.FlashTaskbarIcon());
            ImGui.SameLine();
            if (ImGui.Button("Bring foreground"))
                SetNmResult(Service.NotificationMaster.TryBringGameForeground());

            ImGui.Spacing();
            DrawUtil.Checkbox("Enabled", ref _nmTestConfig.Enabled);
            DrawUtil.Checkbox("Echo chat", ref _nmTestConfig.EchoChatMessage);
            DrawUtil.Checkbox("In-game toast", ref _nmTestConfig.DisplayGameToast);
            DrawUtil.Checkbox("Tray toast", ref _nmTestConfig.DisplayToastNotification);
            DrawUtil.Checkbox("Flash taskbar", ref _nmTestConfig.FlashTaskbarIcon);
            DrawUtil.Checkbox("Bring foreground", ref _nmTestConfig.BringGameForeground);
            DrawUtil.Checkbox("Beep on success", ref _nmTestConfig.BeepOnSuccess);
            if (ImGui.Button("Notify()"))
                SetNmResult(Service.NotificationMaster.TryNotify(_nmTestConfig, _nmToastText));

            if (!string.IsNullOrEmpty(_nmLastResult))
                ImGui.TextWrapped($"Last result: {_nmLastResult}");
        }
    }

    private static void SetNmResult(bool success) => _nmLastResult = success ? "true" : "false";

    private static void DrawWikiPresets() {
        ImGui.Text("Wiki presets");
        using (ImRaii.PushIndent()) {
            if (ImGui.Button($"Fetch wiki pages (cd: {EzThrottler.GetRemainingTime("WikiUpdate")})"))
                _ = WikiPresets.ListWikiPages();

            foreach (var preset in WikiPresets.Presets) {
                ImGui.TextWrapped($"{preset.Key} ({preset.Value.Count} entries)");
                foreach (var item in preset.Value) {
                    if (item.Folder != null)
                        ImGui.BulletText($"{item.Folder.Root.FolderName} ({item.Folder.Presets.Count} presets, {item.Folder.Folders.Count} folders)");
                    else
                        ImGui.BulletText(item.Presets.FirstOrDefault()?.PresetName ?? "No preset name");
                }
            }
        }
    }

    public override void Dispose() { }
}
