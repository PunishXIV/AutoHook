namespace AutoHook.Modules.Rod;

public sealed class ExtraComponent(RodFishingModule module) : RodComponent(module) {
    private const int PresetSwapCap = 8;

    public ExtraConfig GetExtraCfg()
        => RodFishingModule.Presets.SelectedPreset?.ExtraCfg.Enabled ?? false ? RodFishingModule.Presets.SelectedPreset.ExtraCfg : RodFishingModule.Presets.DefaultPreset.ExtraCfg;

    public CustomPresetConfig? FindPresetByName(string presetName)
        => PresetSwapHelpers.FindPresetByName(presetName);

    private CustomPresetConfig GetExtraOwnerPreset()
        => RodFishingModule.Presets.SelectedPreset?.ExtraCfg.Enabled == true ? RodFishingModule.Presets.SelectedPreset : RodFishingModule.Presets.DefaultPreset;

    private bool ExtraSwapStillNeeded(ExtraTrigger trig) {
        if (trig.SwapPreset && !string.IsNullOrEmpty(trig.PresetToSwap) && trig.PresetToSwap != @"-" && RodFishingModule.Presets.SelectedPreset?.PresetName != trig.PresetToSwap)
            return true;
        return trig.SwapBait && trig.BaitToSwap.Id > 0 && Ws.Fishing.BaitInfo.BaitId != trig.BaitToSwap.Id;
    }

    public void QueueResolveCollectables() {
        var extraCfg = GetExtraCfg();
        foreach (var trig in extraCfg.Triggers) {
            if (trig is not { Enabled: true, ResolveCollectablesWindow: true, ConditionSet: not null })
                continue;

            if (!trig.ConditionSet.Evaluate(Ws, Registry))
                continue;

            AutoCollectables.Get().RequestResolve(trig.ResolveCollectablesForceNo);
            return;
        }
    }

    public void ProcessExtraActions(ActionHints hints, ActionHintsResolver resolver) {
        var anyPresetSwapped = false;
        var involvedPresetIds = new HashSet<Guid>();
        var iterations = 0;

        try {
            while (true) {
                if (++iterations > PresetSwapCap) {
                    SwapLoopBailout(involvedPresetIds);
                    break;
                }

                Ws.Execute(new RodState.OpClearFishingStepFlag(FishingSteps.PresetSwapped));
                Ws.Execute(new RodState.OpClearFishingStepFlag(FishingSteps.BaitSwapped));

                involvedPresetIds.Add(GetExtraOwnerPreset().UniqueId);

                var extraCfg = GetExtraCfg();
                if (extraCfg.Triggers.Count == 0)
                    break;

                hints.SideEffects.Clear();
                ProposeExtraTriggerSideEffects(hints, extraCfg);

                // apply effects, and if unique id changed, loop re-checking the new extracfg
                var presetChanged = resolver.ApplySideEffects(Ws, hints, Rod);
                if (!presetChanged)
                    break;

                anyPresetSwapped = true;
                involvedPresetIds.Add(GetExtraOwnerPreset().UniqueId);
            }

            if (anyPresetSwapped)
                Ws.Execute(new RodState.OpSetFishingStep(FishingSteps.PresetSwapped, Or: true));
        }
        finally {
            SettleIntuitionEdges();
        }
    }

    private void SettleIntuitionEdges() {
        var cur = Ws.Fishing.Intuition;
        if (cur.Status == IntuitionStatus.Gained)
            Ws.Execute(new RodState.OpIntuition(new IntuitionInfo(IntuitionStatus.Active, cur.TimeRemaining)));
        else if (cur.Status == IntuitionStatus.Lost)
            Ws.Execute(new RodState.OpIntuition(new IntuitionInfo(IntuitionStatus.NotActive, 0f)));
    }

    private void SwapLoopBailout(HashSet<Guid> involvedPresetIds) {
        var involvedNames = EnumerateHookPresets().Where(p => involvedPresetIds.Contains(p.UniqueId)).Select(p => p.PresetName).ToList();

        Configuration.C.PluginEnabled = false;
        Configuration.Save();

        var presetList = involvedNames.Count > 0 ? string.Join(", ", involvedNames) : UIStrings.UnknownPresets;
        IChatGui.Get().PrintStatus(string.Format(UIStrings.Extra_PresetSwapLoop_Bailout, presetList));
    }

    private static IEnumerable<CustomPresetConfig> EnumerateHookPresets() {
        yield return RodFishingModule.Presets.DefaultPreset;
        foreach (var preset in RodFishingModule.Presets.CustomPresets)
            yield return preset;
    }

    private void ProposeExtraTriggerSideEffects(ActionHints hints, ExtraConfig extraCfg) {
        for (var i = 0; i < extraCfg.Triggers.Count; i++) {
            if (extraCfg.Triggers[i] is not { Enabled: true, ConditionSet: not null } trig)
                continue;

            var current = trig.ConditionSet.Evaluate(Ws, Registry);
            var last = i < extraCfg.LastTriggerStates.Count && extraCfg.LastTriggerStates[i];
            var fire = current && (!last || ExtraSwapStillNeeded(trig));

            if (i < extraCfg.LastTriggerStates.Count)
                extraCfg.LastTriggerStates[i] = current;
            else
                extraCfg.LastTriggerStates.Add(current);
            if (!fire)
                continue;

            Ws.Decide(DecisionContext.Extra, true, trig.GetRuleLabel(i), JoinDetail(trig.DescribeActions(), trig.ConditionSet.Describe()), GetExtraOwnerPreset().PresetName);
            AddExtraTriggerSideEffects(hints, trig);
        }
    }

    private static string? JoinDetail(params string?[] parts) {
        var joined = string.Join("\n", parts.Where(p => !string.IsNullOrEmpty(p)));
        return string.IsNullOrEmpty(joined) ? null : joined;
    }

    private static void AddExtraTriggerSideEffects(ActionHints hints, ExtraTrigger trig) {
        if (trig.StopAction != ExtraStopAction.None)
            hints.AddSideEffect(new StopFishingHint(trig.StopAction));

        if (trig.ResetFishCaughtCounter)
            hints.AddSideEffect(new ResetCounterHint());

        if (trig.SwapPreset)
            hints.AddSideEffect(new SwapPresetHint(trig.PresetToSwap));

        if (trig.SwapBait)
            hints.AddSideEffect(new SwapBaitHint(trig.BaitToSwap));

        if (trig.RemoveStatus && trig.StatusToRemove != 0)
            hints.AddSideEffect(new RemoveStatusHint(trig.StatusToRemove));

        if (trig.StartFishing)
            hints.AddSideEffect(new StartFishingHint());

        if (trig.ReduceFish)
            hints.AddSideEffect(new StartReductionHint());

        hints.AddSideEffect(new NotifyHint(trig.NotifyOnSuccess, "Rule condition success"));
    }
}
