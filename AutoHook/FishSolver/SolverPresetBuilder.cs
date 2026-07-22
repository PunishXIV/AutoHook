using AutoHook.FishSolver.Engine;
using AutoHook.FishSolver.Models;
using Lumina.Excel.Sheets;

namespace AutoHook.FishSolverIntegration;

public static class SolverPresetBuilder {
    public static CustomPresetConfig? Build(SolverOutput plan, string? presetName = null) {
        using var _ = Configuration.SuppressSave();
        var target = FindFish(plan.TargetFishId);
        if (target == null)
            return null;

        var moochList = ResolveMoochFish(target.Mooches);
        var tackleBait = moochList.Count > 0 ? moochList[^1].InitialBait : plan.BaitId;
        if (tackleBait <= 0)
            tackleBait = target.InitialBait;

        var name = presetName;
        if (string.IsNullOrWhiteSpace(name))
            name = $"Solver - {target.Name} ({plan.Archetype})";

        var preset = new CustomPresetConfig(name);
        var isIntuition = target.Predators.Count > 0;
        var slapFish = plan.SlapTargetFishId is { } slapId ? FindFish(slapId) : null;

        if (isIntuition) {
            SetupIntuitionPrep(preset, target, plan);
            if (moochList.Count > 0)
                SetupIntuitionMoochTarget(preset, target, moochList, plan);
            else
                ConfigureTargetIntuitionBait(preset, target, moochList, plan);
        }
        else {
            SetupBaitAndMooch(preset, tackleBait, target, moochList, isIntuition: false, slapFish, plan);
            ApplyCastPolicy(preset, tackleBait, target, plan);
        }

        preset.ExtraCfg.Enabled = true;
        preset.ExtraCfg.ForceBaitSwap = true;
        preset.ExtraCfg.ForcedBaitId = isIntuition && target.Predators.Count > 0
            ? ResolveStartingPrepBait(target)
            : tackleBait;

        if (isIntuition)
            SetupIntuitionBaitSwapRules(preset, target, moochList, plan);

        SetupAutoCasts(preset, target, moochList, plan);
        SetupFishCaughtActions(preset, target, plan, slapFish);

        return preset;
    }

    private static void SetupFishCaughtActions(CustomPresetConfig preset, ImportedFish target, SolverOutput plan, ImportedFish? slapFish) {
        if (slapFish != null) {
            var slapCfg = AddFishConfig(preset, slapFish.ItemId);
            slapCfg.SurfaceSlap.Enabled = true;
        }

        if (plan.HoldMode == PrepHoldMode.IdenticalCastZeroTime
            || plan.Archetype == StrategyArchetype.IntuitionRebuild) {
            if (target.Predators.Count == 2)
                ConfigureDualPredatorIcSlap(preset, target);
            else {
                var prepBait = ResolveStartingPrepBait(target);
                foreach (var predator in target.Predators) {
                    var triggerCfg = AddFishConfig(preset, predator.ItemId);
                    if (prepBait > 0)
                        ConfigureSinglePredatorIcSlap(triggerCfg, predator.ItemId, predator.Quantity, prepBait);
                }
            }
        }
        ConfigureIntuitionMoochFish(preset, target, plan);
        if (plan.HoldMode == PrepHoldMode.SwimbaitBank) {
            SetupSwimbaitBank(preset, target, plan);
            return;
        }

        AddFishConfig(preset, target.ItemId);
    }

    private static void SetupSwimbaitBank(CustomPresetConfig preset, ImportedFish target, SolverOutput plan) {
        var bankCount = plan.PrepPhase.Steps
            .FirstOrDefault(s => s.Action == PrepActionKind.SwimbaitSelect)?.Count ?? 3;

        var fishConfig = AddFishConfig(preset, target.ItemId);
        fishConfig.SparefulHand.Enabled = true;
        fishConfig.SparefulHand.FishIdToCheck = (uint)target.ItemId;
        fishConfig.SparefulHand.ConditionSet = Configuration.ConditionSetBuilder.SwimbaitCount(bankCount, "<", target.ItemId) is { } cond
            ? new Conditions.ConditionSet {
                CombineMode = Conditions.ConditionCombineMode.All,
                Groups = [new Conditions.ConditionGroup { CombineMode = Conditions.ConditionCombineMode.All, Conditions = [cond] }],
            } : null;
        fishConfig.StopAfterCaughtLimit.Value = (true, bankCount + 1);
    }

