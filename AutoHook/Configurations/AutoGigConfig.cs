using System;
using System.Collections.Generic;
using System.Linq;
using AutoHook.Classes;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

namespace AutoHook.Configurations;

public class AutoGigConfig : BasePresetConfig
{
    public string Name { get; set; } = "Old Preset";

    public List<BaseGig> Gigs { get; set; } = new();

    public int HitboxSize = 25;

    public AutoGigConfig(string presetName)
    {
        PresetName = presetName;
    }


    public List<BaseGig> GetGigCurrentNode(int node) =>
        Gigs.Where(f => f.Fish != null && f.Fish.Nodes.Contains(node)).ToList();

    public override void AddItem(BaseOption item)
    {
        Gigs.Add((BaseGig)item);
        Service.Save();
    }

    public override void RemoveItem(Guid value)
    {
        Gigs.RemoveAll(x => x.UniqueId == value);
        Service.Save();
    }

    public override void DrawOptions()
    {
        if (Gigs == null || Gigs.Count == 0)
            return;

        foreach (var gig in Gigs)
        {
            ImGui.PushID(gig.UniqueId.ToString());
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var icon = FontAwesomeIcon.Trash.ToIconString();
                var buttonSize = ImGui.CalcTextSize(icon) + ImGui.GetStyle().FramePadding * 2;
                if (ImGui.Button(@$"{icon}", buttonSize) &&
                    ImGui.GetIO().KeyShift)
                {
                    RemoveItem(gig.UniqueId);
                    Service.Save();
                    return;
                }
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(UIStrings.HoldShiftToDelete);

            ImGui.SameLine(0, 3);

            DrawUtil.Checkbox(@$"", ref gig.Enabled);

            ImGui.SameLine(0, 3);

            var x = ImGui.GetCursorPosX();
            if (ImGui.TreeNodeEx($"{gig.Fish?.Name ?? UIStrings.None}", ImGuiTreeNodeFlags.FramePadding))
            {
                ImGui.SetCursorPosX(x);
                ImGui.BeginGroup();
                gig.DrawOptions();
                ImGui.EndGroup();
                ImGui.TreePop();
            }

            ImGui.PopID();
        }
    }
}