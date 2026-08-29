using SixLabors.ImageSharp.PixelFormats;

namespace ArtTool;

/// <summary>
/// The master 16-bit palette. Every procedural generator picks exclusively from
/// these ramps, which is what makes independently generated assets read as one game.
/// </summary>
public static class GamePalette
{
    private static Rgba32 C(byte r, byte g, byte b) => new(r, g, b, 255);

    // Soil / slash debris — warm and light so cutblock separates hard from forest
    public static readonly Rgba32[] Soil =
    {
        C(62, 42, 26), C(90, 62, 38), C(118, 84, 52), C(146, 106, 66), C(174, 132, 86),
    };

    // Dead wood / logs — weathered tan
    public static readonly Rgba32[] Wood =
    {
        C(96, 72, 48), C(140, 110, 74), C(178, 145, 100), C(205, 175, 128),
    };

    // Cream ground — soft plantable soil
    public static readonly Rgba32[] Cream =
    {
        C(120, 96, 62), C(150, 122, 80), C(176, 148, 102), C(196, 170, 124),
    };

    // Conifer greens — dark, cool, blue-leaning so forest separates hard from dirt
    public static readonly Rgba32[] Conifer =
    {
        C(8, 30, 26), C(12, 42, 34), C(18, 56, 42), C(26, 74, 50), C(38, 92, 58),
    };

    // Ground vegetation / grass — dark, sits in forest shadow under the canopy
    public static readonly Rgba32[] Grass =
    {
        C(20, 36, 24), C(28, 48, 30), C(38, 62, 36), C(50, 76, 42),
    };

    // Swamp — wet muddy grey-browns
    public static readonly Rgba32[] Swamp =
    {
        C(38, 36, 26), C(52, 50, 34), C(66, 62, 42), C(80, 76, 52),
    };

    // Standing water in swamp
    public static readonly Rgba32[] Water =
    {
        C(28, 46, 48), C(38, 60, 62), C(50, 76, 76),
    };

    // Rock / stone ground
    public static readonly Rgba32[] Stone =
    {
        C(84, 84, 80), C(108, 108, 102), C(134, 134, 126), C(162, 160, 150),
    };


    // Foreman figure
    public static readonly Rgba32 HardHat = C(228, 186, 44);
    public static readonly Rgba32 HardHatShade = C(190, 148, 30);
    public static readonly Rgba32 Skin = C(206, 160, 122);
    public static readonly Rgba32 SkinShade = C(172, 128, 94);
    public static readonly Rgba32 Vest = C(222, 104, 32);
    public static readonly Rgba32 VestStripe = C(214, 214, 206);
    public static readonly Rgba32 Sleeve = C(52, 62, 48);
    public static readonly Rgba32 Pants = C(60, 50, 40);
    public static readonly Rgba32 Boot = C(34, 28, 22);

    // Cache tarp + tree boxes
    public static readonly Rgba32[] Tarp =
    {
        C(148, 152, 158), C(178, 182, 188), C(208, 212, 218), C(232, 234, 238),
    };
    public static readonly Rgba32 BoxFace = C(226, 222, 210);
    public static readonly Rgba32 BoxSide = C(192, 188, 176);
    public static readonly Rgba32 BoxStripe = C(44, 96, 58);

    // Road / gravel
    public static readonly Rgba32[] Road =
    {
        C(105, 82, 52), C(130, 103, 66), C(152, 123, 80), C(172, 143, 96),
    };

    // Trunks and snags
    public static readonly Rgba32[] Trunk =
    {
        C(58, 42, 30), C(82, 60, 42), C(110, 84, 58), C(150, 138, 120),
    };

    /// <summary>Pick from a ramp by 0..1 value, clamped.</summary>
    public static Rgba32 Ramp(Rgba32[] ramp, float t)
    {
        int i = (int)(t * ramp.Length);
        if (i < 0) i = 0;
        if (i >= ramp.Length) i = ramp.Length - 1;
        return ramp[i];
    }
}
