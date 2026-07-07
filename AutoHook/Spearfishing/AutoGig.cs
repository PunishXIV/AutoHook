using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ECommons.Automation;
using ECommons.Automation.NeoTaskManager;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LuminaAction = Lumina.Excel.Sheets.Action;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AutoHook.Spearfishing;

internal class AutoGig : Window, IDisposable {
    private const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.NoDecoration
                                                 | ImGuiWindowFlags.NoInputs
                                                 | ImGuiWindowFlags.AlwaysAutoResize
                                                 | ImGuiWindowFlags.NoFocusOnAppearing
                                                 | ImGuiWindowFlags.NoNavFocus
                                                 | ImGuiWindowFlags.NoBackground;

    private float _uiScale = 1;
    private Vector2 _uiPos = Vector2.Zero;
    private Vector2 _uiSize = Vector2.Zero;

    private int currentNode = 0;

    private readonly SpearFishingPresets _gigCfg = Service.Configuration.AutoGigConfig;

    public static string Gig = "Gig";

    private readonly TaskManager _taskManager = new() {
        DefaultConfiguration = { TimeLimitMS = 10000, ShowDebug = false }
    };

    public AutoGig() : base(@"SpearfishingHelper", WindowFlags, true) {
        Service.WindowSystem.AddWindow(this);
        IsOpen = true;
        Gig = LuminaAction.GetRow(IDs.Actions.Gig).Name.ToString();
    }

    public void Dispose() {
        Service.WindowSystem.RemoveWindow(this);
        Configuration.FlushAsync().GetAwaiter().GetResult();
    }

    public override void Draw() {
        if (!_gigCfg.AutoGigHideOverlay || _gigCfg.AutoGigEnabled)
            DrawFishOverlay();
    }

    public void DrawSettings() {
        if (ImGui.Checkbox(UIStrings.Enable_AutoGig, ref _gigCfg.AutoGigEnabled))
            Service.Save();

        var selectedPreset = _gigCfg.SelectedPreset;
        ImGui.SameLine();
        DrawUtil.Checkbox(UIStrings.CatchEverything, ref _gigCfg.CatchAll, UIStrings.IgnoresPresets);
        PluginUi.ShowKofi();
        DrawUtil.DrawComboSelector(_gigCfg.Presets, preset => preset.PresetName, _gigCfg.SelectedPreset?.PresetName ?? UIStrings.None, gig => _gigCfg.SelectedPreset = gig);

        ImGui.SetNextItemWidth(90.Scaled());
        if (selectedPreset != null) {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90.Scaled());
            if (ImGui.InputInt(UIStrings.Hitbox + @" ", ref selectedPreset.HitboxSize)) {
                selectedPreset.HitboxSize = Math.Max(0, Math.Min(selectedPreset.HitboxSize, 300));
                Service.Save();
            }
        }

        ImGui.SameLine();

