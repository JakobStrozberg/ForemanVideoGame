using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ArtTool;

/// <summary>
/// Parallax horizon layers for the in-game "View" band: sky, two distant ridges,
/// and a near treeline. All layers tile seamlessly in X (they scroll horizontally
/// with the camera). Same palette as the map, so the band never clashes.
/// </summary>
public static class HorizonGen
{
    public const int W = 1024, H = 240;

    public static void Generate(string outDir, int seed)
    {
        Directory.CreateDirectory(outDir);
        Sky(seed).SaveAsPng(Path.Combine(outDir, "HorizonSky.png"));
        Ridge(seed + 1, GamePalette.RidgeFar, baseFrac: 0.52f, ampPx: 46).SaveAsPng(Path.Combine(outDir, "HorizonFar.png"));
        Ridge(seed + 2, GamePalette.RidgeMid, baseFrac: 0.68f, ampPx: 34).SaveAsPng(Path.Combine(outDir, "HorizonMid.png"));
        Treeline(seed + 3).SaveAsPng(Path.Combine(outDir, "HorizonTree.png"));
        Console.WriteLine($"wrote 4 horizon layers to {outDir}");
    }

    private static Image<Rgba32> Sky(int seed)
    {
        var img = new Image<Rgba32>(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float t = y / (float)H + (Noise.Hash(x, y, seed) - 0.5f) * 0.06f;
                img[x, y] = GamePalette.Ramp(GamePalette.Sky, t);
            }

        // soft cloud banks
        var rng = new Random(seed);
        for (int i = 0; i < 9; i++)
        {
            int cx = rng.Next(W), cy = rng.Next(H / 8, H / 2);
            int rx = rng.Next(50, 110), ry = rng.Next(7, 16);
            for (int y = -ry; y <= ry; y++)
                for (int x = -rx; x <= rx; x++)
                {
                    float d = (x * (float)x) / (rx * rx) + (y * (float)y) / (ry * ry);
                    if (d > 1) continue;
                    if (Noise.Hash(cx + x, cy + y, seed + i) < d * 0.85f) continue; // fluffy edge
                    int px = Noise.Mod(cx + x, W), py = cy + y;
                    if (py < 0 || py >= H) continue;
                    var b = img[px, py];
                    img[px, py] = new Rgba32(
                        (byte)((b.R + GamePalette.Cloud.R * 2) / 3),
                        (byte)((b.G + GamePalette.Cloud.G * 2) / 3),
                        (byte)((b.B + GamePalette.Cloud.B * 2) / 3), 255);
                }
        }
        return img;
    }

    private static Image<Rgba32> Ridge(int seed, Rgba32 color, float baseFrac, int ampPx)
    {
        var img = new Image<Rgba32>(W, H);
        int baseY = (int)(H * baseFrac);
        for (int x = 0; x < W; x++)
        {
            // periodic 1D noise so the layer tiles in X
            float n = Noise.Value(x / 64f, 0.37f, seed, W / 64)
                    + Noise.Value(x / 22f, 0.71f, seed + 9, W / 22) * 0.35f;
            int top = baseY - (int)(n / 1.35f * ampPx);
            for (int y = top; y < H; y++)
            {
                // dithered top edge
                if (y == top && Noise.Hash(x, y, seed) < 0.5f) continue;
                img[x, y] = color;
            }
        }
        return img;
    }

    private static Image<Rgba32> Treeline(int seed)
    {
        var img = new Image<Rgba32>(W, H);
        var rng = new Random(seed);

        // Dense conifer silhouettes standing on the band's bottom edge: canopy ends
        // above the ground line and trunks run down to it, so the seam against the
        // map reads as a forest edge instead of sliced canopy.
        int x = 0;
        while (x < W + 30) // overshoot so wrap-around is seamless
        {
            int th = rng.Next(60, 115);
            int trunkLen = rng.Next(10, 18);
            int tw = Math.Max(8, th / 4 + rng.Next(-2, 3));
            var col = GamePalette.Conifer[rng.NextDouble() < 0.3 ? 1 : 0];
            int baseY = H - rng.Next(0, 4);
            int canopyBottom = baseY - trunkLen;
            int apexY = canopyBottom - (th - trunkLen);

            for (int y = Math.Max(0, apexY); y < canopyBottom; y++)
            {
                float frac = (y - apexY) / (float)(canopyBottom - apexY);
                int hw = Math.Max(1, (int)(tw / 2f * (0.12f + 0.88f * frac)));
                hw = Math.Max(1, hw + (int)((Noise.Hash(x, y, seed) - 0.5f) * 3));
                for (int dx = -hw; dx <= hw; dx++)
                    img[Noise.Mod(x + dx, W), y] = col;
            }

            // trunk down to the ground line
            int trunkW = th > 85 ? 3 : 2;
            for (int y = canopyBottom; y < baseY && y < H; y++)
                for (int dx = 0; dx < trunkW; dx++)
                    img[Noise.Mod(x - trunkW / 2 + dx, W), y] = GamePalette.Trunk[0];

            x += (int)(tw * 0.75f);
        }

        // dark undergrowth line where the forest meets the block
        for (int gx = 0; gx < W; gx++)
        {
            int gh = 2 + (int)(Noise.Hash(gx, 0, seed + 7) * 4);
            for (int y = H - gh; y < H; y++)
                img[gx, y] = GamePalette.Grass[Noise.Hash(gx, y, seed + 8) < 0.5f ? 0 : 1];
        }
        return img;
    }
}