    private static void SetupIntuitionPrep(CustomPresetConfig preset, ImportedFish target, SolverOutput plan) {
        var useSharedPrepBait = target.Predators.Count > 1
            && (plan.HoldMode == PrepHoldMode.IdenticalCastZeroTime
                || plan.Archetype == StrategyArchetype.IntuitionRebuild);
        if (useSharedPrepBait) {
            ConfigureIntuitionPrepBaits(preset, target);
        }
        else {
            foreach (var predator in target.Predators) {
                var fish = FindFish(predator.ItemId);
                if (fish == null)
                    continue;
                var bait = ResolveTackleBait(fish, ResolveMoochFish(fish.Mooches));
                if (bait > 0)
                    ConfigurePrepBait(preset, bait, [fish]);
            }
        }
        foreach (var predator in target.Predators) {
            var fish = FindFish(predator.ItemId);
            if (fish == null)
                continue;
            AddFishConfig(preset, fish.ItemId);
        }
    }
    private static void ConfigureIntuitionPrepBaits(CustomPresetConfig preset, ImportedFish target) {
        var spotId = target.SpotIds.FirstOrDefault();
        var predators = target.Predators
            .Select(p => FindFish(p.ItemId))
            .Where(f => f != null)
            .Cast<ImportedFish>()
            .ToList();
        if (predators.Count == 0)
            return;
        var baitToFish = new Dictionary<int, List<ImportedFish>>();
        foreach (var fish in predators) {
            foreach (var baitId in ResolveBaitsForFishAtSpot(fish, spotId)) {
                if (!baitToFish.TryGetValue(baitId, out var list)) {
                    list = [];
                    baitToFish[baitId] = list;
                }
                if (list.All(f => f.ItemId != fish.ItemId))
                    list.Add(fish);
            }
        }
        // always include the starting / force-only baits
        var startBait = ResolveStartingPrepBait(target);
        var catchBait = ResolveIntuitionCatchBait(target);
        EnsureBaitHasFish(baitToFish, startBait, predators);
        if (catchBait > 0 && catchBait != startBait)
            EnsureBaitHasFish(baitToFish, catchBait, [.. predators.Where(f => IsMoochPredator(target, f.ItemId))]);
        foreach (var (baitId, fishOnBait) in baitToFish) {
            if (baitId <= 0 || fishOnBait.Count == 0)
                continue;
            ConfigurePrepBait(preset, baitId, fishOnBait);
            EnableIntuitionSwimbaitUse(preset, baitId);
        }
    }
    private static void EnsureBaitHasFish(Dictionary<int, List<ImportedFish>> baitToFish, int baitId, List<ImportedFish> candidates) {
        if (baitId <= 0 || candidates.Count == 0)
            return;
        if (!baitToFish.TryGetValue(baitId, out var list)) {
            list = [];
            baitToFish[baitId] = list;
        }
        foreach (var fish in candidates) {
            if (list.All(f => f.ItemId != fish.ItemId))
                list.Add(fish);
        }
    }
    private static bool IsMoochPredator(ImportedFish target, int fishId)
        => target.Mooches.Contains(fishId) || target.Predators.Any(p => p.ItemId == fishId && target.Mooches.Contains(p.ItemId));
    private static void EnableIntuitionSwimbaitUse(CustomPresetConfig preset, int baitId) {
        var baitCfg = preset.ListOfBaits.FirstOrDefault(f => f.BaitFish.Id == baitId);
        if (baitCfg == null)
            return;
        baitCfg.SwimbaitIntuition.UseSwimbait = true;
    }
    private static void SetupIntuitionMoochTarget(CustomPresetConfig preset, ImportedFish target, List<ImportedFish> moochList, SolverOutput plan) {
        var catchBait = ResolveIntuitionCatchBait(target);
        if (catchBait <= 0)
            catchBait = ResolveTackleBait(target, moochList);
        // Intuition catch bait: only hook the mooch fish bite
        var moochFish = moochList[^1];
        var baitCfg = preset.ListOfBaits.FirstOrDefault(f => f.BaitFish.Id == catchBait) ?? new HookConfig(catchBait);
        baitCfg.IntuitionHook.UseCustomStatusHook = true;
        baitCfg.SetBiteAndHookType(moochFish.BiteType, moochFish.HookType, isIntuition: true);
        baitCfg.SetHooksetTimer(moochFish.BiteType, moochFish.BiteTimeMin, moochFish.BiteTimeMax, isIntuition: true);
        baitCfg.SwimbaitIntuition.UseSwimbait = true;
        ApplyLurePolicy(baitCfg.IntuitionHook, plan);
        ApplyEarlyCancel(baitCfg.IntuitionHook, plan);
        preset.ReplaceBaitConfig(baitCfg);
        NarrowPrepBaitsToMoochBiteUnderIntuition(preset, target, moochFish);
        SetupBaitAndMooch(preset, catchBait, target, moochList, isIntuition: true, slapFish: null, plan);
    }
    private static void NarrowPrepBaitsToMoochBiteUnderIntuition(CustomPresetConfig preset, ImportedFish target, ImportedFish moochFish) {
        var catchBait = ResolveIntuitionCatchBait(target);
        foreach (var baitCfg in preset.ListOfBaits) {
            if (baitCfg.BaitFish.Id == catchBait)
                continue;
            baitCfg.IntuitionHook.UseCustomStatusHook = true;
            baitCfg.SetBiteAndHookType(moochFish.BiteType, moochFish.HookType, isIntuition: true);
            baitCfg.SetHooksetTimer(moochFish.BiteType, moochFish.BiteTimeMin, moochFish.BiteTimeMax, isIntuition: true);
            baitCfg.SwimbaitIntuition.UseSwimbait = true;
        }
    }

