using AutoHook.FishSolver;
using AutoHook.FishSolverIntegration;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoHook.Ui;

public class PresetCreator {
    private const string AutoV1Tag = "AutoV1";
    private const string AutoV2Tag = "AutoV2";

    private readonly FishingPresets Presets = Configuration.C.HookPresets;

    private string _newPresetName = "";
    private Fish? _selectedTargetFish;
    private List<Fish> _presetMoochList = [];
    private List<(Fish, int)> _presetPrepList = [];
    private bool _includeTimers;
    private bool _includeIntPrep;
    private bool _fishEyes;
    private bool _createAnglersPreset;
    private bool _sparefulHandPrep;

    private void DrawHeader() {
        using (ImRaii.TextWrapPos(0))
            ImGui.TextColored(ImGuiColors.DalamudYellow, "Experimental — leave feedback on Discord.");

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Fish");
        ImGui.SameLine(48.Scaled());
        DrawUtil.DrawComboSelector(FishBaitCatalog.Get().ImportedFishes.Where(f => !f.IsSpearFish).ToList(), item => item.Name, _selectedTargetFish?.Name ?? UIStrings.None, SetSelectedFish);

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Name");
        ImGui.SameLine(48.Scaled());
        ImGui.SetNextItemWidth(-1);
        var hint = _selectedTargetFish == null ? $"{AutoV1Tag}/{AutoV2Tag} - FishName" : $"{AutoV1Tag}/{AutoV2Tag} - {_selectedTargetFish.Name}";
        ImGui.InputTextWithHint("###presetName", hint, ref _newPresetName, 64, ImGuiInputTextFlags.AutoSelectAll);
    }

    private string ResolvePresetName(string versionTag) {
        if (!string.IsNullOrWhiteSpace(_newPresetName))
            return _newPresetName.Trim();
        var fish = _selectedTargetFish?.Name ?? "Preset";
        return $"{versionTag} - {fish} {DateTime.Now:yyyy-MM-dd HH:mm}";
    }

    private void SetSelectedFish(Fish fish) {
        ResetOptions();
        _selectedTargetFish = fish;
    }

    private void ResetOptions() {
        _newPresetName = string.Empty;
        _selectedTargetFish = null;
        _includeTimers = false;
        _includeIntPrep = false;
        _fishEyes = false;
        _createAnglersPreset = false;
        _sparefulHandPrep = false;
        _presetMoochList = [];
        _presetPrepList = [];
    }

    public void DrawPresetGenerator() {
        try {
            DrawHeader();

            if (_selectedTargetFish == null)
                return;

            if (_presetMoochList.Count == 0 && _selectedTargetFish.Mooches.Count > 0)
                _presetMoochList = ResolveMoochFish(_selectedTargetFish.Mooches);

            if (_presetPrepList.Count == 0 && _selectedTargetFish.Predators.Count > 0) {
                foreach (var predator in _selectedTargetFish.Predators) {
                    var fish = FishBaitCatalog.Get().ImportedFishes.FirstOrDefault(f => f.Id == predator.ItemId);
                    if (fish != null)
                        _presetPrepList.Add((fish, predator.Quantity));
                }
            }

            ImGui.Spacing();
            DrawAutoV2Section();
            ImGui.Spacing();
            DrawAutoV1Section();
        }
        catch (Exception e) {
            IPluginLog.Get().Error(e, "[PresetCreator] Draw failed.");
        }
    }

