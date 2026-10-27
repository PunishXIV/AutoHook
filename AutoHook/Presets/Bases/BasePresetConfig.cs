using Newtonsoft.Json;

namespace AutoHook.Presets.Bases;

public abstract class BasePresetConfig() {
    public string PresetName {
        get => field ?? "";
        set => field = value ?? "";
    } = "";

    [JsonIgnore] public string GroupName { get; set; } = "";

    public Guid UniqueId { get; set; } = Guid.NewGuid();

    public abstract void DrawOptions();

    public virtual void RenamePreset(string newName) {
        PresetName = newName;
        Configuration.Save(this as CustomPresetConfig);
    }

    public abstract void AddItem(BaseOption item);

    public abstract void RemoveItem(Guid value);
}
