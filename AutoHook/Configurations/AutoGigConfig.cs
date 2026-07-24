using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using System.ComponentModel;

namespace AutoHook.Configurations;

public class AutoGigConfig : BasePresetConfig {
    [DefaultValue("Old Preset")]
    public string Name { get; set; } = "Old Preset";

    public List<BaseGig> Gigs { get; set; } = [];

    [DefaultValue(25)]
    public int HitboxSize = 25;

    public bool KeepCollectorsGloveOn = false;

    public AutoGigConfig(string presetName) => PresetName = presetName;

    public List<BaseGig> GetGigCurrentNode(int node) {
        return [.. Gigs.Where(f => f.Fish != null && (f.Fish.Nodes is not { Count: > 0 } || f.Fish.Nodes.Contains(node)))];
    }

    public override void AddItem(BaseOption item) {
        Gigs.Add((BaseGig)item);
        Service.Save();
    }

    public override void RemoveItem(Guid value) {
        Gigs.RemoveAll(x => x.UniqueId == value);
        Service.Save();
    }

    public override void DrawOptions() {
        if (Gigs.Count == 0)
            return;

        foreach (var gig in Gigs) {
            using var gigId = ImRaii.PushId(gig.UniqueId.ToString());
            using (ImRaii.PushFont(UiBuilder.IconFont)) {
                var icon = FontAwesomeIcon.Trash.ToIconString();
                var buttonSize = ImGui.CalcTextSize(icon) + ImGui.GetStyle().FramePadding * 2;
                if (ImGui.Button(@$"{icon}", buttonSize) &&
                    ImGui.GetIO().KeyShift) {
                    RemoveItem(gig.UniqueId);
                    Service.Save();
                    return;
                }
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(UIStrings.HoldShiftToDelete);

            ImGui.SameLine(0, 3.Scaled());

            DrawUtil.Checkbox(@$"", ref gig.Enabled);

            ImGui.SameLine(0, 3.Scaled());

            var x = ImGui.GetCursorPosX();
            if (ImGui.TreeNodeEx($"{gig.Fish?.Name ?? UIStrings.None}", ImGuiTreeNodeFlags.FramePadding)) {
                ImGui.SetCursorPosX(x);
                using (ImRaii.Group()) {
                    gig.DrawOptions();
                }
                ImGui.TreePop();
            }
        }
    }
}
