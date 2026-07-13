using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using static AutoHook.Conditions.IConditionDefinition;

namespace AutoHook.Conditions.Definitions;

public sealed class OceanTimeOfDayCD : IConditionDefinition {
    public string Id => nameof(OceanTimeOfDayCD);
    public string Name => "Ocean time of day";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.FishIgnore | ConditionScopeFlags.AutoCast;

    private const int DefaultTod = (int)TimeOfDay.Day;

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var wanted = (TimeOfDay)GetInt(parameters, "tod", DefaultTod);
        var result = world.OceanFishing.TimeOfDay == wanted;
        return GetBool(parameters, "inv", false) ? !result : result;
    }

    public void DrawParams(Condition condition) {
        // if this isn't set, it'll default to TimeOfDay.None, which will never be true
        if (!condition.Params.ContainsKey("tod"))
            condition.Params["tod"] = (long)DefaultTod;

        var tod = (TimeOfDay)Math.Clamp(GetInt(condition.Params, "tod", DefaultTod), 1, 3);

        ImGui.SetNextItemWidth(80.Scaled());
        var label = tod switch {
            TimeOfDay.Day => "Day",
            TimeOfDay.Sunset => "Sunset",
            TimeOfDay.Night => "Night",
            _ => "Day",
        };

        using var combo = ImRaii.Combo("Time of day", label);
        if (!combo)
            return;

        if (ImGui.Selectable("Day", tod == TimeOfDay.Day)) {
            condition.Params["tod"] = (long)TimeOfDay.Day;
        }

        if (ImGui.Selectable("Sunset", tod == TimeOfDay.Sunset)) {
            condition.Params["tod"] = (long)TimeOfDay.Sunset;
        }

        if (ImGui.Selectable("Night", tod == TimeOfDay.Night)) {
            condition.Params["tod"] = (long)TimeOfDay.Night;
        }
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters) {
        var tod = (TimeOfDay)GetInt(parameters, "tod", DefaultTod);
        return tod switch {
            TimeOfDay.Day => "day",
            TimeOfDay.Sunset => "sunset",
            TimeOfDay.Night => "night",
            _ => "day",
        };
    }
}
