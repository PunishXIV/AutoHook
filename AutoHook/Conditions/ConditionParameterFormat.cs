using Lumina.Excel.Sheets;
using static AutoHook.Conditions.IConditionDefinition;

namespace AutoHook.Conditions;

public static class ConditionParameterFormat {
    public static string FormatIntCompare(
        IReadOnlyDictionary<string, object> p,
        string valueKey = "val",
        int defaultValue = 0,
        string defaultOp = ">=") {
        var args = GetIntCompareParams(p, valueKey, defaultValue, defaultOp);
        var op = args.Op switch {
            "<=" => "≤",
            ">=" => "≥",
            _ => args.Op,
        };
        var text = $"{op} {args.Value}";
        return args.Invert ? $"NOT ({text})" : text;
    }

    public static string FormatDoubleCompare(
        IReadOnlyDictionary<string, object> p,
        string valueKey = "val",
        double defaultValue = 0,
        string defaultOp = ">=") {
        var value = GetDouble(p, valueKey, defaultValue);
        var op = GetOp(p, "op", defaultOp);
        var inv = GetBool(p, "inv", false);
        var text = $"{op} {value:g}";
        return inv ? $"NOT ({text})" : text;
    }

    public static string FormatRanges(IReadOnlyDictionary<string, object> p) {
        var ranges = GetRanges(p);
        if (ranges.Count == 0)
            return "no window";
        return string.Join(", ", ranges.Select(r => $"{r.min:0.#}–{r.max:0.#}"));
    }

    public static string FormatStatusNames(IReadOnlyList<uint> ids) {
        if (ids.Count == 0)
            return "any status";
        return string.Join(", ", ids.Select(id => Status.GetRow(id).Name));
    }

    public static string FormatWeather(IReadOnlyDictionary<string, object> p) {
        var slot = GetOp(p, "slot", "current");
        var ids = GetWeatherIds(p);
        var weather = ids.Count == 0
            ? "any"
            : string.Join(", ", ids.Select(id => Weather.GetRow(id).Name));
        return $"{slot} = {weather}";
    }

    public static string FormatBaitId(uint baitId)
        => baitId == 0 ? "any bait" : Item.GetRow(baitId).Name.ToString();

    public static string FormatFishCount(IReadOnlyDictionary<string, object> p) {
        var fishId = GetInt(p, "id", 0);
        var fish = fishId > 0 ? Item.GetRow((uint)fishId).Name.ToString() : "any fish";
        return $"{fish} {FormatIntCompare(p)}";
    }

    public static string FormatSwimbaitCount(IReadOnlyDictionary<string, object> p) {
        var fishId = GetInt(p, "id", 0);
        var fish = fishId == 0 ? "slot fish" : Item.GetRow((uint)fishId).Name.ToString();
        return $"{fish} {FormatIntCompare(p)}";
    }

    public static string FormatGenericParams(IReadOnlyDictionary<string, object> p) {
        if (p.Count == 0)
            return string.Empty;

        return string.Join(", ", p
            .Where(kv => kv.Key is not "inv")
            .Select(kv => $"{kv.Key}={FormatParamValue(kv.Value)}"));
    }

    private static string FormatParamValue(object value) => value switch {
        List<object> list => string.Join("/", list.Select(FormatParamValue)),
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? string.Empty,
    };
}
