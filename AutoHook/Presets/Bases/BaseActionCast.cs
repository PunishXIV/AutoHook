using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.ComponentModel;
using System.Numerics;

namespace AutoHook.Presets.Bases;

public abstract class BaseActionCast {
    private const int ActionControlColumn = 200;
    private const int GpButtonWidth = 55;

    protected BaseActionCast(uint id, ActionType actionType = ActionType.Action) {
        Id = id;
        Enabled = false;

        ActionType = actionType;

        if (actionType == ActionType.Action && id is not IDs.Actions.ThaliaksFavor and not IDs.Actions.NaturesBounty and not IDs.Actions.Collect
            and not IDs.Actions.BaitedBreath and not IDs.Actions.ElectricCurrent and not IDs.Actions.VeteranTrade
            and not IDs.Actions.VitalSight)
            GpThreshold = (int)ActionExecutor.GetActionCost(Id, ActionType);
    }

    public bool Enabled;

    public uint Id;

    public int GpThreshold;

    [NonSerialized] public bool IsSpearFishing;

    [DefaultValue(true)]
    public bool GpThresholdAbove { get; set; } = true;

    public virtual bool DoesCancelMooch() => false;

    [DefaultValue(true)]
    public bool DontCancelMooch = true;

    public virtual bool RequiresAutoCastAvailable() => false;

    public virtual bool RequiresTimeWindow() => false;

    public virtual bool RestoresGp => false;
    public virtual bool ShowGpThreshold => true;

    public virtual int Priority { get; set; }

    [NonSerialized] public ActionType ActionType;

    public ConditionSet? ConditionSet { get; set; }

    protected bool EvaluateConditionSet(WorldState ws)
        => ConditionSet.PassesOrUnconfigured(ws);

    protected void DrawAutoCastConditions(bool showSubPrefix = true)
        => ConditionSet = ConditionUi.DrawConditionSet(UIStrings.Conditions, ConditionSet, IsSpearFishing ? ConditionScope.Spearfishing : ConditionScope.AutoCast, showAdvanced: true, showSubPrefix: showSubPrefix);

    public void DrawFishCaughtActionOptions()
        => DrawAutoCastConditions(showSubPrefix: false);

    public virtual void SetThreshold(int newCost) {
        GpThreshold = (newCost < 0) ? 0 : Math.Max(newCost, GetActionCost());
        Configuration.Save();
    }

     private int CurrentThresholdValue(WorldState ws)
        => CostsAnglersArt ? ws.Player.GetStatusStacks(IDs.Status.AnglersArt) : (int)ws.Player.CurrentGp;

    private string ThresholdUnit => CostsAnglersArt ? "AA" : "GP";

    public bool IsAvailableToCast(WorldState ws, bool ignoreCurrentMooch = false)
        => DescribeUnavailable(ws, ignoreCurrentMooch) == null;

    // null if castable; else short reason for replay decision log.
    public string? DescribeUnavailable(WorldState ws, bool ignoreCurrentMooch = false) {
        if (!Enabled)
            return "Disabled";

        if (DoesCancelMooch() && ws.IsMoochAvailable() && DontCancelMooch && !ignoreCurrentMooch)
            return "Would cancel mooch";

        if (RestoresGp && ws.Player.HasPendingGp)
            return "GP pending";

        var condition = CastCondition(ws);
        var current = CurrentThresholdValue(ws);
           var hasGp = !ShowGpThreshold || (GpThresholdAbove ? current >= GpThreshold : current <= GpThreshold);
        var actionAvailable = ws.ActionAvailable(Id, ActionType);

        if (!condition) {
            if (ConditionSet != null && !ConditionSet.PassesOrUnconfigured(ws))
                return "Condition set failed";
            return "Cast conditions not met";
        }

        if (!hasGp)
            return GpThresholdAbove ? $"{ThresholdUnit} {current} < {GpThreshold}" : $"{ThresholdUnit} {current} > {GpThreshold}";

        if (!actionAvailable)
            return "Action not available";

        return null;
    }

    public bool IsGpBlocked(WorldState ws, bool ignoreCurrentMooch = false) {
        if (!Enabled || !ShowGpThreshold || CostsAnglersArt)
            return false;

        if (DoesCancelMooch() && ws.IsMoochAvailable() && DontCancelMooch && !ignoreCurrentMooch)
            return false;

        if (!CastCondition(ws))
            return false;

        if (!ws.ActionAvailable(Id, ActionType))
            return false;

        var currentGp = ws.Player.CurrentGp;
        return GpThresholdAbove ? currentGp < GpThreshold : currentGp > GpThreshold;
    }

    public abstract bool CastCondition(WorldState ws);

    public abstract string GetName();

    public virtual string GetHelpText() => "";

    public virtual int GetPriority() => Priority;

    protected delegate void DrawOptionsDelegate();

    protected virtual DrawOptionsDelegate? DrawOptions => null;

    public abstract bool IsExcludedPriority { get; set; }

    public virtual void DrawConfig(List<BaseActionCast>? availableActs = null)
        => DrawConfigWithLabel(GetName(), availableActs);

    public void DrawConfigWithLabel(string label, List<BaseActionCast>? availableActs = null) {
        using var cfgId = ImRaii.PushId(@$"{GetType().Name}_cfg");

        if (DrawOptions != null) {
            DrawUtil.DrawCheckboxTree(label, ref Enabled, () => DrawOptions?.Invoke(), GetHelpText(),
                drawLabelExtras: () => {
                    if (ShowGpThreshold) {
                        ImGui.SameLine(ActionControlColumn.Scaled());
                        DrawGpThreshold();
                    }
                    DrawUpDownArrows(availableActs);
                });
        }
        else {
            DrawUtil.Checkbox(@$"###{GetType().Name}", ref Enabled, GetHelpText(), true);

            ImGui.SameLine(0, 28.Scaled());
            ImGui.Text(label);
            if (ShowGpThreshold) {
                ImGui.SameLine(ActionControlColumn.Scaled());
                DrawGpThreshold();
            }
            DrawUpDownArrows(availableActs);
        }
    }

