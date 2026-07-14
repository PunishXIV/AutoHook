using AutoHook.Conditions;

namespace AutoHook.Replay;

public sealed class DecisionLog : IDisposable {
    private readonly string _context;
    private string? _presetName;
    private string? _about;
    private readonly List<string> _skipped = [];
    private IReadOnlyList<(string Label, bool Result)>? _conditions;
    private bool _committed;

    private DecisionLog(string context, string? presetName) {
        _context = context;
        _presetName = presetName;
    }

    public static DecisionLog Start(string context, string? presetName = null)
        => new(context, presetName);

    public static string FormatConditionTrace(IReadOnlyList<(string Label, bool Result)> trace)
        => trace.Count == 0 ? string.Empty : string.Join("\n", trace.Select(t => $"{t.Label}: {(t.Result ? "T" : "F")}"));

    public DecisionLog About(string summary) {
        _about = summary;
        return this;
    }

    public DecisionLog Skipped(string reason) {
        _skipped.Add(reason);
        return this;
    }

    public DecisionLog WithPreset(string presetName) {
        _presetName = presetName;
        return this;
    }

    public DecisionLog WithConditions(ConditionSet? set) {
        if (set != null)
            _conditions = set.DescribeEvaluation(Service.WorldState, ConditionRegistry.Registry);
        return this;
    }

    public DecisionLog WithConditionResults(IReadOnlyList<(string Label, bool Result)>? results) {
        if (results is { Count: > 0 })
            _conditions = results;
        return this;
    }

    public void Chose(string outcome) {
        Commit(outcome);
    }

    public void Dispose() {
        if (!_committed)
            Commit("(no outcome)");
    }

    private void Commit(string action) {
        if (_committed)
            return;
        _committed = true;

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(_about))
            lines.Add(_about);
        lines.AddRange(_skipped);
        var detail = string.Join("\n", lines);

        var preset = _presetName ?? Service.Configuration.HookPresets.SelectedPreset?.PresetName ?? Service.GlobalPresetName;
        Service.WorldState.LogDecision(_context, preset, action, _conditions, detail);
    }
}