    private static void DrawSectionHeader(string title) {
        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudOrange, title);
        ImGui.Separator();
    }

    private void DrawAutoV2Section() {
        using var _ = ImRaii.PushId("AutoV2");

        DrawSectionHeader("Auto V2");
        if (!FishBaitCatalog.Get().FishSolver.IsLoaded) {
            ImGui.TextColored(ImGuiColors.DalamudRed, "FishSolver data not loaded.");
            return;
        }

        var ws = WorldState.Get();
        var cordials = FishSolverBridge.ReadCordialInventory(ws.Player.GetItemCount);
        var fisherLevel = IPlayerState.Get().GetClassJobLevel(ClassJob.GetRow(18));
        var plan = FishBaitCatalog.Get().FishSolver.Solve(
            _selectedTargetFish!.Id,
            fisherLevel,
            (int)ws.Player.MaxGp,
            cordials);

        if (plan == null) {
            ImGui.TextColored(ImGuiColors.DalamudYellow, "No plan for this fish.");
            return;
        }

        ImGui.Text($"{plan.Archetype} · {plan.HoldMode} · {plan.RouteVariant}");
        ImGui.TextDisabled($"Arrive early: {DurationFormat.MinutesSeconds(plan.ArriveEarlySeconds)}");

        if (plan.MissingSkillsFallbacks.Count > 0) {
            using (ImRaii.TextWrapPos(0))
                ImGui.TextColored(ImGuiColors.DalamudYellow, string.Join("; ", plan.MissingSkillsFallbacks));
        }

        if (plan.PrepPhase.Steps.Count > 0 && ImGui.TreeNode("Prep steps")) {
            foreach (var step in plan.PrepPhase.Steps)
                ImGui.BulletText($"{step.Action} {step.FishId?.ToString() ?? ""} ({step.EstimatedSeconds}s)");
            ImGui.TreePop();
        }

        if (ImGui.Button("Generate Auto V2", new Vector2(-1, 0)))
            GeneratePresetFromSolver();
    }

    private void DrawAutoV1Section() {
        using var _ = ImRaii.PushId("AutoV1");

        DrawSectionHeader("Auto V1");
        using (ImRaii.TextWrapPos(0))
            ImGui.TextDisabled("Legacy generator");

        var tackleBait = ResolveTackleBait(_selectedTargetFish!, _presetMoochList);
        ImGui.Text($"Bait: {Item.GetRow((uint)tackleBait).Name}");

        if (_presetMoochList.Count > 0)
            ImGui.TextWrapped($"Mooch: {string.Join(" > ", _presetMoochList.Select(f => $"{f.Name} {GetBiteType(f.BiteType)}"))}");

        DrawUtil.Checkbox("Include fish hooking timers", ref _includeTimers, "The values are based on the info available on TeamCraft and are not 100% accurate");

        if (_selectedTargetFish!.Predators.Count > 0) {
            DrawUtil.Checkbox("Include intuition prep in same preset", ref _includeIntPrep, "WEven more experimental, works well with 1 fish requirement but 2 or more idk about that (will be improved)");

            if (_includeIntPrep) {
                ImGui.Indent();
                foreach (var (fish, qty) in _presetPrepList) {
                    var baitName = Item.GetRow((uint)ResolveTackleBait(fish, ResolveMoochFish(fish.Mooches))).Name;
                    ImGui.BulletText($"{qty}x {fish.Name} {GetBiteType(fish.BiteType)} ({baitName})");
                }
                ImGui.Unindent();
            }
        }

        DrawUtil.Checkbox("Setup Auto Casting for Fish Eyes", ref _fishEyes, "This is a simple setup, useful for catching old expansions big fishes");

        if (_fishEyes && _presetMoochList.Count > 0) {
            ImGui.Indent();
            ImGui.TextColored(ImGuiColors.DalamudYellow, "Mooch fish — start with 10 Angler's Art for Makeshift Bait.");
            DrawUtil.Checkbox("Also create Angler's Art stacking preset (versatile lure)", ref _createAnglersPreset);
            ImGui.Unindent();
        }

        if (FishBaitCatalog.Get().MoochableFish.Any(f => f.Id == _selectedTargetFish.Id))
            DrawUtil.Checkbox("Create Spareful Hand prep preset", ref _sparefulHandPrep, "Catch 3 -> swimbait, catch 4th, stop");

        if (ImGui.Button("Generate Auto V1", new Vector2(-1, 0)))
            GeneratePreset(_presetMoochList, _presetPrepList);
    }

    private void GeneratePresetFromSolver() {
        if (_selectedTargetFish == null || !FishBaitCatalog.Get().FishSolver.IsLoaded)
            return;

        var ws = WorldState.Get();
        var cordials = FishSolverBridge.ReadCordialInventory(ws.Player.GetItemCount);
        var presetName = ResolvePresetName(AutoV2Tag);
        var fisherLevel = IPlayerState.Get().GetClassJobLevel(ClassJob.GetRow(18));

        var preset = FishBaitCatalog.Get().FishSolver.BuildPreset(_selectedTargetFish.Id, fisherLevel, (int)ws.Player.MaxGp, presetName, cordials);
        if (preset == null) {
            IPluginLog.Get().Debug("[FishSolver] Failed to build preset.");
            return;
        }

        Presets.RegisterPreset(preset);
        IPluginLog.Get().Information($"[FishSolver] Created preset '{preset.PresetName}'");
        TabFishingPresets.OpenPresetGen = false;
    }

    private void GeneratePreset(List<Fish> moochList, List<(Fish, int)> prepList) {
        if (_selectedTargetFish == null)
            return;

        CustomPresetConfig newPreset;
        CustomPresetConfig? anglersPreset = null;
        var presetName = ResolvePresetName(AutoV1Tag);

        var isInt = _includeIntPrep && prepList.Count > 0;

        newPreset = new CustomPresetConfig(presetName);
        var tackleBait = ResolveTackleBait(_selectedTargetFish, moochList);

        SetupBaitAndMooch(newPreset, tackleBait, _selectedTargetFish, moochList, isInt);

        newPreset.ExtraCfg.Enabled = true;
        newPreset.ExtraCfg.ForceBaitSwap = true;
        newPreset.ExtraCfg.ForcedBaitId = tackleBait;

        if (_includeIntPrep) {
            SetupIntPrep(newPreset, prepList);
            SetupIntuitionBaitSwapRules(newPreset, moochList, prepList);
        }

        if (_fishEyes)
            SetupFishEyes(newPreset);
        else {
            ref var ac = ref newPreset.AutoCastsCfg;
            ac.EnableAll = true;
            ac.CastLine.Enabled = true;
            ac.CastCordial.Enabled = true;
            ac.CastCollect.Enabled = Item.GetRow((uint)_selectedTargetFish.Id).IsCollectable;

            if (moochList.Count > 0) {
                ac.CastPatience.Enabled = true;
                newPreset.AutoCastsCfg.CastMakeShiftBait.Enabled = true;
            }
            else if (Item.GetRow((uint)_selectedTargetFish.Id).IsCollectable) {
                ac.CastPatience.Enabled = true;
            }
        }

        if (_sparefulHandPrep)
            SetupSparefulHandPrep(newPreset);
        else
            newPreset.AddItem(new FishConfig(_selectedTargetFish.Id));

        if (_createAnglersPreset) {
            anglersPreset = CreateAnglerPreset();
            anglersPreset.ExtraCfg.Enabled = true;
            anglersPreset.ExtraCfg.Triggers.Add(new ExtraTrigger {
                Enabled = true,
                ConditionSet = Configuration.ConditionSetBuilder.SingleStatusStacks(IDs.Status.AnglersArt, 10),
                SwapPreset = true,
                PresetToSwap = newPreset.PresetName,
                SwapBait = true,
                BaitToSwap = new BaitFishClass(newPreset.ExtraCfg.ForcedBaitId),
                StopAction = ExtraStopAction.None,
            });
        }

        if (anglersPreset != null)
            Presets.RegisterPreset(anglersPreset, select: false);

        Presets.RegisterPreset(newPreset, select: false);
        ResetOptions();

        TabFishingPresets.OpenPresetGen = false;
    }

    private void SetupFishEyes(CustomPresetConfig newPreset) {
        if (_selectedTargetFish == null)
            return;

        newPreset.AutoCastsCfg.EnableAll = true;
        newPreset.AutoCastsCfg.CastLine.Enabled = true;
        newPreset.AutoCastsCfg.CastLine.ConditionSet = Configuration.ConditionSetBuilder.SingleStatus(IDs.Status.FishEyes);
        newPreset.AutoCastsCfg.CastCordial.Enabled = true;
        newPreset.AutoCastsCfg.CastFishEyes.Enabled = true;
        newPreset.AutoCastsCfg.CastFishEyes.DontCancelMooch = false;

        if (_selectedTargetFish!.Mooches.Count > 0) {
            newPreset.AutoCastsCfg.CastFishEyes.ConditionSet = Configuration.ConditionSetBuilder.SingleStatus(IDs.Status.MakeshiftBait);
            newPreset.AutoCastsCfg.CastPatience.Enabled = true;
            newPreset.AutoCastsCfg.CastPatience.Id = IDs.Actions.Patience;
            newPreset.AutoCastsCfg.CastPatience.GpThreshold = 770;
            newPreset.AutoCastsCfg.CastMakeShiftBait.Enabled = true;
        }
    }

    private void SetupIntPrep(CustomPresetConfig newPreset, List<(Fish, int)> prepList) {
        foreach (var fishPrep in prepList) {
            var fish = fishPrep.Item1;
            var mooches = ResolveMoochFish(fish.Mooches);
            var tackleBait = ResolveTackleBait(fish, mooches);

            SetupBaitAndMooch(newPreset, tackleBait, fish, mooches);
            newPreset.AddItem(new FishConfig(fishPrep.Item1.Id));
        }
    }

    private void SetupIntuitionBaitSwapRules(CustomPresetConfig newPreset, List<Fish> targetMoochList, List<(Fish, int)> prepList) {
        if (_selectedTargetFish == null || prepList.Count == 0)
            return;

        var prepFish = prepList[0].Item1;
        var prepMooches = ResolveMoochFish(prepFish.Mooches);
        var targetBait = ResolveTackleBait(_selectedTargetFish, targetMoochList);
        var prepBait = ResolveTackleBait(prepFish, prepMooches);

        newPreset.ExtraCfg.Triggers.Add(new ExtraTrigger {
            Enabled = true,
            ConditionSet = Configuration.ConditionSetBuilder.SingleFlag<IntuitionActiveCD>(),
            SwapBait = true,
            BaitToSwap = new BaitFishClass(targetBait),
        });

        newPreset.ExtraCfg.Triggers.Add(new ExtraTrigger {
            Enabled = true,
            ConditionSet = Configuration.ConditionSetBuilder.SingleFlag<IntuitionActiveCD>(inverse: true),
            SwapBait = true,
            BaitToSwap = new BaitFishClass(prepBait),
        });

        newPreset.ExtraCfg.ForcedBaitId = prepBait;
    }

    private void SetupBaitAndMooch(CustomPresetConfig newPreset, int bait, Fish fishTarget, List<Fish>? moochList,
        bool isIntuition = false) {
        var initBaitCfg = newPreset.ListOfBaits.FirstOrDefault(f => f.BaitFish.Id == bait);

        if (initBaitCfg == null) {
            initBaitCfg = new HookConfig(bait);
            initBaitCfg.ResetAllHooksets();
        }

        if (isIntuition)
            initBaitCfg.IntuitionHook.UseCustomStatusHook = true;

        // if theres no mooch, set the bait to hook the Tug from the target fish
        if (moochList == null || moochList.Count == 0) {
            initBaitCfg.SetBiteAndHookType(fishTarget.BiteType, fishTarget!.HookType, isIntuition);

            if (fishTarget.IsLureFish) {
                var actionId = fishTarget!.HookType == HookType.Powerful ? IDs.Actions.AmbitiousLure : IDs.Actions.ModestLure;
                initBaitCfg.NormalHook.CastLures.ConfigureSpecialLure(actionId, Configuration.ConditionSetBuilder.SingleStatus(IDs.Status.PrizeCatch));
            }

            if (_includeTimers) {
                initBaitCfg.SetHooksetTimer(fishTarget.BiteType, fishTarget.BiteTimeMin, fishTarget.BiteTimeMax, isIntuition);
            }

            newPreset.ReplaceBaitConfig(initBaitCfg);
            return;
        }

        foreach (var mooch in moochList) {
            // check if the mooch is already included in the list
            var newMooch = newPreset.ListOfMooch.FirstOrDefault(f => f.BaitFish.Id == mooch.Id);

            if (newMooch == null) {
                newMooch = new HookConfig(mooch.Id);
                newMooch.ResetAllHooksets();
            }

            if (isIntuition)
                newMooch.IntuitionHook.UseCustomStatusHook = true;

            // Add the fish to the Fish Caught tab and enable Auto Mooch I/II
            var fishConfig = new FishConfig(mooch.Id);
            fishConfig.Mooch.Enabled = true;
            fishConfig.Mooch.Mooch2.Enabled = true;
            newPreset.AddItem(fishConfig);

            var nextFish = mooch == moochList.First() ? fishTarget : mooch == moochList.Last() ? moochList[^2] : moochList[moochList.IndexOf(mooch) - 1];

            // Mooches are ordered from the target backwards toward the tackle bait:
            // target fish < first mooch < other mooches < last mooch < bait.
            // The bait hooks the last mooch, while each mooch hooks the fish before it.

            // only hook the next fish BiteType
            newMooch.SetBiteAndHookType(nextFish.BiteType, nextFish.HookType, isIntuition);

            if (_includeTimers) {
                newMooch.SetHooksetTimer(nextFish.BiteType, nextFish.BiteTimeMin, nextFish.BiteTimeMax, isIntuition);
            }

            newPreset.ReplaceMoochConfig(newMooch);

            // The last fish in the list is the first one caught from the tackle bait.
            if (mooch == moochList.Last()) {
                // that means we need to set up the bait to the this fish bite.
                initBaitCfg.SetBiteAndHookType(mooch.BiteType, mooch.HookType, isIntuition);
                if (_includeTimers) {
                    initBaitCfg.SetHooksetTimer(mooch.BiteType, mooch.BiteTimeMin, mooch.BiteTimeMax, isIntuition);
                }

                newPreset.ReplaceBaitConfig(initBaitCfg);
            }
        }
    }

    private CustomPresetConfig CreateAnglerPreset() {
        CustomPresetConfig anglers = new($"{AutoV1Tag} - StackAngler {DateTime.Now:yyyy-MM-dd HH:mm}");

        var bait = new HookConfig(29717); // versatile lure

        anglers.ExtraCfg.Enabled = true;
        anglers.ExtraCfg.ForceBaitSwap = true;
        anglers.ExtraCfg.ForcedBaitId = 29717;

        anglers.AutoCastsCfg.EnableAll = true;
        anglers.AutoCastsCfg.CastLine.Enabled = true;
        anglers.AutoCastsCfg.CastPatience.Enabled = true;
        anglers.AutoCastsCfg.CastCordial.Enabled = true;
        anglers.AutoCastsCfg.DontCancelMooch = false;

        anglers.AddItem(bait);

        return anglers;
    }

    private static List<Fish> ResolveMoochFish(IEnumerable<int> moochIds)
        => [.. moochIds.Select(id => FishBaitCatalog.Get().ImportedFishes.FirstOrDefault(f => f.Id == id)).OfType<Fish>()];

    private static int ResolveTackleBait(Fish target, List<Fish> moochList)
        => moochList.Count > 0 ? moochList[^1].InitialBait : target.InitialBait;

    private static string GetBiteType(BiteType bite)
        => bite switch {
            BiteType.Weak => "(!)",
            BiteType.Strong => "(!!)",
            BiteType.Legendary => "(!!!)",
            _ => "Error",
        };

    private void SetupSparefulHandPrep(CustomPresetConfig newPreset) {
        if (_selectedTargetFish == null)
            return;

        var tackleBait = ResolveTackleBait(_selectedTargetFish, _presetMoochList);
        var initBaitCfg = newPreset.ListOfBaits.FirstOrDefault(f => f.BaitFish.Id == tackleBait);

        if (initBaitCfg == null) {
            initBaitCfg = new HookConfig(tackleBait);
            initBaitCfg.ResetAllHooksets();
        }

        initBaitCfg.SetBiteAndHookType(_selectedTargetFish.BiteType, _selectedTargetFish.HookType, false);

        if (_includeTimers) {
            initBaitCfg.SetHooksetTimer(_selectedTargetFish.BiteType, _selectedTargetFish.BiteTimeMin, _selectedTargetFish.BiteTimeMax, false);
        }

        newPreset.ReplaceBaitConfig(initBaitCfg);

        ref var ac = ref newPreset.AutoCastsCfg;
        ac.EnableAll = true;
        ac.CastLine.Enabled = true;
        ac.CastCordial.Enabled = true;
        ac.CastCollect.Enabled = Item.GetRow((uint)_selectedTargetFish.Id).IsCollectable;

        var fishConfig = new FishConfig(_selectedTargetFish.Id);

        fishConfig.SparefulHand.Enabled = true;
        fishConfig.SparefulHand.FishIdToCheck = (uint)_selectedTargetFish.Id;
        fishConfig.SparefulHand.ConditionSet = Configuration.ConditionSetBuilder.SwimbaitCount(3, "<", _selectedTargetFish.Id) is { } cond
            ? new ConditionSet {
                CombineMode = ConditionCombineMode.All,
                Groups = [new ConditionGroup { CombineMode = ConditionCombineMode.All, Conditions = [cond] }],
            }
            : null;

        fishConfig.StopAfterCaughtLimit.Value = (true, 4);

        newPreset.AddItem(fishConfig);
    }
}
