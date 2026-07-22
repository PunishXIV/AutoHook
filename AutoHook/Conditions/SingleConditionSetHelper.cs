namespace AutoHook.Conditions;

public static class SingleConditionSetHelper {
    public static Condition? GetFirstCondition(this ConditionSet? set, string typeId) {
        if (!set.HasGroups())
            return null;
        foreach (var group in set.Groups) {
            var c = group.Conditions.FirstOrDefault(x => x.TypeId == typeId);
            if (c != null)
                return c;
        }
        return null;
    }

    public static ConditionSet? SetSingleCondition(ConditionSet? current, string typeId, IReadOnlyDictionary<string, object>? conditionParams) {
        if (conditionParams == null || conditionParams.Count == 0) {
            if (!current.HasGroups())
                return current;
            foreach (var group in current.Groups)
                group.Conditions.RemoveAll(c => c.TypeId == typeId);
            return current;
        }

        var set = current ?? new ConditionSet { CombineMode = ConditionCombineMode.All };
        var grp = set.HasGroups() ? set.Groups[0] : null;
        if (grp == null) {
            grp = new ConditionGroup { CombineMode = ConditionCombineMode.All };
            set.Groups.Add(grp);
        }

        var cond = grp.Conditions.FirstOrDefault(c => c.TypeId == typeId);
        var paramsDict = new Dictionary<string, object>(conditionParams);
        if (cond == null)
            grp.Conditions.Add(new Condition { TypeId = typeId, Params = paramsDict });
        else
            cond.Params = paramsDict;
        return set;
    }

    // drop empty groups. null if nothing left.
    public static ConditionSet? CompactOrNull(ConditionSet? set) {
        if (!set.HasGroups())
            return null;
        set.Groups.RemoveAll(g => g.Conditions.Count == 0);
        return set.HasGroups() ? set : null;
    }
}
