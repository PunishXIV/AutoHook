using AutoHook.Replay;
using AutoHook.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using System.IO;
using System.Reflection;
using System.Threading;

namespace AutoHook;

public sealed class ReplayManager : IDisposable {
    private const int MaxReplayFiles = 10;

    public sealed class ReplayEntry : IDisposable {
        public string Path;
        public float Progress;
        public CancellationTokenSource Cancel = new();
        public Task<FishingReplay> Replay;
        public ReplayDetailsWindow? Window;
        public bool AutoShowWindow;
        public bool Selected;
        public bool Disposed;
        public bool Disposing;
        public DateTime? InitialTime;

        public ReplayEntry(string path, bool autoShow, DateTime? initialTime = null) {
            Path = path;
            AutoShowWindow = autoShow;
            InitialTime = initialTime;
            Replay = Task.Run(() => ReplayParser.Parse(path, ref Progress, Cancel.Token));
        }

        public void Dispose() {
            Disposing = true;
            Window?.Dispose();
            Cancel.Cancel();
            try {
                Replay.Wait();
            }
            catch { }
            Replay.Dispose();
            Cancel.Dispose();
            Disposed = true;
        }

        public void Show() {
            if (!Replay.IsCompletedSuccessfully || Replay.Result.Ops.Count == 0)
                return;
            Window ??= new ReplayDetailsWindow(Replay.Result, InitialTime);
            Window.IsOpen = true;
            Window.BringToFront();
        }
    }

    private ReplayRecorder? _recorder;
    private readonly EventSubscriptions _subs;
    private readonly List<ReplayEntry> _entries = [];
    private string _path = "";
    private string _fileDialogStartPath;
    /// <summary>Defer stop so <see cref="WorldState.OpEndedSession"/> can enqueue via Modified before we unsubscribe.</summary>
    private int _stopAfterFrames;

    public bool IsRecording => _recorder != null;
    public string? LastRecordedPath { get; private set; }
    public DirectoryInfo ReplayDirectory { get; }

    public ReplayManager() {
        var dir = Path.Combine(Svc.Interface.GetPluginConfigDirectory(), "replays");
        ReplayDirectory = new DirectoryInfo(dir);
        ReplayDirectory.Create();
        _fileDialogStartPath = ReplayDirectory.FullName;
        _path = ReplayDirectory.FullName;

        var ws = Service.WorldState;
        _subs = new(
            ws.BeganSession.Subscribe(_ => TryAutoStart()),
            ws.OceanZoneStarted.Subscribe(_ => TryAutoStart()), // zone start happens before BeganSession. capture OZON / ACHP during walk to railing
            ws.EndedSession.Subscribe(_ => TryAutoStop()));

        Svc.Framework.Update += OnFrameworkUpdate;
        PruneOldReplays();
    }

    private void OnFrameworkUpdate(IFramework _) {
        _recorder?.FlushPending();
        if (_stopAfterFrames > 0 && --_stopAfterFrames == 0)
            StopRecording();
        Update();
    }

    public void Dispose() {
        Svc.Framework.Update -= OnFrameworkUpdate;
        _stopAfterFrames = 0;
        StopRecording();
        _subs.Dispose();
        foreach (var e in _entries)
            e.Dispose();
        _entries.Clear();
    }

    public void Update() {
        _entries.RemoveAll(e => e.Disposed);

        foreach (var e in _entries) {
            if (e.AutoShowWindow && e.Window == null && e.Replay.IsCompletedSuccessfully && e.Replay.Result.Ops.Count > 0)
                e.Show();
        }
    }

    public void Draw() {
        DrawNewEntry();
        DrawEntries();
        DrawEntriesOperations();
    }

    public void StartRecording(bool manual = true) {
        if (_recorder != null)
            return;

        var prefix = manual ? "manual" : "session";
        _recorder = new ReplayRecorder(Service.WorldState, ReplayDirectory, prefix, logInitialState: true);
        _recorder.WritePresetSnapshot(SerializeCurrentPreset());
        LastRecordedPath = _recorder.FilePath;
        Service.PrintDebug($"[Replay] Recording started: {_recorder.FilePath}");
    }

    public void StopRecording() {
        if (_recorder is not { } recorder)
            return;

        recorder.FlushPending();
        recorder.WriteMeta(BuildMetadata());
        LastRecordedPath = recorder.FilePath;
        recorder.Dispose();
        _recorder = null;
        PruneOldReplays();
        Service.PrintDebug($"[Replay] Recording stopped: {LastRecordedPath}");
    }