    public virtual void DrawConfigOptions() {
        DrawOptions?.Invoke();
    }

    private void DrawUpDownArrows(List<BaseActionCast>? availableActs) {
        if (availableActs is null || IsExcludedPriority) return;

        if (GetPriority() == 0) //failsafe I guess
            Priority = availableActs.MaxBy(x => x.Priority)!.Priority + 1;

        if (ShowGpThreshold)
            ImGui.SameLine();
        else
            ImGui.SameLine(ActionControlColumn.Scaled() + GpButtonWidth.Scaled() + ImGui.GetStyle().ItemSpacing.X);

        var canMoveUp = availableActs.Any(x => x.Priority < Priority && !x.IsExcludedPriority);
        using (ImRaii.Disabled(!canMoveUp)) {
            if (ImGui.ArrowButton(@"###UpArrow", ImGuiDir.Up)) {
                var nextAct = availableActs.Where(x => x.Priority < Priority && !x.IsExcludedPriority)
                    .OrderByDescending(x => x.Priority).First();
                nextAct.Priority = Priority;
                Priority--;
                Configuration.Save();
            }
        }

        ImGui.SameLine();

        var canMoveDown = availableActs.Any(x => x.Priority > Priority && !x.IsExcludedPriority);
        using (ImRaii.Disabled(!canMoveDown)) {
            if (ImGui.ArrowButton(@"###DownArrow", ImGuiDir.Down)) {
                var lastAct = availableActs.Where(x => x.Priority > Priority && !x.IsExcludedPriority)
                    .OrderBy(x => x.Priority).First();
                lastAct.Priority = Priority;
                Priority++;
                Configuration.Save();
            }
        }
    }

    private int GetActionCost()
        => (int)ActionExecutor.GetActionCost(Id, ActionType);

    private bool CostsAnglersArt
        => ActionType == ActionType.Action && Id is IDs.Actions.MakeshiftBait or IDs.Actions.BigGameFishing or IDs.Actions.MultiHook or IDs.Actions.ThaliaksFavor;

     private bool IsDefaultGpThreshold(int actionCost)
        => GpThresholdAbove && GpThreshold <= actionCost;

     protected virtual int? AnglersArtStacksToUse => null;

    private string DescribeGpThreshold(int actionCost) {
        if (CostsAnglersArt) {
            // own stack option has to pass too.
            var minStacks = AnglersArtStacksToUse ?? actionCost;
            var when = IsDefaultGpThreshold(actionCost) ? $"equal or above {minStacks}"
                : GpThresholdAbove ? $"equal or above {Math.Max(minStacks, GpThreshold)}"
                : $"equal or above {minStacks} and equal or below {GpThreshold}";
            return $"{GetName()} will be used when your Angler's Art stacks are {when}\nCosts {actionCost}";
        }

        if (IsDefaultGpThreshold(actionCost))
            return actionCost > 0
                ? $"{GetName()} {UIStrings.WillBeUsedWhenYourGPIsEqualOr} {UIStrings.Above} {actionCost}"
                : $"{GetName()}: no GP requirement";

        var text = @$"{GetName()} {UIStrings.WillBeUsedWhenYourGPIsEqualOr} {(GpThresholdAbove ? UIStrings.Above : UIStrings.Below)} {GpThreshold}";
        return actionCost > 0 ? $"{text}\nGP cost: {actionCost}" : text;
    }

    public virtual void DrawGpThreshold() {
        using var gpId = ImRaii.PushId(@$"{GetType().Name}_gp");
        var actionCost = GetActionCost();
        var buttonWidth = GpButtonWidth.Scaled();
        bool clicked;
        if (IsDefaultGpThreshold(actionCost))
            clicked = ImGui.Button($"{actionCost}###gp_btn", new Vector2(buttonWidth, 0));
        else
            clicked = DrawUtil.IconTextButton(@"gp_btn",
                GpThresholdAbove ? FontAwesomeIcon.CaretUp : FontAwesomeIcon.CaretDown,
                $"{GpThreshold}", buttonWidth, ImGuiColors.DalamudOrange);

        ImGui.TooltipOnHover(DescribeGpThreshold(actionCost));

        if (clicked)
            ImGui.OpenPopup(strId: @"gp_cfg");

        using var popup = ImRaii.Popup(@"gp_cfg");
        if (!popup.Success) return;

        ImGui.Dummy(new Vector2(175.Scaled(), 0));
        if (ImGui.Button(@" X "))
            ImGui.CloseCurrentPopup();
        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.DalamudYellow, @$"{ThresholdUnit} - {GetName()}");

        ImGui.TooltipOnHover(DescribeGpThreshold(actionCost));

        ImGui.Separator();
        if (ImGui.RadioButton(UIStrings.Above, GpThresholdAbove)) {
            GpThresholdAbove = true;
            Configuration.Save();
        }

        //ImGui.SameLine();

        if (ImGui.RadioButton(UIStrings.Below, !GpThresholdAbove)) {
            GpThresholdAbove = false;
            Configuration.Save();
        }

        //ImGui.SameLine();

        var threshold = Math.Max(GpThreshold, actionCost);
        ImGui.SetNextItemWidth(100.Scaled());
        if (ImGui.InputInt(CostsAnglersArt ? @"AA" : UIStrings.GP, ref threshold, 1, 1)) {
            SetThreshold(Math.Max(threshold, 0));
            Configuration.Save();
        }
    }
}
