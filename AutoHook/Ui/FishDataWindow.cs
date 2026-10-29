using AutoHook.Data.FishInfo;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoHook.Ui;

public sealed class FishDataWindow : Window, IDisposable {
    private const int LowSampleRows = 50; // rows with fewer casts than this are dimmed
    private const int TimelineWidth = 460;

    private static readonly (string Label, LureFilter Filter)[] LureOptions = [
        ("Exclude lures", LureFilter.NoLure),
        ("Ambitious (any)", new(LureKind.Ambitious)),
        ("Ambitious 1", new(LureKind.Ambitious, 1)),
        ("Ambitious 2", new(LureKind.Ambitious, 2)),
        ("Ambitious 3", new(LureKind.Ambitious, 3)),
        ("Modest (any)", new(LureKind.Modest)),
        ("Modest 1", new(LureKind.Modest, 1)),
        ("Modest 2", new(LureKind.Modest, 2)),
        ("Modest 3", new(LureKind.Modest, 3)),
        ("All casts", LureFilter.All),
    ];

    private int _fishId;
    private int _spotId;
    private FishDataSources _sources = FishDataSources.Teamcraft;
    private int _lureIndex;
    private bool _misses;
    private uint _selectedBait;
    private bool _chum;
    private float _minRatePercent = 1;

    private (int Spot, FishDataSources Sources, int Lure, bool Misses, int Version)? _matrixKey;
    private BaitMatrix? _matrix;

    public FishDataWindow() : base("Fish Data###AutoHookFishData") {
        WindowsService.Get().WindowSystem.AddWindow(this);
        Flags = ImGuiWindowFlags.AlwaysAutoResize;
        RespectCloseHotkey = false;
    }

    public void Dispose() => WindowsService.Get().WindowSystem.RemoveWindow(this);

    public void Open(int fishId) {
        SelectFish(fishId);
        IsOpen = true;
        BringToFront();
    }

    private void SelectFish(int fishId) {
        _fishId = fishId;
        _spotId = 0;
        _selectedBait = 0;
    }

    public override void Draw() {
        var fishData = FishData.Get();
        fishData.EnsureLoaded();
        if (!fishData.IsLoaded) {
            ImGui.TextDisabled("Loading fish data...");
            return;
        }
        if (fishData.LoadError != null)
            ImGui.TextColored(ImGuiColors.DalamudRed, $"Could not load fish data: {fishData.LoadError}");

        try {
            DrawHeader(fishData);
            if (_fishId <= 0) {
                ImGui.TextDisabled("Pick a fish.");
                return;
            }

            ImGui.Spacing();
            DrawIntuitionChain(fishData);
            ImGui.Separator();

            if (_spotId == 0) {
                ImGui.TextDisabled("No fishing spot data for this fish.");
                return;
            }

            var matrix = GetMatrix(fishData);
            using var table = ImRaii.Table("###FishDataPanels", 2, ImGuiTableFlags.BordersInnerV);
            if (!table)
                return;

            ImGui.TableSetupColumn("###matrix", ImGuiTableColumnFlags.WidthFixed, MatrixWidth(matrix));
            ImGui.TableSetupColumn("###bites", ImGuiTableColumnFlags.WidthFixed, Math.Max(TimelineWidth.Scaled(), LegendWidth()));
            ImGui.TableNextColumn();
            DrawMatrix(matrix);
            ImGui.TableNextColumn();
            DrawBiteTimeline(fishData, matrix);
        }
        catch (Exception e) {
            IPluginLog.Get().Error(e, "[FishData] Draw failed");
        }
    }

