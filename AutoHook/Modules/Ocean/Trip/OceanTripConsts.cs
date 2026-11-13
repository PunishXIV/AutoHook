using System.Numerics;

namespace AutoHook.Modules.Ocean;

public static class OceanTripConsts {
    public const uint DockTerritory = 129; // limsa
    public const uint DryskthotaId = 1005421;
    public static readonly Vector3 DryskthotaPosition = new(-409.908f, 3.9825f, 76.0126f);

    public const uint BaitVendorId = 1005422;
    public const uint BaitShopId = 263015;

    public const uint Ragworm = 29714;
    public const uint Krill = 29715;
    public const uint PlumpWorm = 29716;
    public const uint VersatileLure = 29717;

    public static readonly uint[] Baits = [Ragworm, Krill, PlumpWorm];
}