    private static void ConfigureTargetIntuitionBait(CustomPresetConfig preset, ImportedFish target, List<ImportedFish> moochList, SolverOutput plan) {
        var targetBait = ResolveTackleBait(target, moochList);
        var baitCfg = preset.ListOfBaits.FirstOrDefault(f => f.BaitFish.Id == targetBait) ?? new HookConfig(targetBait);
        baitCfg.ResetAllHooksets();
        baitCfg.IntuitionHook.UseCustomStatusHook = true;
        baitCfg.SetBiteAndHookType(target.BiteType, target.HookType, isIntuition: true);
        baitCfg.SetHooksetTimer(target.BiteType, target.BiteTimeMin, target.BiteTimeMax, isIntuition: true);
        ApplyLurePolicy(baitCfg.IntuitionHook, plan);
        ApplyEarlyCancel(baitCfg.IntuitionHook, plan);

        preset.ReplaceBaitConfig(baitCfg);
    }

    private static void ConfigurePrepBait(CustomPresetConfig preset, int baitId, IReadOnlyList<ImportedFish> triggerFish) {
        if (triggerFish.Count == 0)
            return;
        var baitCfg = preset.ListOfBaits.FirstOrDefault(f => f.BaitFish.Id == baitId) ?? new HookConfig(baitId);
        baitCfg.ResetAllHooksets();
        foreach (var fish in triggerFish) {
            baitCfg.SetBiteAndHookType(fish.BiteType, fish.HookType, isIntuition: false);
            baitCfg.SetHooksetTimer(fish.BiteType, fish.BiteTimeMin, fish.BiteTimeMax, isIntuition: false);
        }
        preset.ReplaceBaitConfig(baitCfg);
    }

    private static void SetupIntuitionBaitSwapRules(CustomPresetConfig preset, ImportedFish target, List<ImportedFish> targetMoochList, SolverOutput plan) {
        if (target.Predators.Count == 0)
            return;

        AddPostWindowStopTrigger(preset, target);

        var targetBait = ResolveIntuitionCatchBait(target);
        if (targetBait <= 0)
            targetBait = ResolveTackleBait(target, targetMoochList);
        preset.ExtraCfg.Triggers.Add(new ExtraTrigger {
            Enabled = true,
            ConditionSet = Configuration.ConditionSetBuilder.All(
                Configuration.ConditionSetBuilder.IntuitionActive(),
                Configuration.ConditionSetBuilder.CurrentBait(targetBait, inverse: true)),
            SwapBait = true,
            BaitToSwap = new BaitFishClass(targetBait),
        });

        if (target.Predators.Count > 1)
            SetupMultiPredatorPrepSwaps(preset, target);
        else
            SetupSinglePredatorPrepSwap(preset, target);
    }

    private static void SetupSinglePredatorPrepSwap(CustomPresetConfig preset, ImportedFish target) {
        var prepFish = FindFish(target.Predators[0].ItemId);
        if (prepFish == null)
            return;

        var prepBait = ResolveTackleBait(prepFish, ResolveMoochFish(prepFish.Mooches));
        preset.ExtraCfg.Triggers.Add(new ExtraTrigger {
            Enabled = true,
            ConditionSet = Configuration.ConditionSetBuilder.All(
                Configuration.ConditionSetBuilder.IntuitionActive(inverse: true),
                Configuration.ConditionSetBuilder.CurrentBait(prepBait, inverse: true)),
            SwapBait = true,
            BaitToSwap = new BaitFishClass(prepBait),
        });
    }

