using AutoHook.Conditions;
using AutoHook.Conditions.Definitions;
using static AutoHook.Conditions.ConditionRegistry;

namespace AutoHook.Configurations;

public partial class Configuration {
    internal static class ConditionSetBuilder {
        public static ConditionSet SingleFlag<TDef>(bool inverse = false) where TDef : IConditionDefinition {
            var cond = new Condition {
                TypeId = Registry.GetId<TDef>(),
                Params = []
            };
            if (inverse)
                cond.Params["inv"] = true;

            return Single(cond);
        }

        public static ConditionSet SingleStatusStacks(uint statusId, int minStacks)
            => Single(StatusStacks(statusId, minStacks));

        public static Condition StatusStacks(uint statusId, int stacks, string op = ">=") {
            return new Condition {
                TypeId = Registry.GetId<StatusStacksCD>(),
                Params = new Dictionary<string, object> {
                    ["ids"] = new List<object> { (long)statusId },
                    ["minStacks"] = stacks,
                    ["op"] = op,
                },
            };
        }

        public static ConditionSet SingleStatus(uint statusId, bool inverse = false)
            => Single(StatusActive(statusId, inverse));

        public static ConditionSet SingleSwimbaitCount(int value, bool above) {
            var cond = new Condition {
                TypeId = Registry.GetId<SwimbaitCountCD>(),
                Params = new Dictionary<string, object> {
                    ["val"] = value,
                    ["op"] = above ? ">=" : "<=",
                }
            };

            return Single(cond);
        }

        public static Condition SwimbaitCount(int value, string op = ">=", int fishId = 0) {
            var p = new Dictionary<string, object> {
                ["val"] = value,
                ["op"] = op,
            };
            if (fishId != 0)
                p["id"] = (long)fishId;
            return new Condition {
                TypeId = Registry.GetId<SwimbaitCountCD>(),
                Params = p,
            };
        }

        public static ConditionSet SingleFishCaughtCount(int fishId, int limit) {
            var dict = new IConditionDefinition.IntCompareParams(limit, ">=", false).ToParams();
            if (fishId > 0)
                dict["id"] = (long)fishId;
            var cond = new Condition {
                TypeId = Registry.GetId<SessionCaughtCountCD>(),
                Params = dict,
            };

            return Single(cond);
        }

        public static ConditionSet SingleHookCount(Guid hookGuid, int limit) {
            var dict = new IConditionDefinition.IntCompareParams(limit, ">=", false).ToParams();
            if (hookGuid != Guid.Empty)
                dict["guid"] = hookGuid.ToString();
            var cond = new Condition {
                TypeId = Registry.GetId<HookCountCD>(),
                Params = dict,
            };

            return Single(cond);
        }

        public static ConditionSet SingleTimeWindow(TimeOnly start, TimeOnly end, bool invert = false) {
            var cond = new Condition {
                TypeId = Registry.GetId<TimeWindowCD>(),
                Params = new TimeWindowCD.TimeWindowParams(start, end, invert).ToParams(),
            };

            return Single(cond);
        }

        public static ConditionSet All(params Condition[] conditions) {
            return new ConditionSet {
                CombineMode = ConditionCombineMode.All,
                Groups =
                [
                    new ConditionGroup {
                        CombineMode = ConditionCombineMode.All,
                        Conditions = [.. conditions],
                    }
                ],
            };
        }

        public static ConditionSet Any(params Condition[] conditions) {
            return new ConditionSet {
                CombineMode = ConditionCombineMode.All,
                Groups =
                [
                    new ConditionGroup {
                        CombineMode = ConditionCombineMode.Any,
                        Conditions = [.. conditions],
                    }
                ],
            };
        }

        public static Condition Gp(int value, string op = ">=") {
            var dict = new IConditionDefinition.IntCompareParams(value, op, false).ToParams();
            return new Condition {
                TypeId = Registry.GetId<GpCD>(),
                Params = dict,
            };
        }

        public static Condition ItemCooldownReady(uint itemId) {
            return new Condition {
                TypeId = Registry.GetId<ActionCooldownCD>(),
                Params = new Dictionary<string, object> {
                    ["id"] = (long)itemId,
                    ["type"] = 1L,
                    ["sec"] = 0L,
                    ["op"] = "<=",
                },
            };
        }

        /// <summary>True when the action still has remaining cooldown (e.g. Mooch II on CD).</summary>
        public static Condition ActionOnCooldown(uint actionId, int minSecondsRemaining = 1) {
            return new Condition {
                TypeId = Registry.GetId<ActionCooldownCD>(),
                Params = new Dictionary<string, object> {
                    ["id"] = (long)actionId,
                    ["type"] = 0L,
                    ["sec"] = (long)minSecondsRemaining,
                    ["op"] = ">=",
                },
            };
        }

        public static Condition IntuitionActive(bool inverse = false) {
            var cond = new Condition {
                TypeId = Registry.GetId<IntuitionActiveCD>(),
                Params = [],
            };
            if (inverse)
                cond.Params["inv"] = true;
            return cond;
        }

        public static Condition FishCount(int fishId, int count, string op = ">=") {
            var dict = new IConditionDefinition.IntCompareParams(count, op, false).ToParams();
            dict["id"] = (long)fishId;
            return new Condition {
                TypeId = Registry.GetId<FishCaughtCounterCD>(),
                Params = dict,
            };
        }

        public static Condition CurrentBait(int baitId, bool inverse = false) {
            var cond = new Condition {
                TypeId = Registry.GetId<CurrentBaitCD>(),
                Params = new Dictionary<string, object> {
                    ["ids"] = new List<object> { (long)baitId },
                },
            };
            if (inverse)
                cond.Params["inv"] = true;
            return cond;
        }

        public static Condition StatusActive(uint statusId, bool inverse = false) {
            var cond = new Condition {
                TypeId = Registry.GetId<StatusActiveCD>(),
                Params = new Dictionary<string, object> {
                    ["ids"] = new List<object> { (long)statusId }
                }
            };
            if (inverse)
                cond.Params["inv"] = true;
            return cond;
        }

        public static Condition? Range<TDef>(double min, double max) where TDef : IConditionDefinition {
            if (min <= 0 && max <= 0)
                return null;

            return new Condition {
                TypeId = Registry.GetId<TDef>(),
                Params = new Dictionary<string, object> {
                    // "r": [min, max]; max 0 => no upper bound
                    ["r"] = new List<object> { min, max }
                }
            };
        }

        private static ConditionSet Single(Condition cond) {
            return new ConditionSet {
                CombineMode = ConditionCombineMode.All,
                Groups =
                [
                    new ConditionGroup
                    {
                        CombineMode = ConditionCombineMode.All,
                        Conditions = [cond],
                    }
                ]
            };
        }
    }
}