    private void DrawHeader(FishData fishData) {
        var catalog = FishBaitCatalog.Get();
        DrawUtil.DrawComboSelector(catalog.Fishes, f => f.Label, _fishId > 0 ? ItemName(_fishId) : "Select fish", f => SelectFish(f.Id));

        if (_fishId <= 0)
            return;

        var spots = fishData.GetSpotsForFish(_fishId);
        if (_spotId == 0 || !spots.Contains(_spotId))
            _spotId = fishData.GetTracker(_fishId)?.Spot is { } trackerSpot && spots.Contains(trackerSpot) ? trackerSpot : spots.FirstOrDefault();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(260.Scaled());
        using (var combo = ImRaii.Combo("###FishDataSpot", _spotId == 0 ? "No spots" : SpotName(_spotId))) {
            if (combo) {
                foreach (var spot in spots) {
                    if (ImGui.Selectable($"{SpotName(spot)}###spot{spot}", spot == _spotId)) {
                        _spotId = spot;
                        _selectedBait = 0;
                    }
                }
            }
        }

        ImGui.SameLine();
        var personal = fishData.PersonalStatus == GatherbuddyRecordStatus.Loaded;
        if (!personal)
            _sources = FishDataSources.Teamcraft;
        DrawSourceButton(fishData, "Teamcraft", FishDataSources.Teamcraft, true);
        ImGui.SameLine(0, 2.Scaled());
        DrawSourceButton(fishData, "GB records", FishDataSources.Personal, personal);
        ImGui.SameLine(0, 2.Scaled());
        DrawSourceButton(fishData, "Both", FishDataSources.Both, personal);
    }

