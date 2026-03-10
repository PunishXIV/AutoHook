using AutoHook.Spearfishing.Struct;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons.Automation;
using ECommons.Automation.NeoTaskManager;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;

namespace AutoHook.Spearfishing;

internal class AutoGig : Window, IDisposable
{
    private const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.NoDecoration
                                                 | ImGuiWindowFlags.NoInputs
                                                 | ImGuiWindowFlags.AlwaysAutoResize
                                                 | ImGuiWindowFlags.NoFocusOnAppearing
                                                 | ImGuiWindowFlags.NoNavFocus
                                                 | ImGuiWindowFlags.NoBackground;

    private float _uiScale = 1;
    private Vector2 _uiPos = Vector2.Zero;
    private Vector2 _uiSize = Vector2.Zero;
    private unsafe SpearfishWindow* _addon = null;
    private bool checkForNullAddon = false;

    private int currentNode = 0;

    private readonly SpearFishingPresets _gigCfg = Service.Configuration.AutoGigConfig;

    public static string Gig = "Gig";

    private readonly TaskManager _taskManager = new()
    {
        DefaultConfiguration = { TimeLimitMS = 10000, ShowDebug = false }
    };

    public AutoGig() : base(@"SpearfishingHelper", WindowFlags, true)
    {
        Service.WindowSystem.AddWindow(this);
        IsOpen = true;
        Svc.Condition.ConditionChange += Condition_ConditionChange;
        Gig = MultiString.GetActionName(IDs.Actions.Gig);
    }

    private void Condition_ConditionChange(Dalamud.Game.ClientState.Conditions.ConditionFlag flag, bool value)
    {
        if (flag == (Dalamud.Game.ClientState.Conditions.ConditionFlag)85)
        {
            if (value)
                checkForNullAddon = false;
        }
    }

    public void Dispose()
    {
        Service.WindowSystem.RemoveWindow(this);
        Svc.Condition.ConditionChange -= Condition_ConditionChange;
        Service.Save();
    }

    public override void Draw()
    {
        if (!_gigCfg.AutoGigHideOverlay || _gigCfg.AutoGigEnabled)
            DrawFishOverlay();
    }