    private static void SetupMultiPredatorPrepSwaps(CustomPresetConfig preset, ImportedFish target) {
        var resolved = target.Predators
            .Select(p => FindFish(p.ItemId) is { } fish ? (Predator: p, Fish: fish) : default)
            .Where(x => x.Fish != null)
            .ToList();
        if (resolved.Count < 2)
            return;
        var startBait = ResolveStartingPrepBait(target);
        var catchBait = ResolveIntuitionCatchBait(target);
        if (startBait <= 0)
            return;
        // When non-mooch predators are done, swap to the force-only mooch bait (e.g. Metal Spinner)
        if (catchBait > 0 && catchBait != startBait) {
            var nonMooch = resolved.Where(r => !target.Mooches.Contains(r.Fish.ItemId)).ToList();
            if (nonMooch.Count > 0) {
                var conditions = new List<Conditions.Condition> {
                    Configuration.ConditionSetBuilder.IntuitionActive(inverse: true),
                    Configuration.ConditionSetBuilder.CurrentBait(catchBait, inverse: true),
                };
                foreach (var r in nonMooch)
                    conditions.Add(Configuration.ConditionSetBuilder.FishCount(r.Fish.ItemId, r.Predator.Quantity));
                preset.ExtraCfg.Triggers.Add(new ExtraTrigger {
                    Enabled = true,
                    ConditionSet = Configuration.ConditionSetBuilder.All([.. conditions]),
                    SwapBait = true,
                    BaitToSwap = new BaitFishClass(catchBait),
                });
            }
        }
        var allComplete = resolved.Select(r => Configuration.ConditionSetBuilder.FishCount(r.Fish.ItemId, r.Predator.Quantity)).ToList();
        var completeConditions = new List<Conditions.Condition> {
            Configuration.ConditionSetBuilder.IntuitionActive(inverse: true),
            Configuration.ConditionSetBuilder.CurrentBait(startBait, inverse: true),
        };
        completeConditions.AddRange(allComplete);
        preset.ExtraCfg.Triggers.Add(new ExtraTrigger {
            Enabled = true,
            ConditionSet = Configuration.ConditionSetBuilder.All([.. completeConditions]),
            SwapBait = true,
            BaitToSwap = new BaitFishClass(startBait),
            ResetFishCaughtCounter = true,
        });
    }

    private static void AddPostWindowStopTrigger(CustomPresetConfig preset, ImportedFish target) {
        if (!TryParseFishingWindowEnd(target.Time, out var windowEnd))
            return;

        var stopEnd = windowEnd.AddHours(1);
        preset.ExtraCfg.Triggers.Add(new ExtraTrigger {
            Enabled = true,
            ConditionSet = Configuration.ConditionSetBuilder.SingleTimeWindow(windowEnd, stopEnd),
            StopAction = ExtraStopAction.QuitFishing,
        });
    }

    private static void SetupAutoCasts(CustomPresetConfig preset, ImportedFish target, List<ImportedFish> moochList, SolverOutput plan) {
        ref var ac = ref preset.AutoCastsCfg;
        ac.EnableAll = true;
        ac.CastLine.Enabled = true;
        ac.CastCordial.Enabled = plan.ResourcePolicy.UseCordials;
        ac.CastCollect.Enabled = Item.GetRow((uint)target.ItemId).IsCollectable;
        ac.CastSnagging.Enabled = target.Snagging;

        var intuitionPrep = target.Predators.Count > 0;
        var longIntuitionRebuild = plan.Archetype == StrategyArchetype.IntuitionRebuild
            || plan.HoldMode == PrepHoldMode.IdenticalCastZeroTime;

        if (intuitionPrep && longIntuitionRebuild) {
            ConfigurePreWindowResourceCasts(ref ac, target, plan, moochList);
            // Intuition + mooch: Makeshift for AA, skip Thaliak's.
            if (moochList.Count > 0) {
                ac.CastMakeShiftBait.Enabled = true;
                ac.CastThaliaksFavor.Enabled = false;
            }
            else if (plan.PlayerProfileUsed.Skills.ThaliaksFavor) {
                ac.CastThaliaksFavor.Enabled = true;
            }
            ac.CastChum.Enabled = false;
        }
        else {
            ac.CastChum.Enabled = plan.ResourcePolicy.UseChum;
            if (moochList.Count > 0 || plan.HoldMode == PrepHoldMode.MoochHold) {
                ac.CastPatience.Enabled = true;
                ac.CastMakeShiftBait.Enabled = true;
            }
            else if (Item.GetRow((uint)target.ItemId).IsCollectable) {
                ac.CastPatience.Enabled = true;
            }
        }
    }

