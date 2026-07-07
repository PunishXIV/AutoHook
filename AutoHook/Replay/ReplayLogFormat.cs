namespace AutoHook.Replay;

public static class ReplayLogFormatMagic {
    public const int Version = 2;

    public static readonly uint CompressedBinary = ToFourCC("AHCB");

    public static uint ToFourCC(string tag) {
        if (tag.Length != 4)
            throw new ArgumentException($"Replay tag must be 4 characters: '{tag}'", nameof(tag));
        return BitConverter.ToUInt32(Encoding.ASCII.GetBytes(tag));
    }

    public static string FromFourCC(uint value) {
        var bytes = BitConverter.GetBytes(value);
        return Encoding.ASCII.GetString(bytes);
    }
}
