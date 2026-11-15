using clib.Configuration;
using Newtonsoft.Json.Linq;

namespace AutoHook.Presets.Migration;

// clib discovers json migrations by type and needs one parameterless class per target version.
file abstract class ConfigJsonStep(int targetVersion, Action<JObject> migrate) : IConfigJsonMigration<Configuration> {
    public int TargetVersion => targetVersion;
    public void Migrate(JObject root) => migrate(root);
}

file sealed class ConfigJsonV3() : ConfigJsonStep(3, ConfigurationJsonMigrator.MigrateV2ToV3Json);
file sealed class ConfigJsonV5() : ConfigJsonStep(5, ConfigurationJsonMigrator.MigrateV5);
file sealed class ConfigJsonV6() : ConfigJsonStep(6, ConfigurationJsonMigrator.MigrateV6);
file sealed class ConfigJsonV7() : ConfigJsonStep(7, ConfigurationJsonMigrator.MigrateV7);
file sealed class ConfigJsonV8() : ConfigJsonStep(8, ConfigurationJsonMigrator.MigrateV8);
file sealed class ConfigJsonV9() : ConfigJsonStep(9, ConfigurationJsonMigrator.MigrateV9);
file sealed class ConfigJsonV10() : ConfigJsonStep(10, ConfigurationJsonMigrator.MigrateV10);
file sealed class ConfigJsonV11() : ConfigJsonStep(11, ConfigurationJsonMigrator.MigrateV11);