    private static void ConfigurePreWindowResourceCasts(
        ref AutoCastsConfig ac,
        ImportedFish target,
        SolverOutput plan,
        List<ImportedFish> moochList) {
        if (!TryParseFishingWindow(target.Time, out var windowStart, out var windowEnd))
            return;
        var gpMax = plan.PlayerProfileUsed.GpMax;
        ac.CastPatience.Enabled = true;
        // Prep: outside window + AA not capped. Window: Patience only while Mooch II is on CD
        if (moochList.Count > 0) {
            ac.CastPatience.ConditionSet = new Conditions.ConditionSet {
                CombineMode = Conditions.ConditionCombineMode.Any,
                Expression = "A || B",
                Groups = [
                    new Conditions.ConditionGroup {
                        CombineMode = Conditions.ConditionCombineMode.All,
                        Conditions = [
                            TimeWindowCondition(windowStart, windowEnd, invert: true),
                            Configuration.ConditionSetBuilder.StatusStacks(IDs.Status.AnglersArt, 10, "<"),
                        ],
                    },
                    new Conditions.ConditionGroup {
                        CombineMode = Conditions.ConditionCombineMode.All,
                        Conditions = [
                            TimeWindowCondition(windowStart, windowEnd, invert: false),
                            Configuration.ConditionSetBuilder.ActionOnCooldown(IDs.Actions.Mooch2),
                        ],
                    },
                ],
            };
        }
        else {
            ac.CastPatience.ConditionSet = Configuration.ConditionSetBuilder.All(
                TimeWindowCondition(windowStart, windowEnd, invert: true),
                Configuration.ConditionSetBuilder.StatusStacks(IDs.Status.AnglersArt, 10, "<"));
        }
        if (!plan.ResourcePolicy.UseCordials)
            return;
        var cordialConditions = new List<Conditions.Condition> {
            TimeWindowCondition(windowStart, windowEnd, invert: true),
            Configuration.ConditionSetBuilder.Gp(gpMax, "<"),
        };
        ac.CastCordial.Enabled = true;
        ac.CastCordial.IgnoreTimeWindow = true;
        ac.CastCordial.GpThresholdAbove = false;
        ac.CastCordial.GpThreshold = Math.Max(0, gpMax - 1);
        ac.CastCordial.ConditionSet = new Conditions.ConditionSet {
            CombineMode = Conditions.ConditionCombineMode.All,
            Groups = [
                new Conditions.ConditionGroup {
                    CombineMode = Conditions.ConditionCombineMode.All,
                    Conditions = cordialConditions,
                },
                new Conditions.ConditionGroup {
                    CombineMode = Conditions.ConditionCombineMode.Any,
                    Conditions = [.. CordialReadyConditions()],
                },
            ],
        };
    }

    private static Conditions.Condition TimeWindowCondition(TimeOnly start, TimeOnly end, bool invert) {
        var set = Configuration.ConditionSetBuilder.SingleTimeWindow(start, end, invert);
        return set.Groups[0].Conditions[0];
    }

    private static IEnumerable<Conditions.Condition> CordialReadyConditions()
    => [
        Configuration.ConditionSetBuilder.ItemCooldownReady(IDs.Item.HiCordial),
        Configuration.ConditionSetBuilder.ItemCooldownReady(IDs.Item.HQCordial),
        Configuration.ConditionSetBuilder.ItemCooldownReady(IDs.Item.Cordial),
        Configuration.ConditionSetBuilder.ItemCooldownReady(IDs.Item.HQCordial),
    ];

    private static void ApplyCastPolicy(CustomPresetConfig preset, int baitId, ImportedFish target, SolverOutput plan) {
        var baitCfg = preset.ListOfBaits.FirstOrDefault(b => b.BaitFish.Id == baitId) ?? preset.ListOfMooch.FirstOrDefault(b => b.BaitFish.Id == baitId);
        if (baitCfg == null)
            return;

        var isIntuition = target.Predators.Count > 0;
        var hookset = isIntuition ? baitCfg.IntuitionHook : baitCfg.NormalHook;
        ApplyEarlyCancel(hookset, plan);
        ApplyLurePolicy(hookset, plan);
        baitCfg.SetHooksetTimer(target.BiteType, target.BiteTimeMin, target.BiteTimeMax, isIntuition);
    }

    private static void ApplyEarlyCancel(BaseHookset hookset, SolverOutput plan) {
        if (plan.EarlyCancelSec is not { } cancel || cancel <= 0)
            return;
        hookset.ChumTimeoutMax = cancel;
    }

    private static void ApplyLurePolicy(BaseHookset hookset, SolverOutput plan) {
        var lureRule = plan.CastPolicy.FirstOrDefault(r =>
            r.Action is HookActionKind.ModestLure or HookActionKind.AmbitiousLure);
        if (lureRule == null)
            return;

        ref var cl = ref hookset.CastLures;
        cl.Enabled = true;
        cl.CancelAttempt = lureRule.Action == HookActionKind.ModestLure;
        cl.LureTarget = LureTarget.Special;
        cl.Id = lureRule.Action == HookActionKind.AmbitiousLure ? IDs.Actions.AmbitiousLure : IDs.Actions.ModestLure;
    }