        if (_gigCfg.CatchAll)
            ImGui.TextColored(ImGuiColors.DalamudYellow, UIStrings.CatchAllGigWindow);
    }

    private unsafe void DrawFishOverlay() {
        if (!Svc.GameGui.TryGetAddon<AddonSpearFishing>("SpearFishing", out var addon)) return;
        var isOpen = addon != null && addon->AtkUnitBase.WindowNode != null;

        if (!isOpen)
            return;

        ImGui.SetNextWindowPos(new Vector2(addon->AtkUnitBase.X + 5, addon->AtkUnitBase.Y - 65));
        if (ImGui.Begin("gig###gig", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar)) {
            DrawSettings();
            ImGui.End();
        }

        if (_gigCfg is { AutoGigEnabled: true, }) {
            var selectedPreset = _gigCfg.SelectedPreset;

            if (selectedPreset is { KeepCollectorsGloveOn: true } && !Service.WorldState.HasStatus(IDs.Status.CollectorsGlove))
                PlayerRes.CastActionDelayed(IDs.Actions.Collect, actionName: UIStrings.Collect);

            if (!Service.WorldState.HasStatus(IDs.Status.NaturesBounty) && _gigCfg.NatureBountyBeforeFish)
                PlayerRes.CastActionDelayed(IDs.Actions.NaturesBounty);
            ;
            GigFish(addon, addon->Fish[0], addon->GetNodeById(15));
            GigFish(addon, addon->Fish[1], addon->GetNodeById(16));
            GigFish(addon, addon->Fish[2], addon->GetNodeById(17));
        }
    }

    private unsafe void GigFish(AddonSpearFishing* addon, AddonSpearFishing.FishInfo info, AtkResNode* node) {
        var drawList = ImGui.GetWindowDrawList();
        var gigHitbox = _gigCfg.SelectedPreset?.HitboxSize ?? 0;

        DrawGigHitbox(addon, drawList, gigHitbox);

        if (_gigCfg.ThaliaksFavor.IsAvailableToCast())
            PlayerRes.CastActionDelayed(_gigCfg.ThaliaksFavor.Id, _gigCfg.ThaliaksFavor.ActionType,
                UIStrings.Thaliaks_Favor);

        if (!info.Available) {
            Service.PrintDebug("[AutoGig] GigFish - Fish not available");
            return;
        }

        var fish = _gigCfg.CatchAll ? GetCatchAllGig() : CheckFish(info);
        Service.PrintDebug($"[AutoGig] GigFish - fish: {(fish != null ? fish.Fish?.Name ?? "null" : "null")}, Enabled: {fish?.Enabled ?? false}, CatchAll: {_gigCfg.CatchAll}");

        if (fish == null || !fish.Enabled) {
            Service.PrintDebug($"[AutoGig] GigFish - Skipping (fish is null: {fish == null}, enabled: {fish?.Enabled ?? false})");
            return;
        }

        if (!Service.WorldState.HasStatus(IDs.Status.NaturesBounty) && fish.UseNaturesBounty)
            PlayerRes.CastActionDelayed(IDs.Actions.NaturesBounty);

        var centerX = _uiSize.X / 2;
        float fishHitbox = 0;

        // Im so tired of trying to figure this out someone help
        /*if (!info.InverseDirection)
            fishHitbox = (node->X * _uiScale) + (node->Width * node->ScaleX * _uiScale * 0.8f);
        else*/

        // did i fucking do it?
        if (info.InverseDirection)
            fishHitbox = node->X * _uiScale + node->Width * node->ScaleX * _uiScale * (0.5f + fish.RightOffset / 10);
        else
            fishHitbox = node->X * _uiScale + node->Width * node->ScaleX * _uiScale * (0.4f - fish.LeftOffset / 10);

        Service.PrintDebug($"[AutoGig] GigFish - Drawing hitbox at {fishHitbox}, centerX: {centerX}, gigHitbox: {gigHitbox}");
        DrawFishHitbox(addon, drawList, fishHitbox);

        if (fishHitbox >= (centerX - gigHitbox) && fishHitbox <= (centerX + gigHitbox)) {
            Service.PrintDebug("[AutoGig] GigFish - Fish in range, casting gig");
            _taskManager.Enqueue(() => { Chat.ExecuteCommand($"/ac \"{Gig}\""); });
        }
    }

    private BaseGig? CheckFish(AddonSpearFishing.FishInfo info) {
        Service.PrintDebug($"[AutoGig] CheckFish - currentNode: {currentNode}, Speed: {info.Speed}, Size: {info.Size}");

        var fishes = _gigCfg.SelectedPreset?.GetGigCurrentNode(currentNode);
        Service.PrintDebug($"[AutoGig] GetGigCurrentNode returned {fishes?.Count ?? 0} fish(es)");

        if (fishes is null || fishes.Count == 0) {
            Service.PrintDebug("[AutoGig] No fish found for current node");
            return null;
        }

        foreach (var f in fishes) {
            Service.PrintDebug($"[AutoGig] Checking fish: {f.Fish?.Name ?? "null"}, Enabled: {f.Enabled}, Fish.Speed: {f.Fish?.Speed}, Fish.Size: {f.Fish?.Size}");
        }

        var matched = fishes.FirstOrDefault(f => f.Fish != null && (short)f.Fish.Speed == info.Speed && f.Fish.Size == (Enums.SpearfishSize)info.Size); // TODO convert over properly
        Service.PrintDebug($"[AutoGig] Matched fish: {(matched != null ? matched.Fish?.Name ?? "null" : "none")}, Enabled: {matched?.Enabled ?? false}");

        return matched;
    }

    private BaseGig? GetCatchAllGig() {
        return new BaseGig(0) { Enabled = true, UseNaturesBounty = _gigCfg.CatchAllNaturesBounty };
    }

    private unsafe void DrawGigHitbox(AddonSpearFishing* addon, ImDrawListPtr drawList, int gigHitbox) {
        if (!_gigCfg.AutoGigDrawGigHitbox)
            return;

        var space = gigHitbox;

        var startX = _uiSize.X / 2;
        var fishLines = addon->GetNodeById(3);
        var centerY = fishLines->Y * _uiScale;
        var endY = fishLines->Height * _uiScale;

        //Hitbox left
        var lineStart = _uiPos + new Vector2(startX - space, centerY);
        var lineEnd = lineStart + new Vector2(0, endY);
        drawList.AddLine(lineStart, lineEnd, 0xFF0000C0, 1.Scaled());

        //Hitbox right
        lineStart = _uiPos + new Vector2(startX + space, centerY);
        lineEnd = lineStart + new Vector2(0, endY);
        drawList.AddLine(lineStart, lineEnd, 0xFF0000C0, 1.Scaled());
    }

    private unsafe void DrawFishHitbox(AddonSpearFishing* addon, ImDrawListPtr drawList, float fishHitbox) {
        Service.PrintDebug($"[AutoGig] DrawFishHitbox - AutoGigDrawFishHitbox: {_gigCfg.AutoGigDrawFishHitbox}, fishHitbox: {fishHitbox}");

        if (!_gigCfg.AutoGigDrawFishHitbox) {
            Service.PrintDebug("[AutoGig] DrawFishHitbox - Setting is disabled, not drawing");
            return;
        }

        var fishLines = addon->GetNodeById(3);
        var lineStart = _uiPos + new Vector2(fishHitbox, fishLines->Y * _uiScale);
        var lineEnd = lineStart + new Vector2(0, fishLines->Height * _uiScale);
        drawList.AddLine(lineStart, lineEnd, 0xFF20B020, 1.Scaled());
        Service.PrintDebug($"[AutoGig] DrawFishHitbox - Green line drawn at {fishHitbox}");
    }

    private bool _isOpen = false;

    public override unsafe bool DrawConditions() {
        var lastOpen = _isOpen;
        if (Svc.GameGui.TryGetAddon<AtkUnitBase>("Spearfishing", out _)) return false;

        if (!_isOpen)
            return false;

        if (_isOpen != lastOpen)
            SetFishTargets();

        return true;
    }

    private void SetFishTargets() {
        currentNode = 0;
        if (Svc.Targets.Target is { ObjectKind: ObjectKind.GatheringPoint, BaseId: var id })
            currentNode = (int)id;
    }

    public override unsafe void PreDraw() {
        if (Svc.GameGui.TryGetAddon<AtkUnitBase>("Spearfishing", out var addon)) return;
        _uiScale = addon->Scale;
        _uiPos = new Vector2(addon->X, addon->Y);
        _uiSize = new Vector2(addon->WindowNode->AtkResNode.Width * _uiScale,
            addon->WindowNode->AtkResNode.Height * _uiScale);

        Position = _uiPos;
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = _uiSize,
            MaximumSize = Vector2.One * 10000,
        };
    }
}
