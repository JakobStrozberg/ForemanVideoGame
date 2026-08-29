using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ArtTool;

/// <summary>
/// Procedural, seamless (tileable) ground textures and road strips.
/// Everything draws exclusively from GamePalette ramps.
/// </summary>
public static class TextureGen
{
    public const int GroundSize = 512; // all ground textures are square and tile seamlessly

    // ---------- ground textures ----------

    /// <summary>Cutblock slash: dark churned soil, fallen logs, stumps, green tufts.</summary>
    public static Image<Rgba32> Slash(int seed)
    {
        var img = BaseSoil(seed, GamePalette.Soil, contrast: 1.0f);
        var rng = new Random(seed * 7 + 1);

        // small sticks only — big logs and stumps are real world objects now
        // (scattered by the compositor, rendered and collided in-game)
        int logs = 16;
        for (int i = 0; i < logs; i++)
        {
            float x = rng.Next(GroundSize), y = rng.Next(GroundSize);
            float ang = (float)(rng.NextDouble() * Math.PI);
            int len = rng.Next(14, 44);
            int thick = rng.Next(1, 3);
            float shade = 0.45f + (float)rng.NextDouble() * 0.5f;
            DrawLogWrapped(img, x, y, ang, len, thick, shade, seed + i);
        }

        // green tufts pushing through
        for (int i = 0; i < 120; i++)
        {
            int cx = rng.Next(GroundSize), cy = rng.Next(GroundSize);
            var g = GamePalette.Grass[rng.Next(2, 4)];
            for (int j = 0; j < rng.Next(2, 6); j++)
                Put(img, cx + rng.Next(-2, 3), cy + rng.Next(-2, 3), g);
        }
        return img;
    }

    /// <summary>Cream: soft open soil, sparse sticks, easy planting.</summary>
    public static Image<Rgba32> Cream(int seed)
    {
        var img = BaseSoil(seed + 500, GamePalette.Cream, contrast: 0.7f);
        var rng = new Random(seed * 11 + 2);

        for (int i = 0; i < 8; i++) // few thin sticks
        {
            float ang = (float)(rng.NextDouble() * Math.PI);
            DrawLogWrapped(img, rng.Next(GroundSize), rng.Next(GroundSize), ang, rng.Next(15, 40), 1,
                0.35f, seed + 700 + i);
        }
        for (int i = 0; i < 90; i++) // grass freckles
        {
            var g = GamePalette.Grass[rng.Next(2, 4)];
            Put(img, rng.Next(GroundSize), rng.Next(GroundSize), g);
        }
        return img;
    }

    /// <summary>Forest floor under the standing timber.</summary>
    public static Image<Rgba32> ForestFloor(int seed)
    {
        var img = new Image<Rgba32>(GroundSize, GroundSize);
        for (int y = 0; y < GroundSize; y++)
            for (int x = 0; x < GroundSize; x++)
            {
                float n = Noise.Fbm(x, y, seed + 900, 3, 28f, GroundSize);
                float d = Noise.Hash(x, y, seed + 901) * 0.18f;
                img[x, y] = GamePalette.Ramp(GamePalette.Grass, n * 0.9f + d - 0.05f);
            }
        return img;
    }

    /// <summary>Swamp: wet mud, standing water pools, reed tufts.</summary>
    public static Image<Rgba32> Swamp(int seed)
    {
        var img = BaseSoil(seed + 1300, GamePalette.Swamp, contrast: 0.8f);
        var rng = new Random(seed * 13 + 3);

        // water pools: blobby dark patches with lighter rim
        for (int i = 0; i < 14; i++)
        {
            int cx = rng.Next(GroundSize), cy = rng.Next(GroundSize);
            int r = rng.Next(10, 30);
            for (int y = -r - 2; y <= r + 2; y++)
                for (int x = -r - 2; x <= r + 2; x++)
                {
                    float d = MathF.Sqrt(x * x + y * y);
                    float wobble = (Noise.Hash(cx + x, cy + y, seed + i) - 0.5f) * 6f;
                    if (d + wobble < r - 2)
                        Put(img, cx + x, cy + y, GamePalette.Water[(x + y) % 7 == 0 ? 1 : 0]);
                    else if (d + wobble < r)
                        Put(img, cx + x, cy + y, GamePalette.Water[2]);
                }
        }

        // reeds
        for (int i = 0; i < 160; i++)
        {
            int cx = rng.Next(GroundSize), cy = rng.Next(GroundSize);
            var col = GamePalette.Grass[rng.Next(2, 4)];
            for (int j = 0; j < rng.Next(2, 5); j++)
                Put(img, cx + rng.Next(-1, 2), cy - j, col);
        }
        return img;
    }

