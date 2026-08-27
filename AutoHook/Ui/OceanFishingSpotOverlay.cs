using AutoHook.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHook.Ui;

public static class OceanFishingSpotOverlay {
    private static readonly uint FillColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.75f, 0.95f, 0.28f));
    private static readonly uint OutlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.9f, 1f, 0.9f));
    private static readonly uint LabelColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f));
    private static readonly uint InSpotColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.95f, 0.35f, 0.95f));
    private static readonly uint OutSpotColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.35f, 0.2f, 0.95f));

    public static bool Enabled { get; set; }

    public static void Draw() {
        if (!Enabled) return;
        if (Svc.PlayerState.TerritoryIntendedUse is not FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse.OceanFishing) return;
        var dl = ImGui.GetBackgroundDrawList(ImGuiHelpers.MainViewport);
        var player = Svc.Objects.LocalPlayer;
        var inAnySpot = false;

        foreach (var region in AutoOceanFish.ValidFishingRegions) {
            if (player != null && region.ContainsXZ(player.Position, margin: 0.15f))
                inAnySpot = true;
            DrawRegion(dl, region);
        }

        if (player != null && Svc.GameGui.WorldToScreen(player.Position, out var playerScreen, out var inFront) && inFront) {
            var color = inAnySpot ? InSpotColor : OutSpotColor;
            dl.AddCircleFilled(playerScreen, 6f, color);
            dl.AddCircle(playerScreen, 10f, color, 0, 2f);
            var status = inAnySpot ? "in" : "out";
            dl.AddText(playerScreen + new Vector2(12f, -8f), color, status);
        }
    }

    private static void DrawRegion(ImDrawListPtr dl, FishingSpotRegion region) {
        var corners = region.Corners;
        Span<Vector2> screen = stackalloc Vector2[4];
        for (var i = 0; i < 4; i++) {
            if (!Svc.GameGui.WorldToScreen(corners[i], out screen[i], out var inFront) || !inFront)
                return;
        }

        dl.AddQuadFilled(screen[0], screen[1], screen[2], screen[3], FillColor);
        dl.AddQuad(screen[0], screen[1], screen[2], screen[3], OutlineColor, 2.5f);

        if (Svc.GameGui.WorldToScreen(region.Centroid, out var labelPos, out var labelFront) && labelFront) {
            var label = $"{region.Name}\nX[{region.MinX:0.##},{region.MaxX:0.##}] Z[{region.MinZ:0.##},{region.MaxZ:0.##}]";
            var size = ImGui.CalcTextSize(label);
            dl.AddRectFilled(labelPos - new Vector2(4f, 2f), labelPos + size + new Vector2(4f, 2f), ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));
            dl.AddText(labelPos, LabelColor, label);
        }
    }
}