    public unsafe void DrawSettings()
    {
        if (ImGui.Checkbox(UIStrings.Enable_AutoGig, ref _gigCfg.AutoGigEnabled))
            Service.Save();

        var selectedPreset = _gigCfg.SelectedPreset;

        ImGui.SameLine();

        if (DrawUtil.Checkbox(UIStrings.CatchEverything, ref _gigCfg.CatchAll, UIStrings.IgnoresPresets))
            Service.Save();

        PluginUi.ShowKofi();

        DrawUtil.DrawComboSelector(
            _gigCfg.Presets,
            preset => preset.PresetName,
            _gigCfg.SelectedPreset?.PresetName ?? UIStrings.None,
            gig => _gigCfg.SelectedPreset = gig);

        ImGui.SetNextItemWidth(90);
        if (selectedPreset != null)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt(UIStrings.Hitbox + @" ", ref selectedPreset.HitboxSize))
            {
                selectedPreset.HitboxSize = Math.Max(0, Math.Min(selectedPreset.HitboxSize, 300));
                Service.Save();
            }
        }

        ImGui.SameLine();

        if (_gigCfg.CatchAll)
            ImGui.TextColored(ImGuiColors.DalamudYellow, UIStrings.CatchAllGigWindow);
    }

    private unsafe void DrawFishOverlay()
    {
        _addon = (SpearfishWindow*)Svc.GameGui.GetAddonByName("SpearFishing").Address;

        if (!checkForNullAddon && (_addon == null || _addon->Base.WindowNode == null))
        {
            if (_addon == null)
                Svc.Chat.PrintError($"AutoHook has detected a null addon whilst spearfishing. Please let us know in the Discord this happened.");

            if (_addon->Base.WindowNode == null)
                Svc.Chat.PrintError($"AutoHook has detected a null window whilst spearfishing. Please let us know in the Discord this happened.");

            checkForNullAddon = true;
            return;
        }

        bool isOpen = _addon != null && _addon->Base.WindowNode != null;

        if (!isOpen)
            return;

        ImGui.SetNextWindowPos(new Vector2(_addon->Base.X + 5, _addon->Base.Y - 65));
        if (ImGui.Begin("gig###gig", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
        {
            DrawSettings();
            ImGui.End();
        }

        if (_gigCfg is { AutoGigEnabled: true, })
        {
            if (!PlayerRes.HasStatus(IDs.Status.NaturesBounty) && _gigCfg.NatureBountyBeforeFish)
                PlayerRes.CastActionDelayed(IDs.Actions.NaturesBounty);

            GigFish(_addon->Fish1, _addon->Fish1Node);
            GigFish(_addon->Fish2, _addon->Fish2Node);
            GigFish(_addon->Fish3, _addon->Fish3Node);
        }
    }

    private unsafe void GigFish(SpearfishWindow.Info info, AtkResNode* node)
    {
        var drawList = ImGui.GetWindowDrawList();

        var gigHitbox = _gigCfg.SelectedPreset?.HitboxSize ?? 0;

        DrawGigHitbox(drawList, gigHitbox);

        if (_gigCfg.ThaliaksFavor.IsAvailableToCast())
            PlayerRes.CastActionDelayed(_gigCfg.ThaliaksFavor.Id, _gigCfg.ThaliaksFavor.ActionType,
                UIStrings.Thaliaks_Favor);

        if (!info.Available)
        {
            Service.PrintDebug("[AutoGig] GigFish - Fish not available");
            return;
        }

        var fish = _gigCfg.CatchAll ? GetCatchAllGig() : CheckFish(info);
        Service.PrintDebug($"[AutoGig] GigFish - fish: {(fish != null ? fish.Fish?.Name ?? "null" : "null")}, Enabled: {fish?.Enabled ?? false}, CatchAll: {_gigCfg.CatchAll}");

        if (fish == null || !fish.Enabled)
        {
            Service.PrintDebug($"[AutoGig] GigFish - Skipping (fish is null: {fish == null}, enabled: {fish?.Enabled ?? false})");
            return;
        }

        if (!PlayerRes.HasStatus(IDs.Status.NaturesBounty) && fish.UseNaturesBounty)
            PlayerRes.CastActionDelayed(IDs.Actions.NaturesBounty);

        var centerX = (_uiSize.X / 2);

        float fishHitbox = 0;

        // Im so tired of trying to figure this out someone help
        /*if (!info.InverseDirection)
            fishHitbox = (node->X * _uiScale) + (node->Width * node->ScaleX * _uiScale * 0.8f);
        else*/

        // did i fucking do it?
        if (info.InverseDirection)
            fishHitbox = (node->X * _uiScale) + (node->Width * node->ScaleX * _uiScale * (0.5f + (fish.RightOffset / 10)));
        else
            fishHitbox = (node->X * _uiScale) + (node->Width * node->ScaleX * _uiScale * (0.4f - (fish.LeftOffset / 10)));

        Service.PrintDebug($"[AutoGig] GigFish - Drawing hitbox at {fishHitbox}, centerX: {centerX}, gigHitbox: {gigHitbox}");
        DrawFishHitbox(drawList, fishHitbox);

        if (fishHitbox >= (centerX - gigHitbox) && fishHitbox <= (centerX + gigHitbox))
        {
            Service.PrintDebug("[AutoGig] GigFish - Fish in range, casting gig");
            _taskManager.Enqueue(() => { Chat.ExecuteCommand($"/ac \"{Gig}\""); });
        }
    }

    private BaseGig? CheckFish(SpearfishWindow.Info info)
    {
        Service.PrintDebug($"[AutoGig] CheckFish - currentNode: {currentNode}, Speed: {info.Speed}, Size: {info.Size}");

        var fishes = _gigCfg.SelectedPreset?.GetGigCurrentNode(currentNode);
        Service.PrintDebug($"[AutoGig] GetGigCurrentNode returned {fishes?.Count ?? 0} fish(es)");

        if (fishes is null || fishes.Count == 0)
        {
            Service.PrintDebug("[AutoGig] No fish found for current node");
            return null;
        }

        foreach (var f in fishes)
        {
            Service.PrintDebug($"[AutoGig] Checking fish: {f.Fish?.Name ?? "null"}, Enabled: {f.Enabled}, Fish.Speed: {f.Fish?.Speed}, Fish.Size: {f.Fish?.Size}");
        }

        var matched = fishes.FirstOrDefault(f =>
            f.Fish != null &&
            f.Fish.Speed == info.Speed &&
            f.Fish.Size == info.Size);
        Service.PrintDebug($"[AutoGig] Matched fish: {(matched != null ? matched.Fish?.Name ?? "null" : "none")}, Enabled: {matched?.Enabled ?? false}");

        return matched;
    }

    private BaseGig? GetCatchAllGig()
    {
        return new BaseGig(0) { Enabled = true, UseNaturesBounty = _gigCfg.CatchAllNaturesBounty };
    }

    private unsafe void DrawGigHitbox(ImDrawListPtr drawList, int gigHitbox)
    {
        if (!_gigCfg.AutoGigDrawGigHitbox)
            return;

        int space = gigHitbox;

        float startX = _uiSize.X / 2;
        float centerY = _addon->FishLines->Y * _uiScale;
        float endY = _addon->FishLines->Height * _uiScale;

        //Hitbox left
        var lineStart = _uiPos + new Vector2(startX - space, centerY);
        var lineEnd = lineStart + new Vector2(0, endY);
        drawList.AddLine(lineStart, lineEnd, 0xFF0000C0, 1 * ImGuiHelpers.GlobalScale);

        //Hitbox right
        lineStart = _uiPos + new Vector2(startX + space, centerY);
        lineEnd = lineStart + new Vector2(0, endY);
        drawList.AddLine(lineStart, lineEnd, 0xFF0000C0, 1 * ImGuiHelpers.GlobalScale);
    }

    private unsafe void DrawFishHitbox(ImDrawListPtr drawList, float fishHitbox)
    {
        Service.PrintDebug($"[AutoGig] DrawFishHitbox - AutoGigDrawFishHitbox: {_gigCfg.AutoGigDrawFishHitbox}, fishHitbox: {fishHitbox}");

        if (!_gigCfg.AutoGigDrawFishHitbox)
        {
            Service.PrintDebug("[AutoGig] DrawFishHitbox - Setting is disabled, not drawing");
            return;
        }

        var lineStart = _uiPos + new Vector2(fishHitbox, _addon->FishLines->Y * _uiScale);
        var lineEnd = lineStart + new Vector2(0, _addon->FishLines->Height * _uiScale);
        drawList.AddLine(lineStart, lineEnd, 0xFF20B020, 1 * ImGuiHelpers.GlobalScale);
        Service.PrintDebug($"[AutoGig] DrawFishHitbox - Green line drawn at {fishHitbox}");
    }

    private bool _isOpen = false;

    public override unsafe bool DrawConditions()
    {
        var lastOpen = _isOpen;

        _addon = (SpearfishWindow*)Svc.GameGui.GetAddonByName("SpearFishing").Address;
        _isOpen = _addon != null && _addon->Base.WindowNode != null;

        if (!_isOpen)
            return false;

        if (_isOpen != lastOpen)
            SetFishTargets();

        return true;
    }

    private void SetFishTargets()
    {
        currentNode = 0;
        if (Svc.Targets.Target is { ObjectKind: ObjectKind.GatheringPoint, BaseId: var id })
            currentNode = (int)id;
    }

    public override unsafe void PreDraw()
    {
        if (_addon is null) return;
        _uiScale = _addon->Base.Scale;
        _uiPos = new Vector2(_addon->Base.X, _addon->Base.Y);
        _uiSize = new Vector2(_addon->Base.WindowNode->AtkResNode.Width * _uiScale,
            _addon->Base.WindowNode->AtkResNode.Height * _uiScale);

        Position = _uiPos;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = _uiSize,
            MaximumSize = Vector2.One * 10000,
        };
    }
}
