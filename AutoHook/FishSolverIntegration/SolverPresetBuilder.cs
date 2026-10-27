using AutoHook.FishSolver.Engine;
using AutoHook.FishSolver.Models;
using Lumina.Excel.Sheets;

namespace AutoHook.FishSolverIntegration;

public static class SolverPresetBuilder {
    public static CustomPresetConfig? Build(SolverOutput plan, string? presetName = null) {
        var target = FindFish(plan.TargetFishId);
        if (target == null)
            return null;

        var moochList = UsesMoochRoute(plan) ? ResolveMoochFish(target.Mooches) : [];
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

    // MoochHold/SwimbaitBank/Multimooch use the mooch list, the rest keept Mooches as an alternative but shouldn't be wired
    private static bool UsesMoochRoute(SolverOutput plan)
        => plan.HoldMode is PrepHoldMode.MoochHold or PrepHoldMode.SwimbaitBank
           || plan.Archetype is StrategyArchetype.PreMoochOpener or StrategyArchetype.MoochChain
               or StrategyArchetype.MoochLoop or StrategyArchetype.SwimbaitBank;

    private static void SetupFishCaughtActions(CustomPresetConfig preset, Fish target, SolverOutput plan, Fish? slapFish) {
        if (slapFish != null) {
            var slapCfg = AddFishConfig(preset, slapFish.Id);
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

        AddFishConfig(preset, target.Id);
    }

    private static void SetupSwimbaitBank(CustomPresetConfig preset, Fish target, SolverOutput plan) {
        var bankCount = plan.PrepPhase.Steps
            .FirstOrDefault(s => s.Action == PrepActionKind.SwimbaitSelect)?.Count ?? 3;

        var fishConfig = AddFishConfig(preset, target.Id);
        fishConfig.SparefulHand.Enabled = true;
        fishConfig.SparefulHand.FishIdToCheck = (uint)target.Id;
        fishConfig.SparefulHand.ConditionSet = Configuration.ConditionSetBuilder.SwimbaitCount(bankCount, "<", target.Id) is { } cond
            ? new ConditionSet {
                CombineMode = ConditionCombineMode.All,
                Groups = [new ConditionGroup { CombineMode = ConditionCombineMode.All, Conditions = [cond] }],
            } : null;
        fishConfig.StopAfterCaughtLimit.Value = (true, bankCount + 1);
    }

    private static void SetupIntuitionPrep(CustomPresetConfig preset, Fish target, SolverOutput plan) {
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
            AddFishConfig(preset, fish.Id);
        }
    }
    private static void ConfigureIntuitionPrepBaits(CustomPresetConfig preset, Fish target) {
        var spotId = target.SpotIds.FirstOrDefault();
        var predators = target.Predators
            .Select(p => FindFish(p.ItemId))
            .Where(f => f != null)
            .Cast<Fish>()
            .ToList();
        if (predators.Count == 0)
            return;
        var baitToFish = new Dictionary<int, List<Fish>>();
        foreach (var fish in predators) {
            foreach (var baitId in ResolveBaitsForFishAtSpot(fish, spotId)) {
                if (!baitToFish.TryGetValue(baitId, out var list)) {
                    list = [];
                    baitToFish[baitId] = list;
                }
                if (list.All(f => f.Id != fish.Id))
                    list.Add(fish);
            }
        }
        // always include the starting / force-only baits
        var startBait = ResolveStartingPrepBait(target);
        var catchBait = ResolveIntuitionCatchBait(target);
        EnsureBaitHasFish(baitToFish, startBait, predators);
        if (catchBait > 0 && catchBait != startBait)
            EnsureBaitHasFish(baitToFish, catchBait, [.. predators.Where(f => IsMoochPredator(target, f.Id))]);
        foreach (var (baitId, fishOnBait) in baitToFish) {
            if (baitId <= 0 || fishOnBait.Count == 0)
                continue;
            ConfigurePrepBait(preset, baitId, fishOnBait);
            EnableIntuitionSwimbaitUse(preset, baitId);
        }
    }
    private static void EnsureBaitHasFish(Dictionary<int, List<Fish>> baitToFish, int baitId, List<Fish> candidates) {
        if (baitId <= 0 || candidates.Count == 0)
            return;
        if (!baitToFish.TryGetValue(baitId, out var list)) {
            list = [];
            baitToFish[baitId] = list;
        }
        foreach (var fish in candidates) {
            if (list.All(f => f.Id != fish.Id))
                list.Add(fish);
        }
    }
    private static bool IsMoochPredator(Fish target, int fishId)
        => target.Mooches.Contains(fishId) || target.Predators.Any(p => p.ItemId == fishId && target.Mooches.Contains(p.ItemId));
    private static void EnableIntuitionSwimbaitUse(CustomPresetConfig preset, int baitId) {
        var baitCfg = preset.ListOfBaits.FirstOrDefault(f => f.BaitFish.Id == baitId);
        if (baitCfg == null)
            return;
        baitCfg.SwimbaitIntuition.UseSwimbait = true;
    }
    private static void SetupIntuitionMoochTarget(CustomPresetConfig preset, Fish target, List<Fish> moochList, SolverOutput plan) {
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
    private static void NarrowPrepBaitsToMoochBiteUnderIntuition(CustomPresetConfig preset, Fish target, Fish moochFish) {
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

    private static void ConfigureTargetIntuitionBait(CustomPresetConfig preset, Fish target, List<Fish> moochList, SolverOutput plan) {
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

    private static void ConfigurePrepBait(CustomPresetConfig preset, int baitId, IReadOnlyList<Fish> triggerFish) {
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

    private static void SetupIntuitionBaitSwapRules(CustomPresetConfig preset, Fish target, List<Fish> targetMoochList, SolverOutput plan) {
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

    private static void SetupSinglePredatorPrepSwap(CustomPresetConfig preset, Fish target) {
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

    private static void SetupMultiPredatorPrepSwaps(CustomPresetConfig preset, Fish target) {
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
            var nonMooch = resolved.Where(r => !target.Mooches.Contains(r.Fish.Id)).ToList();
            if (nonMooch.Count > 0) {
                var conditions = new List<Condition> {
                    Configuration.ConditionSetBuilder.IntuitionActive(inverse: true),
                    Configuration.ConditionSetBuilder.CurrentBait(catchBait, inverse: true),
                };
                foreach (var r in nonMooch)
                    conditions.Add(Configuration.ConditionSetBuilder.FishCount(r.Fish.Id, r.Predator.Quantity));
                preset.ExtraCfg.Triggers.Add(new ExtraTrigger {
                    Enabled = true,
                    ConditionSet = Configuration.ConditionSetBuilder.All([.. conditions]),
                    SwapBait = true,
                    BaitToSwap = new BaitFishClass(catchBait),
                });
            }
        }
        var allComplete = resolved.Select(r => Configuration.ConditionSetBuilder.FishCount(r.Fish.Id, r.Predator.Quantity)).ToList();
        var completeConditions = new List<Condition> {
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

    private static void AddPostWindowStopTrigger(CustomPresetConfig preset, Fish target) {
        if (!TryParseFishingWindowEnd(target.Time, out var windowEnd))
            return;

        var stopEnd = windowEnd.AddHours(1);
        preset.ExtraCfg.Triggers.Add(new ExtraTrigger {
            Enabled = true,
            ConditionSet = Configuration.ConditionSetBuilder.SingleTimeWindow(windowEnd, stopEnd),
            StopAction = ExtraStopAction.QuitFishing,
        });
    }

    private static void SetupAutoCasts(CustomPresetConfig preset, Fish target, List<Fish> moochList, SolverOutput plan) {
        ref var ac = ref preset.AutoCastsCfg;
        ac.EnableAll = true;
        ac.CastLine.Enabled = true;
        ac.CastCordial.Enabled = plan.ResourcePolicy.UseCordials;
        ac.CastCollect.Enabled = Item.GetRow((uint)target.Id).IsCollectable;
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
            var isCollectable = Item.GetRow((uint)target.Id).IsCollectable;
            var needsMooch = moochList.Count > 0 || plan.HoldMode is PrepHoldMode.MoochHold or PrepHoldMode.SwimbaitBank;
            var multiMooch = moochList.Count > 1 || plan.Archetype == StrategyArchetype.MoochChain;
            // single mooch, no collectability / Spareful Hand: try mooch2 first (more gp efficient). Patience only when M2 is on CD.
            var mooch2Primary = needsMooch && !multiMooch && plan.HoldMode != PrepHoldMode.SwimbaitBank && !isCollectable && plan.PlayerProfileUsed.Skills.MoochII;
            if (mooch2Primary) {
                ac.CastPatience.Enabled = true;
                ApplyPatienceMinGp(ac.CastPatience, plan);
                ac.CastPatience.ConditionSet = Configuration.ConditionSetBuilder.All(Configuration.ConditionSetBuilder.ActionOnCooldown(IDs.Actions.Mooch2));
                ac.CastMakeShiftBait.Enabled = true;
                if (ac.CastChum.Enabled)
                    ApplyChumMoochGate(ac.CastChum, plan, mooch2Primary: true);
            }
            else if (needsMooch) {
                // multi-mooch / collectable / swimbait: Patience or Prize Catch primary; mooch2 stays on fish as a mid-loop fallback
                if (plan.HoldMode != PrepHoldMode.SwimbaitBank) {
                    ac.CastPatience.Enabled = true;
                    ApplyPatienceMinGp(ac.CastPatience, plan);
                    ac.CastMakeShiftBait.Enabled = true;
                }
                if (ac.CastChum.Enabled)
                    ApplyChumMoochGate(ac.CastChum, plan, mooch2Primary: false);
            }
            else if (isCollectable) {
                ac.CastPatience.Enabled = true;
                ApplyPatienceMinGp(ac.CastPatience, plan);
            }
        }
    }

    private static void ApplyPatienceMinGp(AutoPatience patience, SolverOutput plan) {
        if (plan.ResourcePolicy.PatienceMinGp <= 0)
            return;
        patience.GpThresholdAbove = true;
        // don't use SetThreshold — that triggers Save mid-build
        patience.GpThreshold = plan.ResourcePolicy.PatienceMinGp;
    }

    // gate chum behind any mooch tool (m2, patience, prize catch) so you don't get in a loop where you're too low on gp to mooch
    private static void ApplyChumMoochGate(AutoChum chum, SolverOutput plan, bool mooch2Primary) {
        const int mooch2Cost = 100;
        const int patienceIICost = 560;
        const int prizeCatchCost = 200;
        var patienceFloor = plan.ResourcePolicy.PatienceMinGp > 0 ? plan.ResourcePolicy.PatienceMinGp : patienceIICost;
        if (mooch2Primary) {
            // M2 ready -> only need M2 GP; M2 on CD -> save for Patience fallback
            chum.ConditionSet = new ConditionSet {
                CombineMode = ConditionCombineMode.Any,
                Groups = [
                    new ConditionGroup {
                        CombineMode = ConditionCombineMode.All,
                        Conditions = [Configuration.ConditionSetBuilder.StatusActive(IDs.Status.AnglersFortune)],
                    },
                    new ConditionGroup {
                        CombineMode = ConditionCombineMode.All,
                        Conditions = [Configuration.ConditionSetBuilder.StatusActive(IDs.Status.PrizeCatch)],
                    },
                    new ConditionGroup {
                        CombineMode = ConditionCombineMode.All,
                        Conditions = [
                            Configuration.ConditionSetBuilder.ActionReady(IDs.Actions.Mooch2),
                            Configuration.ConditionSetBuilder.Gp(mooch2Cost),
                        ],
                    },
                    new ConditionGroup {
                        CombineMode = ConditionCombineMode.All,
                        Conditions = [
                            Configuration.ConditionSetBuilder.ActionOnCooldown(IDs.Actions.Mooch2),
                            Configuration.ConditionSetBuilder.Gp(patienceFloor),
                        ],
                    },
                ],
            };
            return;
        }

        var gpFloor = plan.HoldMode == PrepHoldMode.SwimbaitBank ? prizeCatchCost : patienceFloor;
        chum.ConditionSet = Configuration.ConditionSetBuilder.Any(
            Configuration.ConditionSetBuilder.StatusActive(IDs.Status.AnglersFortune),
            Configuration.ConditionSetBuilder.StatusActive(IDs.Status.PrizeCatch),
            Configuration.ConditionSetBuilder.Gp(gpFloor));
    }

    private static void ConfigurePreWindowResourceCasts(ref AutoCastsConfig ac, Fish target, SolverOutput plan, List<Fish> moochList) {
        if (!TryParseFishingWindow(target.Time, out var windowStart, out var windowEnd))
            return;
        var gpMax = plan.PlayerProfileUsed.GpMax;
        ac.CastPatience.Enabled = true;
        ApplyPatienceMinGp(ac.CastPatience, plan);
        // Prep: outside window + AA not capped. Window: Patience only while Mooch II is on CD
        if (moochList.Count > 0) {
            ac.CastPatience.ConditionSet = new ConditionSet {
                CombineMode = ConditionCombineMode.Any,
                Expression = "A || B",
                Groups = [
                    new ConditionGroup {
                        CombineMode = ConditionCombineMode.All,
                        Conditions = [
                            TimeWindowCondition(windowStart, windowEnd, invert: true),
                            Configuration.ConditionSetBuilder.StatusStacks(IDs.Status.AnglersArt, 10, "<"),
                        ],
                    },
                    new ConditionGroup {
                        CombineMode = ConditionCombineMode.All,
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
        var cordialConditions = new List<Condition> {
            TimeWindowCondition(windowStart, windowEnd, invert: true),
            Configuration.ConditionSetBuilder.Gp(gpMax, "<"),
        };
        ac.CastCordial.Enabled = true;
        ac.CastCordial.SkipGlobalTimeWindow = true;
        ac.CastCordial.GpThresholdAbove = false;
        ac.CastCordial.GpThreshold = Math.Max(0, gpMax - 1);
        ac.CastCordial.ConditionSet = new ConditionSet {
            CombineMode = ConditionCombineMode.All,
            Groups = [
                new ConditionGroup {
                    CombineMode = ConditionCombineMode.All,
                    Conditions = cordialConditions,
                },
                new ConditionGroup {
                    CombineMode = ConditionCombineMode.Any,
                    Conditions = [.. CordialReadyConditions()],
                },
            ],
        };
    }

    private static Condition TimeWindowCondition(TimeOnly start, TimeOnly end, bool invert) {
        var set = Configuration.ConditionSetBuilder.SingleTimeWindow(start, end, invert);
        return set.Groups[0].Conditions[0];
    }

    private static IEnumerable<Condition> CordialReadyConditions()
    => [
        Configuration.ConditionSetBuilder.ItemCooldownReady(IDs.Item.HiCordial),
        Configuration.ConditionSetBuilder.ItemCooldownReady(IDs.Item.HQCordial),
        Configuration.ConditionSetBuilder.ItemCooldownReady(IDs.Item.Cordial),
        Configuration.ConditionSetBuilder.ItemCooldownReady(IDs.Item.HQCordial),
    ];

    private static void ApplyCastPolicy(CustomPresetConfig preset, int baitId, Fish target, SolverOutput plan) {
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

        var actionId = lureRule.Action == HookActionKind.AmbitiousLure ? IDs.Actions.AmbitiousLure : IDs.Actions.ModestLure;
        // lure stack / lure isolation should run unconditionally
        hookset.CastLures.ConfigureSpecialLure(actionId);
    }

    private static void SetupBaitAndMooch(CustomPresetConfig preset, int bait, Fish fishTarget, List<Fish>? moochList, bool isIntuition, Fish? slapFish, SolverOutput? plan) {
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

        foreach (var mooch in moochList) {
            var newMooch = preset.ListOfMooch.FirstOrDefault(f => f.BaitFish.Id == mooch.Id) ?? new HookConfig(mooch.Id);
            newMooch.ResetAllHooksets();

            if (isIntuition)
                newMooch.IntuitionHook.UseCustomStatusHook = true;

            var fishConfig = AddFishConfig(preset, mooch.Id);
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

    private static void ConfigureStraightCatchBait(HookConfig initBaitCfg, Fish fishTarget, Fish? slapFish, bool isIntuition) {
        initBaitCfg.SetBiteAndHookType(fishTarget.BiteType, fishTarget.HookType, isIntuition);

        if (slapFish != null && slapFish.BiteType != fishTarget.BiteType)
            initBaitCfg.SetBiteAndHookType(slapFish.BiteType, slapFish.HookType, isIntuition);

        if (fishTarget.IsLureFish) {
            var actionId = fishTarget.HookType == HookType.Powerful ? IDs.Actions.AmbitiousLure : IDs.Actions.ModestLure;
            initBaitCfg.NormalHook.CastLures.ConfigureSpecialLure(actionId, Configuration.ConditionSetBuilder.SingleStatus(IDs.Status.PrizeCatch));
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

    private static int ResolveStartingPrepBait(Fish target) {
        if (target.Predators.Count == 0)
            return 0;
        // Prefer the bait that covers the most prep fish
        var shared = ResolveSharedPrepBait(target);
        if (shared > 0)
            return shared;
        return ResolveFallbackPrepBait(target);
    }

    // bait used once int is up to catch the mooch fish
    private static int ResolveIntuitionCatchBait(Fish target) {
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
        var kb = FishBaitCatalog.Get().FishSolver.KnowledgeBase;
        var selected = PrepBaitSelector.SelectBestSharedPrepBait(pools, [moochId], spotId, kb?.FishById);
        if (selected > 0)
            return selected;
        return FindFish(moochId)?.InitialBait ?? 0;
    }

    private static int ResolvePreferredPrepBaitForFish(Fish target, int fishId) {
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

    private static IEnumerable<int> ResolveBaitsForFishAtSpot(Fish? fish, int spotId) {
        if (fish == null)
            yield break;
        var seen = new HashSet<int>();
        if (spotId > 0) {
            var pools = BuildPoolsAtSpot(spotId);
            foreach (var (baitId, members) in pools) {
                if (members.Contains(fish.Id) && seen.Add(baitId))
                    yield return baitId;
            }
            var memberPools = BuildPoolMembersAtSpot(spotId);
            foreach (var (baitId, members) in memberPools) {
                if (members.Any(m => m.FishId == fish.Id) && seen.Add(baitId))
                    yield return baitId;
            }
        }
        if (fish.InitialBait > 0 && seen.Add(fish.InitialBait))
            yield return fish.InitialBait;
    }

    private static int ResolveSharedPrepBait(Fish target) {
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
        var kb = FishBaitCatalog.Get().FishSolver.KnowledgeBase;
        var selected = PrepBaitSelector.SelectBestSharedPrepBait(
            poolsByBait,
            prepIds,
            spotId,
            kb?.FishById);
        return selected > 0 ? selected : ResolveFallbackPrepBait(target);
    }

    private static int ResolveFallbackPrepBait(Fish target) {
        var first = FindFish(target.Predators[0].ItemId);
        return first == null ? 0 : ResolveTackleBait(first, ResolveMoochFish(first.Mooches));
    }

    private static Dictionary<int, IReadOnlyList<PoolMember>> BuildPoolMembersAtSpot(int spotId) {
        var kb = FishBaitCatalog.Get().FishSolver.KnowledgeBase;
        if (kb == null)
            return [];
        return kb.PoolsByKey.Values
            .Where(p => p.SpotId == spotId && p.Members.Count > 0)
            .ToDictionary(p => p.BaitId, p => (IReadOnlyList<PoolMember>)p.Members);
    }

    private static Dictionary<int, IReadOnlyCollection<int>> BuildPoolsAtSpot(int spotId) {
        var kb = FishBaitCatalog.Get().FishSolver.KnowledgeBase;
        if (kb != null) {
            return kb.PoolsByKey.Values
                .Where(p => p.SpotId == spotId)
                .ToDictionary(p => p.BaitId, p => (IReadOnlyCollection<int>)p.FishIds);
        }

        var pools = new Dictionary<int, HashSet<int>>();
        foreach (var fish in FishBaitCatalog.Get().ImportedFishes) {
            if (!fish.SpotIds.Contains(spotId) || fish.InitialBait <= 0)
                continue;
            if (!pools.TryGetValue(fish.InitialBait, out var set)) {
                set = [];
                pools[fish.InitialBait] = set;
            }
            set.Add(fish.Id);
        }
        return pools.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyCollection<int>)kvp.Value);
    }

    private static void ConfigureDualPredatorIcSlap(CustomPresetConfig preset, Fish target) {
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
    private static void ConfigureIntuitionMoochFish(CustomPresetConfig preset, Fish target, SolverOutput plan) {
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

    private static Fish? FindFish(int fishId)
        => FishBaitCatalog.Get()[fishId];

    private static List<Fish> ResolveMoochFish(IEnumerable<int> moochIds)
        => [.. moochIds.Select(FindFish).OfType<Fish>()];

    private static int ResolveTackleBait(Fish target, List<Fish> moochList)
        => moochList.Count > 0 ? moochList[^1].InitialBait : target.InitialBait;
}