    private static void SetupBaitAndMooch(CustomPresetConfig preset, int bait, ImportedFish fishTarget, List<ImportedFish>? moochList, bool isIntuition, ImportedFish? slapFish, SolverOutput? plan) {
        var initBaitCfg = preset.ListOfBaits.FirstOrDefault(f => f.BaitFish.Id == bait);
        if (initBaitCfg == null) {
            initBaitCfg = new HookConfig(bait);
            initBaitCfg.ResetAllHooksets();
        }

        if (isIntuition)
            initBaitCfg.IntuitionHook.UseCustomStatusHook = true;

        if (moochList == null || moochList.Count == 0) {
            ConfigureStraightCatchBait(initBaitCfg, fishTarget, slapFish, isIntuition);
            preset.ReplaceBaitConfig(initBaitCfg);
            return;
        }

        moochList = [.. moochList];
        moochList.Reverse();

        foreach (var mooch in moochList) {
            var newMooch = preset.ListOfMooch.FirstOrDefault(f => f.BaitFish.Id == mooch.ItemId) ?? new HookConfig(mooch.ItemId);
            newMooch.ResetAllHooksets();

            if (isIntuition)
                newMooch.IntuitionHook.UseCustomStatusHook = true;

            var fishConfig = AddFishConfig(preset, mooch.ItemId);
            fishConfig.Mooch.Enabled = true;
            fishConfig.Mooch.Mooch2.Enabled = true;
            if (isIntuition) {
                fishConfig.Mooch.ConditionSet = Configuration.ConditionSetBuilder.All(
                    Configuration.ConditionSetBuilder.IntuitionActive());
            }

            var nextFish = mooch == moochList.First() ? fishTarget
                : mooch == moochList.Last() ? moochList[^2] : moochList[moochList.IndexOf(mooch) - 1];

            newMooch.SetBiteAndHookType(nextFish.BiteType, nextFish.HookType, isIntuition);
            newMooch.SetHooksetTimer(nextFish.BiteType, nextFish.BiteTimeMin, nextFish.BiteTimeMax, isIntuition);
            preset.ReplaceMoochConfig(newMooch);

            if (mooch == moochList.Last()) {
                initBaitCfg.SetBiteAndHookType(mooch.BiteType, mooch.HookType, isIntuition);
                initBaitCfg.SetHooksetTimer(mooch.BiteType, mooch.BiteTimeMin, mooch.BiteTimeMax, isIntuition);
                preset.ReplaceBaitConfig(initBaitCfg);
            }
        }
    }

    private static void ConfigureStraightCatchBait(HookConfig initBaitCfg, ImportedFish fishTarget, ImportedFish? slapFish, bool isIntuition) {
        initBaitCfg.SetBiteAndHookType(fishTarget.BiteType, fishTarget.HookType, isIntuition);

        if (slapFish != null && slapFish.BiteType != fishTarget.BiteType)
            initBaitCfg.SetBiteAndHookType(slapFish.BiteType, slapFish.HookType, isIntuition);

        if (fishTarget.IsLureFish) {
            ref var cl = ref initBaitCfg.NormalHook.CastLures;
            cl.Enabled = true;
            cl.CancelAttempt = true;
            cl.LureTarget = LureTarget.Special;
            cl.ConditionSet = Configuration.ConditionSetBuilder.SingleStatus(IDs.Status.PrizeCatch);
            cl.Id = fishTarget.HookType == HookType.Powerful ? IDs.Actions.AmbitiousLure : IDs.Actions.ModestLure;
        }

        initBaitCfg.SetHooksetTimer(fishTarget.BiteType, fishTarget.BiteTimeMin, fishTarget.BiteTimeMax, isIntuition);
        if (slapFish != null)
            initBaitCfg.SetHooksetTimer(slapFish.BiteType, slapFish.BiteTimeMin, slapFish.BiteTimeMax, isIntuition);
    }

    private static FishConfig AddFishConfig(CustomPresetConfig preset, int fishId) {
        var existing = preset.GetFishById((uint)fishId);
        if (existing != null)
            return existing;

        var cfg = new FishConfig(fishId);
        preset.AddItem(cfg);
        return cfg;
    }

