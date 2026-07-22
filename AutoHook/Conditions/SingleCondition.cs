namespace AutoHook.Conditions;

public sealed class SingleCondition<TCD, TValue>(Func<object?>? context = null) where TCD : class, IConditionDefinition, ISimpleConditionValue<TValue> where TValue : struct {
    // serialized as ConditionSet via SingleConditionConverter.
    public ConditionSet? BackingSet { get; set; }

    private ISimpleConditionValue<TValue> Definition => field ??= ConditionRegistry.Registry.GetDefinition<TCD>()!;

    public TValue Value {
        get => Get();
        set => Set(value);
    }

    private TValue Get() {
        var set = BackingSet;
        var cond = set.GetFirstCondition(Definition.Id);
        return cond == null ? default : Definition.FromParams(cond.Params);
    }

    private void Set(TValue value) {
        var p = Definition.ToParams(value, context?.Invoke());
        if (p == null || p.Count == 0) {
            var set = BackingSet;
            set = SingleConditionSetHelper.SetSingleCondition(set, Definition.Id, null);
            BackingSet = SingleConditionSetHelper.CompactOrNull(set);
        }
        else {
            BackingSet = SingleConditionSetHelper.SetSingleCondition(BackingSet, Definition.Id, p);
        }
    }
}
