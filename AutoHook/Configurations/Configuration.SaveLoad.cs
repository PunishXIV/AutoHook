using Newtonsoft.Json;
using System.IO;
using System.Threading;

namespace AutoHook.Configurations;

public partial class Configuration {
    private static readonly JsonSerializerSettings SaveSettings = new() {
        Formatting = Formatting.Indented,
    };

    private static readonly JsonSerializerSettings LoadSettings = new() {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
    };

    private static int _savePending;
    private static int _saveSuppressionDepth;
    private static Task? _saveTask;
    private static readonly object _lock = new();
    private static readonly object _diskWriteLock = new();

    internal static readonly object SerializationSync = new(); // lock for capturing snapshot

    /// <summary>Run config mutations under the same lock used when serializing to disk.</summary>
    internal static void MutateSerialized(Action action) {
        lock (SerializationSync)
            action();
    }

    /// <summary>Suppresses coalesced saves while bulk-building presets (AddItem/ReplaceBaitConfig call Save).</summary>
    internal static SaveSuppressionScope SuppressSave() => new();

    internal readonly struct SaveSuppressionScope : IDisposable {
        public SaveSuppressionScope() => Interlocked.Increment(ref _saveSuppressionDepth);
        public void Dispose() => Interlocked.Decrement(ref _saveSuppressionDepth);
    }

    public static async Task<Configuration> LoadAsync(CancellationToken cancellationToken = default) {
        try {
            var file = Svc.Interface.ConfigFile;

            if (file.Exists) {
                var json = await File.ReadAllTextAsync(file.FullName, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
                var configDirectory = Path.GetDirectoryName(file.FullName);
                var migratedJson = ConfigurationJsonMigrator.MigrateToLatest(json, configDirectory);

                Configuration? config;
                try {
                    config = JsonConvert.DeserializeObject<Configuration>(migratedJson, LoadSettings);
                }
                catch (Exception ex) {
                    Svc.Log.Error(@$"[Configuration] Failed to deserialize migrated config JSON: {ex.Message}");
                    config = null;
                }

                if (config != null) {
                    config.Initiate();
                    if (migratedJson != json)
                        await WriteAsync(config, cancellationToken).ConfigureAwait(false);
                    return config;
                }

                BackupUnreadableConfigFile(file.FullName);
                TryDeleteConfigFile(file.FullName);
                Svc.Log.Warning(@"[Configuration] Config file exists but could not be deserialized; recreating defaults.");
            }

            var fresh = new Configuration();
            fresh.Initiate();
            await WriteAsync(fresh, cancellationToken).ConfigureAwait(false);
            return fresh;
        }
        catch (Exception e) {
            Svc.Log.Error(@$"[Configuration] {e.Message}");
            throw;
        }
    }

    /// <summary>Queues a coalesced background write of <see cref="Service.Configuration"/>.</summary>
    public static void Save() {
        if (Volatile.Read(ref _saveSuppressionDepth) > 0)
            return;

        Interlocked.Exchange(ref _savePending, 1);

        lock (_lock) {
            if (_saveTask is { IsCompleted: false })
                return;

            _saveTask = RunSaveLoopAsync();
        }
    }

    /// <summary>Waits for any in-prog save, then writes if still dirty.</summary>
    public static async Task FlushAsync() {
        var task = _saveTask;
        if (task != null)
            await task.ConfigureAwait(false);

        if (Interlocked.CompareExchange(ref _savePending, 0, 0) == 0)
            return;

        await WriteAsync(Service.Configuration).ConfigureAwait(false);
        Interlocked.Exchange(ref _savePending, 0);
    }

    private static async Task RunSaveLoopAsync() {
        try {
            while (true) {
                Interlocked.Exchange(ref _savePending, 0);
                await WriteAsync(Service.Configuration).ConfigureAwait(false);
                if (Interlocked.CompareExchange(ref _savePending, 0, 0) == 0)
                    break;
            }
        }
        catch (Exception ex) {
            Svc.Log.Error(ex, "[Configuration] Save failed.");
        }
        finally {
            lock (_lock) {
                if (Interlocked.CompareExchange(ref _savePending, 0, 0) == 1)
                    _saveTask = RunSaveLoopAsync();
                else
                    _saveTask = null;
            }
        }
    }

    private static void TryDeleteConfigFile(string configPath) {
        try {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
        catch (Exception e) {
            Svc.Log.Warning(@$"[Configuration] Could not delete unreadable config before rewrite: {e.Message}");
        }
    }

    private static void BackupUnreadableConfigFile(string configPath) {
        try {
            if (!File.Exists(configPath))
                return;

            var dir = Svc.Interface.GetPluginConfigDirectory();
            var path = Path.Combine(dir, "autohook_load_failed_backup.json");
            if (File.Exists(path)) {
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                path = Path.Combine(dir, $"autohook_load_failed_backup_{stamp}.json");
            }

            File.Copy(configPath, path, overwrite: false);
            Svc.Log.Warning(@$"[Configuration] Backed up unreadable config to {path}");
        }
        catch (Exception e) {
            Svc.Log.Warning(@$"[Configuration] Failed to back up unreadable config: {e.Message}");
        }
    }

    private static Task WriteAsync(Configuration config, CancellationToken cancellationToken = default) {
        var path = Svc.Interface.ConfigFile.FullName;
        var json = CaptureSnapshot(config);
        return Task.Run(() => WriteJsonToDisk(json, path, cancellationToken), cancellationToken);
    }

    private static string CaptureSnapshot(Configuration config) {
        lock (SerializationSync) {
            return JsonConvert.SerializeObject(config, SaveSettings);
        }
    }

    private static void WriteJsonToDisk(string json, string path, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_diskWriteLock) {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = path + ".new";
            if (File.Exists(tempPath)) {
                Svc.Log.Warning($"[Configuration] Removing stale temp file {tempPath}");
                File.Delete(tempPath);
            }

            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, path, overwrite: true);
        }
    }
}