    private static bool TryParseFishingWindow(string? time, out TimeOnly start, out TimeOnly end) {
        start = default;
        end = default;
        if (string.IsNullOrWhiteSpace(time))
            return false;

        var parts = time.Split(['–', '-', '—'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        return TimeOnly.TryParse(parts[0], out start) && TimeOnly.TryParse(parts[^1], out end);
    }

    private static bool TryParseFishingWindowEnd(string? time, out TimeOnly end) {
        end = default;
        return TryParseFishingWindow(time, out _, out end);
    }

    private static int ResolveStartingPrepBait(ImportedFish target) {
        if (target.Predators.Count == 0)
            return 0;
        // Prefer the bait that covers the most prep fish
        var shared = ResolveSharedPrepBait(target);
        if (shared > 0)
            return shared;
        return ResolveFallbackPrepBait(target);
    }

    // bait used once int is up to catch the mooch fish
    private static int ResolveIntuitionCatchBait(ImportedFish target) {
        if (target.Mooches.Count == 0)
            return 0;
        var moochId = target.Mooches[0];
        var spotId = target.SpotIds.FirstOrDefault();
        if (spotId <= 0)
            return FindFish(moochId)?.InitialBait ?? 0;
        var pools = BuildPoolMembersAtSpot(spotId);
        if (pools.Count == 0) {
            var fallback = BuildPoolsAtSpot(spotId);
            return PrepBaitSelector.SelectBestSharedPrepBait(fallback, [moochId]);
        }
        var kb = GameRes.FishSolver.KnowledgeBase;
        var selected = PrepBaitSelector.SelectBestSharedPrepBait(pools, [moochId], spotId, kb?.FishById);
        if (selected > 0)
            return selected;
        return FindFish(moochId)?.InitialBait ?? 0;
    }

    private static int ResolvePreferredPrepBaitForFish(ImportedFish target, int fishId) {
        var spotId = target.SpotIds.FirstOrDefault();
        var baits = ResolveBaitsForFishAtSpot(FindFish(fishId), spotId).ToList();
        if (baits.Count == 0)
            return ResolveStartingPrepBait(target);
        // Prefer force-only bait when available, else starting bait if listed.
        var start = ResolveStartingPrepBait(target);
        var catchBait = ResolveIntuitionCatchBait(target);
        if (target.Mooches.Contains(fishId) && catchBait > 0 && baits.Contains(catchBait))
            return catchBait;
        if (start > 0 && baits.Contains(start))
            return start;
        return baits[0];
    }

    private static IEnumerable<int> ResolveBaitsForFishAtSpot(ImportedFish? fish, int spotId) {
        if (fish == null)
            yield break;
        var seen = new HashSet<int>();
        if (spotId > 0) {
            var pools = BuildPoolsAtSpot(spotId);
            foreach (var (baitId, members) in pools) {
                if (members.Contains(fish.ItemId) && seen.Add(baitId))
                    yield return baitId;
            }
            var memberPools = BuildPoolMembersAtSpot(spotId);
            foreach (var (baitId, members) in memberPools) {
                if (members.Any(m => m.FishId == fish.ItemId) && seen.Add(baitId))
                    yield return baitId;
            }
        }
        if (fish.InitialBait > 0 && seen.Add(fish.InitialBait))
            yield return fish.InitialBait;
    }

    private static int ResolveSharedPrepBait(ImportedFish target) {
        if (target.Predators.Count == 0)
            return 0;

        var spotId = target.SpotIds.FirstOrDefault();
        if (spotId <= 0)
            return ResolveFallbackPrepBait(target);

        var prepIds = target.Predators.Select(p => p.ItemId).ToList();
        var poolsByBait = BuildPoolMembersAtSpot(spotId);
        if (poolsByBait.Count == 0) {
            var fallbackPools = BuildPoolsAtSpot(spotId);
            return PrepBaitSelector.SelectBestSharedPrepBait(fallbackPools, prepIds);
        }
        var kb = GameRes.FishSolver.KnowledgeBase;
        var selected = PrepBaitSelector.SelectBestSharedPrepBait(
            poolsByBait,
            prepIds,
            spotId,
            kb?.FishById);
        return selected > 0 ? selected : ResolveFallbackPrepBait(target);
    }

    private static int ResolveFallbackPrepBait(ImportedFish target) {
        var first = FindFish(target.Predators[0].ItemId);
        return first == null ? 0 : ResolveTackleBait(first, ResolveMoochFish(first.Mooches));
    }

    private static Dictionary<int, IReadOnlyList<PoolMember>> BuildPoolMembersAtSpot(int spotId) {
        var kb = GameRes.FishSolver.KnowledgeBase;
        if (kb == null)
            return [];
        return kb.PoolsByKey.Values
            .Where(p => p.SpotId == spotId && p.Members.Count > 0)
            .ToDictionary(p => p.BaitId, p => (IReadOnlyList<PoolMember>)p.Members);
    }

    private static Dictionary<int, IReadOnlyCollection<int>> BuildPoolsAtSpot(int spotId) {
        var kb = GameRes.FishSolver.KnowledgeBase;
        if (kb != null) {
            return kb.PoolsByKey.Values
                .Where(p => p.SpotId == spotId)
                .ToDictionary(p => p.BaitId, p => (IReadOnlyCollection<int>)p.FishIds);
        }

        var pools = new Dictionary<int, HashSet<int>>();
        foreach (var fish in GameRes.ImportedFishes) {
            if (!fish.SpotIds.Contains(spotId) || fish.InitialBait <= 0)
                continue;
            if (!pools.TryGetValue(fish.InitialBait, out var set)) {
                set = [];
                pools[fish.InitialBait] = set;
            }
            set.Add(fish.ItemId);
        }
        return pools.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyCollection<int>)kvp.Value);
    }

