using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.ComponentModel;

namespace AutoHook.Presets.AutoCasts;

public readonly record struct LureActiveOption(uint ActionId, uint StatusId, LureTarget Target, LureTargetConfig Config);

public class LureTypeConfig {
    public bool Enabled;
    public LureTargetConfig Any = new();
    public LureTargetConfig Special = new();
    public LureTargetConfig NotSpecial = new();

    public IEnumerable<(LureTarget Target, LureTargetConfig Config)> EnumerateTargets() {
        yield return (LureTarget.Any, Any);
        yield return (LureTarget.Special, Special);
        yield return (LureTarget.NotSpecial, NotSpecial);
    }
}

public class LureTargetConfig {
    public bool Enabled;

    [DefaultValue(3)]
    public int LureStacks = 3;

    public bool CancelAttempt;
    public bool ForceAttemptLimit;
    public ConditionSet? ConditionSet { get; set; }

    public bool ConditionsPass(WorldState ws) => ConditionSet.PassesOrUnconfigured(ws);
}

public sealed class AutoLures : BaseActionCast {
    public LureTypeConfig Ambitious = new();
    public LureTypeConfig Modest = new();

    public AutoLures() : base(IDs.Actions.AmbitiousLure) { }

    public override string GetName() => UIStrings.UseLures;

    public LureActiveOption? GetActiveOption(WorldState ws) {
        foreach (var option in EnumerateValidOptions(ws))
            return option;
        return null;
    }

    public int CountValidOptions(WorldState ws) => EnumerateValidOptions(ws).Count();

    private IEnumerable<LureActiveOption> EnumerateValidOptions(WorldState ws) {
        foreach (var (type, actionId, statusId) in EnumerateLureTypes()) {
            if (!type.Enabled)
                continue;

            foreach (var (target, config) in type.EnumerateTargets()) {
                if (!config.Enabled || !config.ConditionsPass(ws))
                    continue;

                yield return new LureActiveOption(actionId, statusId, target, config);
            }
        }
    }

    private IEnumerable<(LureTypeConfig Type, uint ActionId, uint StatusId)> EnumerateLureTypes() {
        yield return (Ambitious, IDs.Actions.AmbitiousLure, IDs.Status.AmbitiousLure);
        yield return (Modest, IDs.Actions.ModestLure, IDs.Status.ModestLure);
    }

    public static bool MatchesLureSuccess(LureTarget target, bool isGenericLure, bool isSpecialLure) => target switch {
        LureTarget.Any => isGenericLure || isSpecialLure,
        LureTarget.Special => isSpecialLure,
        LureTarget.NotSpecial => isGenericLure,
        _ => false
    };

    public void ConfigureSpecialLure(uint actionId, ConditionSet? conditionSet = null) {
        Enabled = true;
        var isAmbitious = actionId == IDs.Actions.AmbitiousLure;
        var type = isAmbitious ? Ambitious : Modest;
        type.Enabled = true;
        type.Special.Enabled = true;
        type.Special.CancelAttempt = true;
        type.Special.ConditionSet = conditionSet;
        Id = actionId;
    }

    public override bool CastCondition(WorldState ws) {
        var option = GetActiveOption(ws);
        if (option == null)
            return false;

        Id = option.Value.ActionId;

        if (ws.Player.GetStatusStacks(option.Value.StatusId) >= option.Value.Config.LureStacks)
            return false;

        return ws.Fishing.FishingState is FishingState.AmbitiousLure or FishingState.ModestLure or FishingState.LineInWater;
    }

    protected override DrawOptionsDelegate DrawOptions => () => {
        if (CountValidOptions(WorldState.Get()) > 1) {
            DrawUtil.Branch();
            ImGui.TextColored(ImGuiColors.DalamudYellow, UIStrings.LureMultipleOptionsWarning);
            ImGui.Spacing();
        }

        DrawLureType(UIStrings.AmbitiousLure, Ambitious);
        DrawLureType(UIStrings.ModestLure, Modest);
    };

    private static void DrawLureType(string label, LureTypeConfig type) {
        using var id = ImRaii.PushId(label);
        DrawUtil.DrawCheckboxTree(label, ref type.Enabled, () => {
            DrawTarget(UIStrings.AnyTarget, type.Any);
            DrawTarget(UIStrings.OnlySpecial, type.Special, $"{UIStrings.SpecialFishExemple} {FishBaitCatalog.Get().LureFishes.FirstOrDefault()?.Name}");
            DrawTarget(UIStrings.NotSpecial, type.NotSpecial);
        });
    }

    private static void DrawTarget(string label, LureTargetConfig config, string infoText = "") {
        using var id = ImRaii.PushId(label);

        DrawUtil.DrawCheckboxTree(label, ref config.Enabled, () => DrawTargetOptions(config),
            drawLabelExtras: string.IsNullOrEmpty(infoText) ? null : () => {
                ImGui.SameLine();
                DrawUtil.Info(infoText);
            });
    }

    private static void DrawTargetOptions(LureTargetConfig config) {
        var stack = config.LureStacks;
        if (DrawUtil.EditNumberField(UIStrings.MaxAttempts, ref stack, "", 1)) {
            config.LureStacks = Math.Clamp(stack, 1, 3);
            Configuration.Save();
        }

        ImGui.SameLine();
        DrawUtil.Checkbox(UIStrings.ForceAttemptLimit, ref config.ForceAttemptLimit, UIStrings.ForceAttemptLimitHelp);

        DrawUtil.Checkbox(UIStrings.CancelAttempt, ref config.CancelAttempt);

        config.ConditionSet = ConditionUi.DrawConditionSet(UIStrings.Conditions, config.ConditionSet, ConditionScope.AutoCast, showAdvanced: true);
    }

    public void TryCasting(WorldState ws, bool lureSuccess) {
        if (!EzThrottler.Check("CastingLure"))
            return;

        var option = GetActiveOption(ws);
        if (option == null)
            return;

        var config = option.Value.Config;
        var stacks = ws.Player.GetStatusStacks(option.Value.StatusId);

        var rod = FishingSessionManager.Get().Rod;

        if (stacks >= config.LureStacks && config.CancelAttempt && !lureSuccess) {
            rod.Enqueue(new ActionRequest(IDs.Actions.Rest));
            return;
        }

        if (lureSuccess && !config.ForceAttemptLimit)
            return;

        Id = option.Value.ActionId;

        if (!IsAvailableToCast(ws))
            return;

        if (!rod.Enqueue(new ActionRequest(Id, ActionType.Action, GetName(), ActionDelayMode.NoDelay)))
            return;

        EzThrottler.Throttle("CastingLure", 2500);
    }

    public override int Priority { get; set; } = 0;
    [DefaultValue(true)]
    public override bool IsExcludedPriority { get; set; } = true;
}
