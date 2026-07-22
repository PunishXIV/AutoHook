using System.Threading;

namespace AutoHook.Configurations;

public partial class Configuration {
    /// <summary>
    /// Single migration step that upgrades a configuration to a specific Version.
    /// </summary>
    private interface IConfigMigration {
        /// <summary>Target configuration version after this migration has successfully run.</summary>
        int Version { get; }

        /// <summary>Apply this migration to the given configuration if applicable.</summary>
        void Apply(Configuration config);
    }

    // Ordered list of migrations. Each migration is responsible for moving from (Version - 1) to Version.
    private static readonly IConfigMigration[] _migrations =
    [
        new V2Migration(),
        new V3Migration(),
        new V4Migration(),
        new V5Migration(),
        // V6+ migrations are handled by ConfigurationJsonMigrator at the JSON level
    ];

    internal static void RunMigrationsUpTo(Configuration config, int maxVersion) {
        foreach (var migration in _migrations) {
            if (migration.Version > maxVersion)
                continue;
            if (config.Version < migration.Version)
                migration.Apply(config);
        }
    }

    /// <summary>v1 -> v2: backup only.</summary>
    private sealed class V2Migration : IConfigMigration {
        public int Version => 2;

        public void Apply(Configuration config) {
            if (config.Version != 1)
                return;

            config.WriteVersionBackup(1);
            config.Version = 2;
        }
    }

    /// <summary>v2 -> v3: now done in ConfigurationJsonMigrator.MigrateV2ToV3Json. No-op here.</summary>
    private sealed class V3Migration : IConfigMigration {
        public int Version => 3;

        public void Apply(Configuration config) {
            if (config.Version != 2)
                return;
            config.WriteVersionBackup(2);
            config.Version = 3;
        }
    }

    /// <summary>v3 -> v4: backup then re-save using the new schema.</summary>
    private sealed class V4Migration : IConfigMigration {
        public int Version => 4;

        public void Apply(Configuration config) {
            if (config.Version != 3)
                return;

            Service.PrintDebug(@$"[Configuration] Updating to v4");
            config.WriteVersionBackup(3);
            WriteJsonToDisk(CaptureSnapshot(config), Svc.Interface.ConfigFile.FullName, CancellationToken.None);
            config.Version = 4;
        }
    }

    /// <summary>v4 -> v5: AutoGig preset rename + default preset name update.</summary>
    private sealed class V5Migration : IConfigMigration {
        public int Version => 5;

        public void Apply(Configuration config) {
            if (config.Version != 4)
                return;

            Service.PrintDebug(@$"[Configuration] Updating to v5");

            config.WriteVersionBackup(4);
            foreach (var gig in config.AutoGigConfig.Presets) {
                Service.PrintDebug($"Renaming {gig.PresetName} to {gig.Name}");
                gig.PresetName = gig.Name;
            }

            config.HookPresets.DefaultPreset.PresetName = Service.GlobalPresetName;

            WriteJsonToDisk(CaptureSnapshot(config), Svc.Interface.ConfigFile.FullName, CancellationToken.None);
            config.Version = 5;
        }
    }
}
