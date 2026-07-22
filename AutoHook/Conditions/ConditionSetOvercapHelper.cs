namespace AutoHook.Conditions;

// overcap overrides use ConditionSet storage, but empty ≠ allow (unlike normal sets).
public static class ConditionSetOvercapHelper {
    public static bool HasAnyEnabledCondition(ConditionSet? set) {
        if (set?.Groups is not { Count: > 0 } groups)
            return false;

        foreach (var group in groups) {
            if (!group.Enabled)
                continue;
            foreach (var c in group.Conditions) {
                if (c.Enabled)
                    return true;
            }
        }

        return false;
    }

    // true = cordial may overcap GP; false = default GP math.
    public static bool EvaluateAllowsOvercap(ConditionSet? set, WorldState world)
        => HasAnyEnabledCondition(set) && set!.Evaluate(world, ConditionRegistry.Registry);
}
