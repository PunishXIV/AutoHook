using System.Diagnostics.CodeAnalysis;

namespace AutoHook.Conditions;

public static class ConditionSetExtensions {
    public static bool HasGroups([NotNullWhen(true)] this ConditionSet? set)
        => set is { Groups.Count: > 0 };

    // any group with at least one condition.
    public static bool HasAnyCondition([NotNullWhen(true)] this ConditionSet? set)
        => set is { Groups.Count: > 0 } && set.Groups.Any(g => g.Conditions.Count > 0);

    // null/empty = pass. otherwise evaluate.
    public static bool PassesOrUnconfigured(this ConditionSet? set)
        => set is not { Groups.Count: > 0 } || set.Evaluate(Service.WorldState, ConditionRegistry.Registry);

    // has groups and eval passes. unconfigured = false.
    public static bool Passes([NotNullWhen(true)] this ConditionSet? set)
        => set is { Groups.Count: > 0 } && set.Evaluate(Service.WorldState, ConditionRegistry.Registry);

    // has groups and eval fails. unconfigured = false.
    public static bool Fails([NotNullWhen(true)] this ConditionSet? set)
        => set is { Groups.Count: > 0 } && !set.Evaluate(Service.WorldState, ConditionRegistry.Registry);
}
