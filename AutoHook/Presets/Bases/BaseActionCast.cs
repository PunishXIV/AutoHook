using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.ComponentModel;
using System.Numerics;

namespace AutoHook.Presets.Bases;

public abstract class BaseActionCast {
    private const int ActionControlColumn = 200;

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

    protected void DrawAutoCastConditions()
        => ConditionSet = ConditionUi.DrawConditionSet(UIStrings.Conditions, ConditionSet, IsSpearFishing ? ConditionScope.Spearfishing : ConditionScope.AutoCast, showAdvanced: true);

    public void DrawFishCaughtActionOptions()
        => DrawAutoCastConditions();

    public virtual void SetThreshold(int newCost) {
        var actionCost = Id == IDs.Actions.ThaliaksFavor ? 0 : (int)ActionExecutor.GetActionCost(Id, ActionType);
        GpThreshold = (newCost < 0) ? 0 : Math.Max(newCost, actionCost);
        Configuration.Save();
    }

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
        var currentGp = ws.Player.CurrentGp;
        var hasGp = GpThresholdAbove ? currentGp >= GpThreshold : currentGp <= GpThreshold;
        var actionAvailable = ws.ActionAvailable(Id, ActionType);

        if (!condition) {
            if (ConditionSet != null && !ConditionSet.PassesOrUnconfigured(ws))
                return "Condition set failed";
            return "Cast conditions not met";
        }

        if (!hasGp)
            return GpThresholdAbove ? $"GP {currentGp} < {GpThreshold}" : $"GP {currentGp} > {GpThreshold}";

        if (!actionAvailable)
            return "Action not available";

        return null;
    }

    public bool IsGpBlocked(WorldState ws, bool ignoreCurrentMooch = false) {
        if (!Enabled)
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

        ImGui.SameLine();

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

    public virtual void DrawGpThreshold() {
        using var gpId = ImRaii.PushId(@$"{GetType().Name}_gp");
        if (ImGui.Button(UIStrings.GPlabel)) {
            ImGui.OpenPopup(strId: @"gp_cfg");
        }

        using var popup = ImRaii.Popup(@"gp_cfg");
        if (!popup.Success) return;

        using var item = ImRaii.Child("###gp_cfg2", new Vector2(175.Scaled(), 125.Scaled()), true);
        if (ImGui.Button(@" X "))
            ImGui.CloseCurrentPopup();
        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.DalamudYellow, @$"GP - {GetName()}");

        ImGui.TooltipOnHover(@$"{GetName()} {UIStrings.WillBeUsedWhenYourGPIsEqualOr} {(GpThresholdAbove ? UIStrings.Above : UIStrings.Below)} {GpThreshold}");

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

        ImGui.SetNextItemWidth(100.Scaled());
        if (ImGui.InputInt(UIStrings.GP, ref GpThreshold, 1, 1)) {
            GpThreshold = Math.Max(GpThreshold, 0);
            SetThreshold(GpThreshold);
            Configuration.Save();
        }
    }
}
