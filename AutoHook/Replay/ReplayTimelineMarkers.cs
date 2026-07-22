using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace AutoHook.Replay;

public sealed record ReplayTimelineMarker(DateTime Time, uint Color, string Label, string? TooltipExtra = null);
public sealed record ReplayTimelineSpan(DateTime Start, DateTime End, uint Color, string Label);

public static class ReplayTimelineMarkers {
    public const uint ActionColor = 0xff99cc66;
    public static readonly uint IntuitionBandColor = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.45f, 0.72f, 1f, 0.38f));
    public static readonly uint SpectralBandColor = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.88f, 0.42f, 1f, 0.38f));

    public static List<ReplayTimelineMarker> Build(FishingReplay replay) {
        var markers = new List<ReplayTimelineMarker>();
        uint prevBaitId = 0;
        var decisions = replay.Decisions;

        foreach (var op in replay.Ops) {
            switch (op) {
                case WorldState.OpBeganSession:
                    markers.Add(new(op.Timestamp, 0xff00ff00, "Session start"));
                    break;
                case WorldState.OpEndedSession:
                    markers.Add(new(op.Timestamp, 0xff0000ff, "Session end"));
                    break;
                case WorldState.OpDecision d when HasTimelineMarker(d): {
                        var label = $"Decision: {d.Context} -> {d.Action}";
                        if (!string.IsNullOrEmpty(d.Detail))
                            label += $" [{d.Detail}]";
                        if (!string.IsNullOrEmpty(d.PresetName))
                            label += $" ({d.PresetName})";
                        markers.Add(new(op.Timestamp, 0xffffff00, label));
                        break;
                    }
                case FishingInfo.OpPlayerUsedAction act when act.Value.ActionId != 0: {
                        var decision = FindNearestSuccessfulAutoCast(decisions, op.Timestamp, maxDeltaMs: 500);
                        var extra = decision is { ConditionResults.Count: > 0 }
                            ? DecisionLog.FormatConditionTrace(decision.ConditionResults)
                            : null;
                        markers.Add(new(op.Timestamp, ActionColor, $"Action: {ActionLabel(act.Value.ActionId)}", extra));
                        break;
                    }
                case FishingInfo.OpTugType tug when tug.HookStrength != 0:
                    markers.Add(new(op.Timestamp, 0xff0080ff, $"Bite: {tug.HookStrength}"));
                    break;
                case FishingInfo.OpSetLastCatch catchOp when catchOp.Value.FishId > 0: {
                        var c = catchOp.Value;
                        markers.Add(new(op.Timestamp, 0xffff00ff,
                            $"Catch: {Item.GetRow(c.FishId).Name} ×{c.Amount}"));
                        break;
                    }
                case FishingInfo.OpFishingState fish when fish.Bait.BaitId != prevBaitId && fish.Bait.BaitId != 0:
                    markers.Add(new(op.Timestamp, 0xffa0522d,
                        prevBaitId == 0
                            ? $"Bait: {Item.GetRow(fish.Bait.BaitId).Name}"
                            : $"Bait change: {Item.GetRow(fish.Bait.BaitId).Name}"));
                    prevBaitId = fish.Bait.BaitId;
                    break;
                case FishingInfo.OpSetFishingStep step:
                    if (step.Step.HasFlag(FishingSteps.PresetSwapped))
                        markers.Add(new(op.Timestamp, 0xffcc66ff, "Preset swapped"));
                    if (step.Step.HasFlag(FishingSteps.BaitSwapped))
                        markers.Add(new(op.Timestamp, 0xff66ccff, "Bait swapped (fish-caught rule)"));
                    break;
                case FishingInfo.OpAddFishCaught fc when fc.FishId > 0:
                    markers.Add(new(op.Timestamp, 0xff44dd44,
                        $"Fish counter +{fc.Amount}: {Item.GetRow(fc.FishId).Name}"));
                    break;
            }
        }

        return markers;
    }

    public static List<ReplayTimelineSpan> BuildSpans(FishingReplay replay) {
        var spans = new List<ReplayTimelineSpan>();
        if (replay.Ops.Count == 0)
            return spans;

        var end = replay.EndTime;
        var intuitionActive = false;
        DateTime? intuitionStart = null;
        var spectralActive = false;
        DateTime? spectralStart = null;

        foreach (var op in replay.Ops) {
            switch (op) {
                case FishingInfo.OpIntuition i: {
                        var active = i.Value.Status == IntuitionStatus.Active;
                        if (!intuitionActive && active)
                            intuitionStart = op.Timestamp;
                        else if (intuitionActive && !active && intuitionStart is { } start)
                            spans.Add(new(start, op.Timestamp, IntuitionBandColor, "Intuition"));
                        intuitionActive = active;
                        break;
                    }
                case WorldState.OpSpectralCurrentChanged sp:
                    if (sp.Change == SpectralCurrentChange.Gained) {
                        if (!spectralActive)
                            spectralStart = op.Timestamp;
                        spectralActive = true;
                    }
                    else if (sp.Change == SpectralCurrentChange.Lost) {
                        if (spectralActive && spectralStart is { } start)
                            spans.Add(new(start, op.Timestamp, SpectralBandColor, "Spectral"));
                        spectralActive = false;
                        spectralStart = null;
                    }
                    break;
                case OceanFishInfo.OpOceanFishing ocean: {
                        var active = ocean.State?.SpectralCurrentActive ?? false;
                        if (!spectralActive && active)
                            spectralStart = op.Timestamp;
                        else if (spectralActive && !active && spectralStart is { } start)
                            spans.Add(new(start, op.Timestamp, SpectralBandColor, "Spectral"));
                        spectralActive = active;
                        if (!active)
                            spectralStart = null;
                        break;
                    }
            }
        }

        if (intuitionActive && intuitionStart is { } iStart)
            spans.Add(new(iStart, end, IntuitionBandColor, "Intuition"));
        if (spectralActive && spectralStart is { } sStart)
            spans.Add(new(sStart, end, SpectralBandColor, "Spectral"));

        return spans;
    }

    /// <summary>Successful Auto Casts keep markers; failed attempts stay in the decision list only.</summary>
    private static bool HasTimelineMarker(WorldState.OpDecision d)
        => d.Context != UIStrings.Auto_Casts || d.Action.StartsWith("Cast ", StringComparison.Ordinal);

    private static WorldState.OpDecision? FindNearestSuccessfulAutoCast(IReadOnlyList<WorldState.OpDecision> decisions, DateTime time, double maxDeltaMs) {
        WorldState.OpDecision? best = null;
        var bestDelta = maxDeltaMs;
        foreach (var d in decisions) {
            if (d.Context != UIStrings.Auto_Casts || !d.Action.StartsWith("Cast ", StringComparison.Ordinal))
                continue;
            var delta = Math.Abs((d.Timestamp - time).TotalMilliseconds);
            if (delta >= bestDelta)
                continue;
            bestDelta = delta;
            best = d;
        }
        return best;
    }

    private static string ActionLabel(uint actionId) {
        try {
            return LuminaAction.GetRow(actionId).Name.ToString();
        }
        catch {
            return $"#{actionId}";
        }
    }
}
