namespace AutoHook.FishSolver;

public static class DurationFormat {
    public static string MinutesSeconds(int totalSeconds) {
        var m = Math.Max(0, totalSeconds) / 60;
        var s = Math.Max(0, totalSeconds) % 60;
        return $"{m}m:{s:D2}s";
    }
}
