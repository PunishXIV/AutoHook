namespace AutoHook.Presets.Config;

public partial class Configuration {
    // one step that bumps config to a target Version.
    private interface IConfigMigration {
        // version after this migration succeeds.
        int Version { get; }

        // run the migration if applicable.
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

    // v1→v2: version bump (clib handles config backups).
    private sealed class V2Migration : IConfigMigration {
        public int Version => 2;

        public void Apply(Configuration config) {
            if (config.Version != 1)
                return;

            config.Version = 2;
        }
    }

    // v2→v3: handled in JsonMigrator.MigrateV2ToV3Json; mostly a version bump here.
    private sealed class V3Migration : IConfigMigration {
        public int Version => 3;

        public void Apply(Configuration config) {
            if (config.Version != 2)
                return;
            config.Version = 3;
        }
    }

    // v3→v4: re-save under new schema.
    private sealed class V4Migration : IConfigMigration {
        public int Version => 4;

        public void Apply(Configuration config) {
            if (config.Version != 3)
                return;

            IPluginLog.Get().Debug(@$"[Configuration] Updating to v4");
            config.Version = 4;
        }
    }

    // v4→v5: autogig rename + default name.
    private sealed class V5Migration : IConfigMigration {
        public int Version => 5;

        public void Apply(Configuration config) {
            if (config.Version != 4)
                return;

            IPluginLog.Get().Debug(@$"[Configuration] Updating to v5");

            foreach (var gig in config.AutoGigConfig.Presets) {
                IPluginLog.Get().Debug($"Renaming {gig.PresetName} to {gig.Name}");
                gig.PresetName = gig.Name;
            }

            config.HookPresets.DefaultPreset.PresetName = AutoHook.GlobalPresetName;
            config.Version = 5;
        }
    }
}