    private void DrawSourceButton(FishData fishData, string label, FishDataSources sources, bool enabled) {
        using (ImRaii.Disabled(!enabled))
        using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], _sources == sources)) {
            if (ImGui.Button(label))
                _sources = sources;
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(enabled ? SourceTooltip(fishData, sources) : PersonalUnavailableText(fishData));
    }

    private string SourceTooltip(FishData fishData, FishDataSources sources) {
        // gb casts with ic/ss aren't counted since that changes the pool
        static string Format((long Counted, long Total) casts)
            => casts.Counted == casts.Total ? $"{casts.Total:N0} casts" : $"{casts.Counted:N0}/{casts.Total:N0} casts";

        var text = $"All spots: {Format(fishData.Casts(sources))}";
        if (_spotId != 0)
            text += $"\nThis spot: {Format(fishData.Casts(_spotId, sources))}";
        return text;
    }

    private static string PersonalUnavailableText(FishData fishData) => fishData.PersonalStatus switch {
        GatherbuddyRecordStatus.NotFound => "GatherBuddy fish records not found",
        GatherbuddyRecordStatus.UnsupportedFormat => "GatherBuddy's record format isn't supported",
        GatherbuddyRecordStatus.Error => $"GatherBuddy records unreadable: {fishData.PersonalError}",
        _ => "Loading GatherBuddy records…",
    };

    private sealed record ChainRow(int FishId, int Quantity, string Window, IReadOnlyList<uint> WeathersFrom, IReadOnlyList<uint> Weathers,
        IReadOnlyList<IReadOnlyList<int>> CatchPath, BiteType Tug, HookType Hookset, string Notes);

    private List<ChainRow> BuildChainRows(FishData fishData) {
        var target = fishData.GetTracker(_fishId);
        var fishIds = new List<(int FishId, int Quantity)> { (_fishId, 0) };
        if (target != null)
            fishIds.AddRange(target.Predators);
        else if (FishBaitCatalog.Get()[_fishId] is { } fish)
            fishIds.AddRange(fish.Predators.Select(p => (p.ItemId, p.Quantity)));

        return [.. fishIds.Select(f => {
            var info = fishData.GetTracker(f.FishId);
            var catalogFish = FishBaitCatalog.Get()[f.FishId];
            var window = info != null ? info.StartHour == 0 && info.EndHour == 24 ? "All day" : $"{Hour(info.StartHour)} – {Hour(info.EndHour)}" : catalogFish?.Time ?? "-";
            return new ChainRow(f.FishId, f.Quantity, window,
                info?.WeathersFrom ?? [.. catalogFish?.WeathersFrom.Select(w => (uint)w) ?? []],
                info?.Weathers ?? [.. catalogFish?.Weathers.Select(w => (uint)w) ?? []],
                info is { BestCatchPath.Count: > 0 } ? info.BestCatchPath : CatalogCatchPath(catalogFish),
                TugFor(fishData, f.FishId),
                HooksetFor(fishData, f.FishId),
                Notes(info, f.FishId == _fishId));
        })];
    }

    private void DrawIntuitionChain(FishData fishData) {
        var rows = BuildChainRows(fishData);

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit;
        using var table = ImRaii.Table("###FishDataChain", 6, flags);
        if (!table)
            return;

        ImGui.TableSetupColumn("Fish");
        ImGui.TableSetupColumn("Window");
        ImGui.TableSetupColumn("Weather");
        ImGui.TableSetupColumn("Catch path");
        ImGui.TableSetupColumn("Tug / hook");
        ImGui.TableSetupColumn("Notes");
        ImGui.TableHeadersRow();

        foreach (var row in rows) {
            using var id = ImRaii.PushId(row.FishId);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (row.Quantity > 0) {
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{row.Quantity}×");
                ImGui.SameLine();
            }
            DrawItemIcon(row.FishId);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange, row.FishId == _fishId))
                ImGui.Text(ItemName(row.FishId));
            if (row.FishId != _fishId && ImGui.IsItemClicked())
                SelectFish(row.FishId);
            if (row.FishId != _fishId)
                ImGui.TooltipOnHover("Click to show this fish");

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(row.Window);

            ImGui.TableNextColumn();
            DrawWeathers(row.WeathersFrom, row.Weathers);

            ImGui.TableNextColumn();
            DrawCatchPath(row.CatchPath);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(FishColor(row.Tug, row.Hookset) with { W = 1 }, TugLabel(row.Tug));
            ImGui.SameLine();
            ImGui.Text(HooksetName(row.Hookset));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(row.Notes);
        }
    }

    private static void DrawWeathers(IReadOnlyList<uint> from, IReadOnlyList<uint> to) {
        if (from.Count == 0 && to.Count == 0) {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("Any");
            return;
        }

        foreach (var w in from) {
            DrawWeatherIcon(w);
            ImGui.SameLine(0, 2.Scaled());
        }
        if (from.Count > 0) {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("→");
            ImGui.SameLine(0, 2.Scaled());
        }
        foreach (var w in to) {
            DrawWeatherIcon(w);
            ImGui.SameLine(0, 2.Scaled());
        }
        ImGui.NewLine();
    }

    private static void DrawCatchPath(IReadOnlyList<IReadOnlyList<int>> steps) {

        if (steps.Count == 0) {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("-");
            return;
        }

        for (var i = 0; i < steps.Count; i++) {
            if (i > 0) {
                ImGui.SameLine(0, 2.Scaled());
                ImGui.AlignTextToFramePadding();
                ImGui.Text("→");
                ImGui.SameLine(0, 2.Scaled());
            }
            for (var j = 0; j < steps[i].Count; j++) {
                if (j > 0) {
                    ImGui.SameLine(0, 2.Scaled());
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextDisabled("/");
                    ImGui.SameLine(0, 2.Scaled());
                }
                DrawItemIcon(steps[i][j]);
            }
        }
    }

    // Mooches runs from the fish's direct mooch back to the first one and leaves out the starting bait
    private static List<IReadOnlyList<int>> CatalogCatchPath(Fish? fish) {
        if (fish is not { InitialBait: > 0 })
            return [];
        if (fish.Mooches.Count == 0)
            return [[fish.InitialBait]];

        var steps = new List<IReadOnlyList<int>>();
        if (FishBaitCatalog.Get()[fish.Mooches[^1]] is { InitialBait: > 0 } first)
            steps.Add([first.InitialBait]);
        steps.AddRange(fish.Mooches.Reverse().Select(m => (IReadOnlyList<int>)[m]));
        return steps;
    }

    private static string Notes(CarbunclePlushyFish? info, bool isTarget) {
        if (info == null)
            return "";
        var notes = new List<string>();
        if (info.Lure != null)
            notes.Add($"{info.Lure} Lure");
        if (info.FishEyes)
            notes.Add("Fish Eyes");
        if (info.Snagging == true)
            notes.Add("Snagging");
        if (info.Folklore != null)
            notes.Add("Folklore");
        if (isTarget && info.IntuitionSeconds is { } seconds)
            notes.Add($"Intuition {seconds}s");
        return string.Join(", ", notes);
    }

    // same layout as TC
    private BaitMatrix GetMatrix(FishData fishData) {
        var key = (_spotId, _sources, _lureIndex, _misses, fishData.Version);
        if (_matrix == null || _matrixKey != key) {
            _matrix = fishData.GetMatrix(_spotId, _sources, LureOptions[_lureIndex].Filter, _misses);
            _matrixKey = key;
        }

        // default to the bait the tracker uses for this fish, else the busiest one
        if (!_matrix.Baits.Contains(_selectedBait)) {
            var path = fishData.GetTracker(_fishId)?.BestCatchPath;
            _selectedBait = path is { Count: > 0 } && path[^1].Select(b => (uint)b).FirstOrDefault(_matrix.Baits.Contains) is var pathBait and > 0
                ? pathBait
                : _matrix.Baits.FirstOrDefault();
        }

        return _matrix;
    }

    private void DrawMatrix(BaitMatrix matrix) {
        ImGui.SetNextItemWidth(150.Scaled());
        using (var combo = ImRaii.Combo("###FishDataLure", LureOptions[_lureIndex].Label)) {
            if (combo) {
                for (var i = 0; i < LureOptions.Length; i++) {
                    if (ImGui.Selectable(LureOptions[i].Label, i == _lureIndex))
                        _lureIndex = i;
                }
            }
        }
        ImGui.SameLine();
        ImGui.Checkbox("Misses", ref _misses);
        ImGui.TooltipOnHover("Include casts where nothing was caught");

        if (matrix.IsEmpty) {
            ImGui.TextDisabled("No catches recorded for this filter.");
            return;
        }

        var iconSize = new Vector2(ImGui.GetFrameHeight());
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit;
        using var table = ImRaii.Table("###FishDataMatrixTable", matrix.Fish.Count + 1, flags, new Vector2(MatrixTableWidth(matrix), 0));
        if (!table)
            return;

        ImGui.TableSetupColumn("###bait", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        foreach (var fish in matrix.Fish)
            ImGui.TableSetupColumn($"###fish{fish}", ImGuiTableColumnFlags.WidthFixed, MatrixCellWidth());

        // icon header row
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        ImGui.TableNextColumn();
        for (var column = 0; column < matrix.Fish.Count; column++) {
            ImGui.TableNextColumn();
            var fish = matrix.Fish[column];
            if (fish == _fishId)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiColors.DalamudOrange with { W = 0.35f }));
            DrawItemIcon(fish, iconSize);
        }

        for (var row = 0; row < matrix.Baits.Count; row++) {
            var bait = matrix.Baits[row];
            var total = matrix.RowTotals[row];
            using var id = ImRaii.PushId((int)bait);
            using var dim = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.5f, total < LowSampleRows);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (bait == _selectedBait)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.HeaderActive));
            DrawItemIcon((int)bait, iconSize);
            if (ImGui.IsItemClicked())
                _selectedBait = bait;
            ImGui.TooltipOnHover($"{ItemName((int)bait)}\n{total:N0} casts{(total < LowSampleRows ? " (few samples)" : "")}\nClick to show bite times");

            for (var column = 0; column < matrix.Fish.Count; column++) {
                ImGui.TableNextColumn();
                if (matrix.Rate(row, column) is not { } rate)
                    continue;
                ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiColors.ParsedGreen with { W = 0.1f + 0.6f * (float)rate }));
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{rate * 100:0.#}%");
                ImGui.TooltipOnHover($"{ItemName(matrix.Fish[column])}\n{matrix.Counts[row, column]:N0} of {total:N0} casts");
                if (ImGui.IsItemClicked())
                    _selectedBait = bait;
            }
        }
    }

    private void DrawBiteTimeline(FishData fishData, BaitMatrix matrix) {
        var hasPersonal = _sources.HasFlag(FishDataSources.Personal);
        if (!hasPersonal)
            _chum = false;

        if (_selectedBait != 0) {
            DrawItemIcon((int)_selectedBait);
            ImGui.SameLine();
        }
        ImGui.AlignTextToFramePadding();
        ImGui.Text(_selectedBait == 0 ? "No bait selected" : ItemName((int)_selectedBait));
        ImGui.SameLine();
        using (ImRaii.Disabled(!hasPersonal))
            ImGui.Checkbox("Chum", ref _chum);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(hasPersonal ? "Bite times while Chum is active (from your records)" : "Teamcraft doesn't record Chum. Use GB records or Both.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120.Scaled());
        ImGui.SliderFloat("###FishDataMinRate", ref _minRatePercent, 0, 20, "Hide < %.0f%%");
        ImGui.TooltipOnHover("Hide fish this bait rarely catches");

        var row = matrix.Baits.ToList().IndexOf(_selectedBait);
        if (row < 0)
            return;

        // fish this bait catches (above the threshold), plus the target so it's always visible
        var entries = new List<(int Fish, double? Rate, BiteHistogram Bites)>();
        for (var column = 0; column < matrix.Fish.Count; column++) {
            var fish = matrix.Fish[column];
            var rate = matrix.Rate(row, column);
            if (fish == CatchResult.MissFishId || rate == null || (rate * 100 < _minRatePercent && fish != _fishId))
                continue;
            entries.Add((fish, rate, fishData.GetBites(_spotId, _sources, _selectedBait, fish, _chum)));
        }

        if (entries.All(e => e.Bites.IsEmpty)) {
            ImGui.TextDisabled(_chum ? "No chum bite times recorded for this bait." : "No bite times recorded for this bait.");
            return;
        }

        // ignore single extreme outliers when scaling the axis
        var axisMax = Math.Max(10, entries.Where(e => !e.Bites.IsEmpty).Max(e => e.Bites.Percentile(0.99) + 2));
        var labelWidth = entries.Max(e => ImGui.CalcTextSize(ItemName(e.Fish)).X) + ImGui.GetFrameHeight() + 8.Scaled();

        DrawAxis(labelWidth, axisMax);
        foreach (var (fish, rate, bites) in entries.OrderBy(e => e.Bites.IsEmpty ? double.MaxValue : e.Bites.Percentile(0.5)))
            DrawBiteRow(fish, rate, bites, labelWidth, axisMax);

        ImGui.Spacing();
        DrawColorLegend();
    }

    private static void DrawAxis(float labelWidth, float axisMax) {
        var start = ImGui.GetCursorScreenPos() + new Vector2(labelWidth, 0);
        var width = ImGui.GetContentRegionAvail().X - labelWidth;
        var dl = ImGui.GetWindowDrawList();
        var step = axisMax > 40 ? 10 : 5;
        for (var s = 0; s <= axisMax; s += step) {
            var x = start.X + width * s / axisMax;
            dl.AddText(new Vector2(x - ImGui.CalcTextSize($"{s}").X / 2, start.Y), ImGui.GetColorU32(ImGuiCol.TextDisabled), $"{s}");
        }
        ImGui.Dummy(new Vector2(0, ImGui.GetTextLineHeightWithSpacing()));
    }

    private void DrawBiteRow(int fish, double? rate, BiteHistogram bites, float labelWidth, float axisMax) {
        var tug = TugFor(FishData.Get(), fish);
        var hookset = HooksetFor(FishData.Get(), fish);
        var rowStart = ImGui.GetCursorScreenPos();
        var width = Math.Max(ImGui.GetContentRegionAvail().X - labelWidth, 1);

        DrawItemIcon(fish);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange, fish == _fishId))
            ImGui.Text(ItemName(fish));

        var height = ImGui.GetFrameHeight();
        var barStart = new Vector2(rowStart.X + labelWidth, rowStart.Y);
        ImGui.SetCursorScreenPos(barStart);
        ImGui.Dummy(new Vector2(width, height));

        if (bites.IsEmpty) {
            ImGui.GetWindowDrawList().AddText(barStart + new Vector2(4, (height - ImGui.GetTextLineHeight()) / 2), ImGui.GetColorU32(ImGuiCol.TextDisabled), "no bite data");
            return;
        }

        float X(double seconds) => barStart.X + width * (float)Math.Clamp(seconds / axisMax, 0, 1);
        var dl = ImGui.GetWindowDrawList();
        var color = FishColor(tug, hookset);
        var mid = barStart.Y + height / 2;
        dl.AddLine(new Vector2(X(bites.Min), mid), new Vector2(X(bites.Max), mid), ImGui.GetColorU32(color with { W = color.W * 0.6f }), 2);
        dl.AddRectFilled(new Vector2(X(bites.Percentile(0.05)), barStart.Y + height * 0.2f), new Vector2(X(bites.Percentile(0.95) + 1), barStart.Y + height * 0.8f), ImGui.GetColorU32(color), 2);
        var median = X(bites.Percentile(0.5) + 0.5);
        dl.AddLine(new Vector2(median, barStart.Y + height * 0.1f), new Vector2(median, barStart.Y + height * 0.9f), ImGui.GetColorU32(ImGuiCol.Text), 2);

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip($"{ItemName(fish)} ({TugLabel(tug)}{(hookset == HookType.None ? "" : $", {HooksetName(hookset)}")}){(rate is { } r ? $", {r * 100:0.#}% of catches" : "")}\n" +
                             $"{bites.Total:N0} bites{(_chum ? " with Chum" : "")}\n" +
                             $"Range: {bites.Min:0.0}s – {bites.Max:0.0}s\n" +
                             $"Middle 90%: {bites.Percentile(0.05)}s – {bites.Percentile(0.95) + 1}s\n" +
                             $"Median: {bites.Percentile(0.5)}s");
        }
    }

    private static float MatrixCellWidth() => Math.Max(ImGui.GetFrameHeight(), ImGui.CalcTextSize("100.0%").X);

    // bait icon column + one column per fish, each with cell padding and a border
    private static float MatrixTableWidth(BaitMatrix matrix) {
        var padding = ImGui.GetStyle().CellPadding.X * 2 + 1;
        return ImGui.GetFrameHeight() + padding + matrix.Fish.Count * (MatrixCellWidth() + padding) + 1;
    }

    // the panel also holds the lure filter row, which can be wider than a small matrix
    private static float MatrixWidth(BaitMatrix matrix) {
        var style = ImGui.GetStyle();
        var filters = 150.Scaled() + style.ItemSpacing.X + ImGui.GetFrameHeight() + style.ItemInnerSpacing.X + ImGui.CalcTextSize("Misses").X;
        return Math.Max(MatrixTableWidth(matrix), filters);
    }

    // Carbuncle's curated tug first, then Teamcraft's; used by both the chain and the bite bars so they agree
    private static BiteType TugFor(FishData fishData, int fishId) => fishData.GetTracker(fishId)?.Tug switch {
        "light" => BiteType.Weak,
        "medium" => BiteType.Strong,
        "heavy" => BiteType.Legendary,
        _ => FishBaitCatalog.Get()[fishId]?.BiteType ?? BiteType.Unknown,
    };

    // same source order as TugFor
    private static HookType HooksetFor(FishData fishData, int fishId) => fishData.GetTracker(fishId)?.Hookset switch {
        "Precision" => HookType.Precision,
        "Powerful" => HookType.Powerful,
        _ => FishBaitCatalog.Get()[fishId]?.HookType switch {
            HookType.Precision => HookType.Precision,
            HookType.Powerful => HookType.Powerful,
            _ => HookType.None,
        },
    };

    private static string ItemName(int id) {
        if (id == CatchResult.MissFishId)
            return "Nothing";
        if (FishBaitCatalog.Get()[id] is { Name.Length: > 0 } fish)
            return fish.Name;
        return id > 0 && Item.TryGetRow((uint)id, out var item) ? item.Name.ToString() : $"#{id}";
    }

    private static string SpotName(int spotId)
        => FishingSpot.TryGetRow((uint)spotId, out var spot) && spot.PlaceName.ValueNullable is { } place
            ? $"{place.Name} ({spot.TerritoryType.ValueNullable?.PlaceName.ValueNullable?.Name.ToString() ?? "?"})"
            : $"Spot #{spotId}";

    private static void DrawItemIcon(int id) => DrawItemIcon(id, new Vector2(ImGui.GetFrameHeight()));

    private static void DrawItemIcon(int id, Vector2 size) {
        if (id == CatchResult.MissFishId) {
            using (ImRaii.PushFont(UiBuilder.IconFont)) {
                var icon = FontAwesomeIcon.Times.ToIconString();
                var pad = (size - ImGui.CalcTextSize(icon)) / 2;
                ImGui.SetCursorPos(ImGui.GetCursorPos() + pad with { Y = Math.Max(pad.Y, 0) });
                ImGui.TextDisabled(icon);
            }
            ImGui.TooltipOnHover("Nothing caught");
            return;
        }

        if (id > 0 && Item.TryGetRow((uint)id, out var item))
            DrawUtil.GameIcon(item.Icon, size, item.Name.ToString());
        else
            ImGui.Dummy(size);
    }

    private static void DrawWeatherIcon(uint weatherId) {
        if (Weather.TryGetRow(weatherId, out var weather))
            DrawUtil.GameIcon((uint)weather.Icon, new Vector2(ImGui.GetFrameHeight()), weather.Name.ToString());
    }

    private static string Hour(double hour) => $"{(int)hour % 24:00}:{(int)Math.Round(hour % 1 * 60):00}";

    private static string TugLabel(BiteType tug) => tug switch {
        BiteType.Weak => "!",
        BiteType.Strong => "!!",
        BiteType.Legendary => "!!!",
        _ => "?",
    };

    // ripped from https://github.com/Ottermandias/GatherBuddy/blob/1e39592f55e57774f287bfcea87dbd651a5cf2d8/GatherBuddy/Config/ColorId.cs#L72-L80 so we can be colour besties
    private const uint WeakColor = 0x8000A000;
    private const uint StrongColor = 0x8000A0A0;
    private const uint LegendaryPrecisionColor = 0x8000A080;
    private const uint LegendaryPowerfulColor = 0x800080A0;
    private const uint UnknownColor = 0x80404040;

    private static readonly (string Label, uint Color)[] Legend = [
        ("! Weak", WeakColor),
        ("!! Strong", StrongColor),
        ("!!! Precision", LegendaryPrecisionColor),
        ("!!! Powerful", LegendaryPowerfulColor),
        ("? Unknown", UnknownColor),
    ];

    private static Vector4 FishColor(BiteType tug, HookType hookset) => ImGui.ColorConvertU32ToFloat4(tug switch {
        BiteType.Weak => WeakColor,
        BiteType.Strong => StrongColor,
        // legendary fish without hookset data fall back to Powerful
        BiteType.Legendary => hookset == HookType.Precision ? LegendaryPrecisionColor : LegendaryPowerfulColor,
        _ => UnknownColor,
    });

    private static Vector2 LegendSwatch => new(ImGui.GetTextLineHeight());

    private static float LegendWidth() {
        var style = ImGui.GetStyle();
        return Legend.Sum(e => LegendSwatch.X + style.ItemInnerSpacing.X + ImGui.CalcTextSize(e.Label).X) + (Legend.Length - 1) * style.ItemSpacing.X;
    }

    private static void DrawColorLegend() {
        const ImGuiColorEditFlags flags = ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoDragDrop | ImGuiColorEditFlags.NoAlpha;
        var style = ImGui.GetStyle();

        for (var i = 0; i < Legend.Length; i++) {
            var (label, color) = Legend[i];
            if (i > 0)
                ImGui.SameLine(0, style.ItemSpacing.X);
            ImGui.ColorButton($"###legend{i}", ImGui.ColorConvertU32ToFloat4(color) with { W = 1 }, flags, LegendSwatch);
            ImGui.SameLine(0, style.ItemInnerSpacing.X);
            ImGui.Text(label);
        }
    }

    private static string HooksetName(HookType hookset) => hookset switch {
        HookType.Precision => "Precision",
        HookType.Powerful => "Powerful",
        _ => "",
    };
}