    private static void ConfigureDualPredatorIcSlap(CustomPresetConfig preset, ImportedFish target) {
        var a = target.Predators[0];
        var b = target.Predators[1];
        var baitA = ResolvePreferredPrepBaitForFish(target, a.ItemId);
        var baitB = ResolvePreferredPrepBaitForFish(target, b.ItemId);
        if (baitA <= 0)
            baitA = ResolveStartingPrepBait(target);
        if (baitB <= 0)
            baitB = baitA;
        ConfigurePredatorIcSlap(preset, a.ItemId, a.Quantity, b.ItemId, b.Quantity, baitA);
        ConfigurePredatorIcSlap(preset, b.ItemId, b.Quantity, a.ItemId, a.Quantity, baitB);
    }
    private static void ConfigureIntuitionMoochFish(CustomPresetConfig preset, ImportedFish target, SolverOutput plan) {
        if (target.Mooches.Count == 0 || target.Predators.Count == 0)
            return;
        var moochId = target.Mooches[0];
        var cfg = AddFishConfig(preset, moochId);
        // Mooch only under intuition
        cfg.Mooch.Enabled = true;
        cfg.Mooch.Mooch2.Enabled = true;
        cfg.Mooch.ConditionSet = Configuration.ConditionSetBuilder.All(
            Configuration.ConditionSetBuilder.IntuitionActive());
        // Bank swimbait while building intuition
        if (plan.PlayerProfileUsed.Skills.SparefulHand) {
            var bankCount = 3;
            cfg.SparefulHand.Enabled = true;
            cfg.SparefulHand.FishIdToCheck = (uint)moochId;
            cfg.SparefulHand.ConditionSet = Configuration.ConditionSetBuilder.All(
                Configuration.ConditionSetBuilder.IntuitionActive(inverse: true),
                Configuration.ConditionSetBuilder.SwimbaitCount(bankCount, "<", moochId));
        }
    }

    private static void ConfigurePredatorIcSlap(CustomPresetConfig preset, int fishId, int quantity, int otherFishId, int otherQuantity, int prepBait) {
        var cfg = AddFishConfig(preset, fishId);
        cfg.IdenticalCast.Enabled = true;
        cfg.IdenticalCast.ConditionSet = Configuration.ConditionSetBuilder.All(
            Configuration.ConditionSetBuilder.FishCount(fishId, quantity - 1, "="),
            Configuration.ConditionSetBuilder.FishCount(otherFishId, otherQuantity, ">="),
            Configuration.ConditionSetBuilder.CurrentBait(prepBait));
        cfg.IdenticalCast.GpThreshold = 350;
        cfg.IdenticalCast.DontCancelMooch = true;
        cfg.SurfaceSlap.Enabled = true;
        cfg.SurfaceSlap.ConditionSet = Configuration.ConditionSetBuilder.All(
            Configuration.ConditionSetBuilder.FishCount(fishId, quantity, ">="),
            Configuration.ConditionSetBuilder.FishCount(otherFishId, otherQuantity - 1, "="),
            Configuration.ConditionSetBuilder.CurrentBait(prepBait));
        cfg.SurfaceSlap.GpThreshold = 200;
        cfg.SurfaceSlap.DontCancelMooch = true;
    }

    private static void ConfigureSinglePredatorIcSlap(FishConfig cfg, int fishId, int quantity, int prepBait) {
        cfg.IdenticalCast.Enabled = true;
        cfg.IdenticalCast.ConditionSet = Configuration.ConditionSetBuilder.All(
            Configuration.ConditionSetBuilder.FishCount(fishId, quantity - 1, "="),
            Configuration.ConditionSetBuilder.CurrentBait(prepBait));
        cfg.IdenticalCast.GpThreshold = 350;
        cfg.IdenticalCast.DontCancelMooch = true;
        cfg.SurfaceSlap.Enabled = true;
        cfg.SurfaceSlap.ConditionSet = Configuration.ConditionSetBuilder.All(
            Configuration.ConditionSetBuilder.FishCount(fishId, quantity, "="),
            Configuration.ConditionSetBuilder.CurrentBait(prepBait));
        cfg.SurfaceSlap.GpThreshold = 200;
        cfg.SurfaceSlap.DontCancelMooch = true;
    }

    private static ImportedFish? FindFish(int fishId)
        => GameRes.ImportedFishes.FirstOrDefault(f => f.ItemId == fishId);

    private static List<ImportedFish> ResolveMoochFish(IEnumerable<int> moochIds)
        => [.. moochIds.Select(FindFish).OfType<ImportedFish>()];

    private static int ResolveTackleBait(ImportedFish target, List<ImportedFish> moochList)
        => moochList.Count > 0 ? moochList[^1].InitialBait : target.InitialBait;
}

