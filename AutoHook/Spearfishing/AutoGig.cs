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
    private const uint FishLaneNodeId = 43;
    private const uint Fish1NodeId = 61;
    private const uint Fish2NodeId = 60;
    private const uint Fish3NodeId = 59;

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
            GigFish(addon, addon->Fish[0], addon->GetNodeById(Fish1NodeId));
            GigFish(addon, addon->Fish[1], addon->GetNodeById(Fish2NodeId));
            GigFish(addon, addon->Fish[2], addon->GetNodeById(Fish3NodeId));
        }
    }

    private unsafe void GigFish(AddonSpearFishing* addon, AddonSpearFishing.FishInfo info, AtkResNode* node) {
        if (node == null)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var gigHitbox = _gigCfg.SelectedPreset?.HitboxSize ?? 0;
        var fishLines = addon->GetNodeById(FishLaneNodeId);
        if (fishLines == null)
            return;

        DrawGigHitbox(fishLines, drawList, gigHitbox);

        if (_gigCfg.ThaliaksFavor.IsAvailableToCast())
            PlayerRes.CastActionDelayed(_gigCfg.ThaliaksFavor.Id, _gigCfg.ThaliaksFavor.ActionType,
                UIStrings.Thaliaks_Favor);

        if (!info.Available)
            return;

        var fish = _gigCfg.CatchAll ? GetCatchAllGig() : CheckFish(info);

        if (fish == null || !fish.Enabled)
            return;

        if (!Service.WorldState.HasStatus(IDs.Status.NaturesBounty) && fish.UseNaturesBounty)
            PlayerRes.CastActionDelayed(IDs.Actions.NaturesBounty);

        var laneOriginX = fishLines->X * _uiScale;
        var centerX = laneOriginX + fishLines->Width * fishLines->ScaleX * _uiScale / 2f;
        var anchor = info.InverseDirection
            ? 0.5f + fish.RightOffset / 10
            : 0.4f - fish.LeftOffset / 10;
        var fishHitbox = laneOriginX + node->X * _uiScale + node->Width * node->ScaleX * _uiScale * anchor;

        DrawFishHitbox(fishLines, drawList, fishHitbox);

        if (fishHitbox >= centerX - gigHitbox && fishHitbox <= centerX + gigHitbox)
            _taskManager.Enqueue(() => { Chat.ExecuteCommand($"/ac \"{Gig}\""); });
    }

    private BaseGig? CheckFish(AddonSpearFishing.FishInfo info) {
        var fishes = _gigCfg.SelectedPreset?.GetGigCurrentNode(currentNode);

        if (fishes is null || fishes.Count == 0)
            return null;

        return fishes.FirstOrDefault(f => f.Fish != null && (short)f.Fish.Speed == info.Speed && f.Fish.Size == (Enums.SpearfishSize)info.Size);
    }

    private BaseGig? GetCatchAllGig() {
        return new BaseGig(0) { Enabled = true, UseNaturesBounty = _gigCfg.CatchAllNaturesBounty };
    }

    private unsafe void DrawGigHitbox(AtkResNode* fishLines, ImDrawListPtr drawList, int gigHitbox) {
        if (!_gigCfg.AutoGigDrawGigHitbox)
            return;

        var laneOriginX = fishLines->X * _uiScale;
        var startX = laneOriginX + fishLines->Width * fishLines->ScaleX * _uiScale / 2f;
        var centerY = fishLines->Y * _uiScale;
        var endY = fishLines->Height * _uiScale;

        var lineStart = _uiPos + new Vector2(startX - gigHitbox, centerY);
        var lineEnd = lineStart + new Vector2(0, endY);
        drawList.AddLine(lineStart, lineEnd, 0xFF0000C0, 1.Scaled());

        lineStart = _uiPos + new Vector2(startX + gigHitbox, centerY);
        lineEnd = lineStart + new Vector2(0, endY);
        drawList.AddLine(lineStart, lineEnd, 0xFF0000C0, 1.Scaled());
    }

    private unsafe void DrawFishHitbox(AtkResNode* fishLines, ImDrawListPtr drawList, float fishHitbox) {
        if (!_gigCfg.AutoGigDrawFishHitbox)
            return;

        var lineStart = _uiPos + new Vector2(fishHitbox, fishLines->Y * _uiScale);
        var lineEnd = lineStart + new Vector2(0, fishLines->Height * _uiScale);
        drawList.AddLine(lineStart, lineEnd, 0xFF20B020, 1.Scaled());
    }

    private bool _isOpen = false;

    public override unsafe bool DrawConditions() {
        var lastOpen = _isOpen;

        if (!Svc.GameGui.TryGetAddon<AtkUnitBase>("SpearFishing", out var addon)) {
            _isOpen = false;
            return false;
        }

        _isOpen = addon->WindowNode != null;

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
        if (!Svc.GameGui.TryGetAddon<AtkUnitBase>("SpearFishing", out var addon)) return;
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