    /// <summary>Rocky ground: stone base, scattered boulders lit from upper-left.</summary>
    public static Image<Rgba32> Rock(int seed)
    {
        var img = BaseSoil(seed + 1700, GamePalette.Stone, contrast: 0.75f);
        var rng = new Random(seed * 17 + 4);

        // boulders
        for (int i = 0; i < 60; i++)
        {
            int cx = rng.Next(GroundSize), cy = rng.Next(GroundSize);
            int r = rng.Next(3, 9);
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    if (x * x + y * y > r * r) continue;
                    float lt = 0.55f - (x + y) / (2.2f * r); // lit upper-left
                    Put(img, cx + x, cy + y, GamePalette.Ramp(GamePalette.Stone, lt));
                }
            // ground-contact shadow
            for (int x = -r; x <= r; x++)
                Put(img, cx + x, cy + r + 1, GamePalette.Stone[0]);
        }

        // dirt pockets
        for (int i = 0; i < 40; i++)
        {
            int cx = rng.Next(GroundSize), cy = rng.Next(GroundSize);
            for (int j = 0; j < rng.Next(3, 9); j++)
                Put(img, cx + rng.Next(-3, 4), cy + rng.Next(-3, 4), GamePalette.Soil[2]);
        }
        return img;
    }

    // ---------- road strips ----------

    /// <summary>
    /// Horizontal road strip, tileable along X. Total height = widthPx + 2*edgeFade;
    /// edges dissolve into transparency with noise so the road blends into ground.
    /// Ruts: darker wheel bands for roads, single center rut for trails.
    /// </summary>
    public static Image<Rgba32> RoadStrip(int seed, int widthPx, bool trail)
    {
        const int length = 512;
        int edge = trail ? 4 : 6;
        int h = widthPx + edge * 2;
        var img = new Image<Rgba32>(length, h);

        for (int y = 0; y < h; y++)
            for (int x = 0; x < length; x++)
            {
                float n = Noise.Fbm(x, y, seed + 300, 3, 18f, length);
                var col = GamePalette.Ramp(GamePalette.Road, n);

                // wheel ruts
                float fy = (y - edge) / (float)widthPx; // 0..1 across road body
                if (!trail)
                {
                    if (Near(fy, 0.28f, 0.09f) || Near(fy, 0.72f, 0.09f))
                        col = GamePalette.Ramp(GamePalette.Road, n * 0.5f);
                }
                else
                {
                    if (Near(fy, 0.5f, 0.16f))
                        col = GamePalette.Ramp(GamePalette.Road, n * 0.55f);
                }

                // noisy alpha edges; trails are patchy overall
                float distEdge = Math.Min(y, h - 1 - y); // px from strip edge
                float solidAt = edge + Noise.Value1D(x * 0.35f + y * 3.1f, seed + 400 + y, 180) * edge * 1.6f - edge * 0.8f;
                byte a = 255;
                if (distEdge < solidAt) a = 0;
                if (trail && Noise.Fbm(x, y, seed + 450, 2, 9f, length) > 0.72f) a = 0; // worn-through patches
                img[x, y] = col with { A = a };
            }
        return img;
    }

    // ---------- helpers ----------

    private static Image<Rgba32> BaseSoil(int seed, Rgba32[] ramp, float contrast)
    {
        var img = new Image<Rgba32>(GroundSize, GroundSize);
        for (int y = 0; y < GroundSize; y++)
            for (int x = 0; x < GroundSize; x++)
            {
                float n = Noise.Fbm(x, y, seed, 4, 32f, GroundSize);
                n = 0.5f + (n - 0.5f) * contrast;
                float dither = Noise.Hash(x, y, seed + 1) * 0.22f - 0.11f;
                img[x, y] = GamePalette.Ramp(ramp, n + dither);
            }
        return img;
    }

    private static void DrawLogWrapped(Image<Rgba32> img, float x, float y, float ang, int len, int thick, float shade, int seed)
    {
        float dx = MathF.Cos(ang), dy = MathF.Sin(ang);
        float px = -dy, py = dx; // perpendicular
        for (int t = 0; t < len; t++)
        {
            if (Noise.Hash(t, 0, seed) > 0.94f) continue; // breaks in the log
            for (int w = 0; w < thick; w++)
            {
                float lt = shade + (w == thick - 1 ? -0.3f : w == 0 ? 0.15f : 0f); // lit top, dark underside
                var col = GamePalette.Ramp(GamePalette.Wood, lt);
                Put(img, (int)(x + dx * t + px * w), (int)(y + dy * t + py * w), col);
            }
        }
    }

    private static void Put(Image<Rgba32> img, int x, int y, Rgba32 col) =>
        img[Noise.Mod(x, img.Width), Noise.Mod(y, img.Height)] = col;

    private static bool Near(float v, float target, float tol) => MathF.Abs(v - target) < tol;
}