    private void DrawNewEntry() {
        ImGui.InputText("###path", ref _path, 500);
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.File)) {
            Service.FileDialog.OpenFileDialog("Select replay", ".ahlog", (confirmed, paths) => {
                if (confirmed && paths.Count > 0) {
                    _path = paths[0];
                    _fileDialogStartPath = new FileInfo(_path).Directory!.FullName;
                }
            }, 1, _fileDialogStartPath);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Open file");
        ImGui.SameLine();
        using (ImRaii.Disabled(_path.Length == 0 || _entries.Any(e => e.Path == _path))) {
            if (ImGui.Button("Open"))
                AddEntry(_path, autoShow: true);
        }
    }

    private void DrawEntries() {
        using var table = ImRaii.Table("replay_entries", 3, ImGuiTableFlags.Resizable);
        if (!table)
            return;

        ImGui.TableSetupColumn("op", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("unload", ImGuiTableColumnFlags.WidthFixed, 50);

        foreach (var e in _entries) {
            using var idScope = ImRaii.PushId(e.Path);

            ImGui.TableNextColumn();
            if (!e.Replay.IsCompleted) {
                ImGui.ProgressBar(e.Progress, new System.Numerics.Vector2(100, 0));
            }
            else if (e.Replay.IsFaulted || e.Replay.Result.Ops.Count == 0) {
                using var color = ImRaii.PushColor(ImGuiCol.Text, 0xff0000ff);
                ImGui.Text("(failed)");
            }
            else {
                if (ImGui.Button("Actions...", new System.Numerics.Vector2(100, 0)))
                    ImGui.OpenPopup("ctx");
                using var popup = ImRaii.Popup("ctx");
                if (popup) {
                    if (ImGui.MenuItem("Show"))
                        e.Show();
                }
            }

            ImGui.TableNextColumn();
            if (ImGui.Button(e.Replay.IsCompleted ? "Unload" : "Cancel", new System.Numerics.Vector2(50, 0)))
                e.Dispose();

            ImGui.TableNextColumn();
            ImGui.Checkbox($"{e.Path}", ref e.Selected);
        }
    }

    private void DrawEntriesOperations() {
        if (_entries.Count == 0)
            return;

        var numSelected = _entries.Count(e => e.Selected);
        var shouldSelectAll = numSelected < _entries.Count;
        if (ImGui.Button(shouldSelectAll ? "Select all" : "Unselect all", new System.Numerics.Vector2(80, 0))) {
            foreach (var e in _entries)
                e.Selected = shouldSelectAll;
        }
        using (ImRaii.Disabled(numSelected == 0)) {
            ImGui.SameLine();
            if (ImGui.Button("Show selected")) {
                foreach (var e in _entries.Where(e => e.Selected))
                    e.Show();
            }
            ImGui.SameLine();
            if (ImGui.Button("Unload selected")) {
                foreach (var e in _entries.Where(e => e.Selected).ToList())
                    e.Dispose();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Unload all")) {
            foreach (var e in _entries.ToList())
                e.Dispose();
        }
    }

    private void AddEntry(string path, bool autoShow) {
        CleanPath(ref path);
        if (path.Length == 0 || _entries.Any(e => e.Path == path))
            return;
        if (!File.Exists(path))
            return;
        _entries.Add(new ReplayEntry(path, autoShow));
        _path = path;
    }

    private static void CleanPath(ref string path) {
        path = path.Trim();
        if (path.StartsWith('"') && path.EndsWith('"'))
            path = path[1..^1];
    }

    private void TryAutoStart() {
        _stopAfterFrames = 0;
        if (_recorder == null)
            StartRecording(manual: false);
    }

    private void TryAutoStop() {
        if (_recorder == null)
            return;
        // OpEndedSession.Fire runs before Modified enqueues FEND, so stop next frame so FlushPending can write FEND before disposal
        _stopAfterFrames = 1;
    }

    private void PruneOldReplays() {
        if (!ReplayDirectory.Exists)
            return;

        foreach (var file in ReplayDirectory.GetFiles("*.ahlog")
                     .OrderByDescending(f => f.LastWriteTimeUtc)
                     .Skip(MaxReplayFiles)) {
            try {
                file.Delete();
                Service.PrintDebug($"[Replay] Pruned old replay: {file.Name}");
            }
            catch (Exception e) {
                Svc.Log.Warning($"[Replay] Failed to delete {file.FullName}: {e.Message}");
            }
        }
    }

    private static ReplayMetadata BuildMetadata() {
        var cfg = Service.Configuration;
        return new ReplayMetadata {
            PresetName = cfg.HookPresets.CurrentPreset.PresetName,
            PluginVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty,
            TerritoryId = Service.WorldState.TerritoryId,
            PresetSnapshotJson = SerializeCurrentPreset(),
        };
    }

    private static string SerializeCurrentPreset() {
        var preset = Service.Configuration.HookPresets.CurrentPreset;
        try {
            return JsonConvert.SerializeObject(preset);
        }
        catch (Exception e) {
            Svc.Log.Warning($"[Replay] Failed to serialize preset snapshot: {e.Message}");
            return string.Empty;
        }
    }
}
