using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ArtTool;

/// <summary>
/// Procedural side-view conifer sprites. Forests are thousands of individual
/// trees scattered and y-sorted, so any boundary shape reads naturally.
/// Trees ship as a sprite atlas + per-block position lists; the game draws them
/// at runtime so they can crest the horizon tips-first and occlude the player.
/// </summary>
public static class TreeGen
{
    public const int AtlasVariants = 16;

    /// <summary>
    /// Export the tree sprite atlas: variants at heights 40..78, packed in a row.
    /// TreeAtlas.json lists each sprite's rect (origin = bottom-center of the rect).
    /// </summary>
    public static void ExportAtlas(string outDir, int seed, int pixelSize)
    {
        var sprites = new Image<Rgba32>[AtlasVariants];
        for (int v = 0; v < AtlasVariants; v++)
        {
            int h = 40 + (int)(v * 38f / (AtlasVariants - 1)); // 40..78
            var img = Conifer(seed * 33 + v * 517, h);
            if (pixelSize > 1)
            {
                int ow = img.Width, oh = img.Height;
                img.Mutate(c =>
                {
                    c.Resize(new ResizeOptions
                    {
                        Size = new Size(Math.Max(1, ow / pixelSize), Math.Max(1, oh / pixelSize)),
                        Sampler = KnownResamplers.NearestNeighbor
                    });
                    c.Resize(new ResizeOptions
                    {
                        Size = new Size(ow, oh),
                        Sampler = KnownResamplers.NearestNeighbor
                    });
                });
            }
            sprites[v] = img;
        }

        int atlasW = sprites.Sum(s => s.Width) + AtlasVariants;
        int atlasH = sprites.Max(s => s.Height);
        using var atlas = new Image<Rgba32>(atlasW, atlasH);
        var rects = new List<object>();
        int x = 0;
        foreach (var s in sprites)
        {
            int y = atlasH - s.Height; // bottom-aligned
            for (int yy = 0; yy < s.Height; yy++)
                for (int xx = 0; xx < s.Width; xx++)
                    if (s[xx, yy].A > 0)
                        atlas[x + xx, y + yy] = s[xx, yy];
            rects.Add(new { x, y, w = s.Width, h = s.Height });
            x += s.Width + 1;
            s.Dispose();
        }

        Directory.CreateDirectory(outDir);
        atlas.SaveAsPng(Path.Combine(outDir, "TreeAtlas.png"));
        File.WriteAllText(Path.Combine(outDir, "TreeAtlas.json"),
            JsonSerializer.Serialize(new { sprites = rects }));
        Console.WriteLine($"wrote TreeAtlas.png ({atlasW}x{atlasH}, {AtlasVariants} variants) + TreeAtlas.json");
    }
    /// <summary>Generate a conifer sprite. Height in px; seed drives shape, shade, and snag chance.</summary>
    public static Image<Rgba32> Conifer(int seed, int height)
    {
        var rng = new Random(seed);
        bool snag = rng.NextDouble() < 0.04; // standing dead tree

        int maxHalf = Math.Max(3, (int)(height * 0.26f) + rng.Next(0, 3));
        int w = maxHalf * 2 + 3;
        var img = new Image<Rgba32>(w, height);
        int cx = w / 2;

        int trunkW = height > 60 ? 3 : 2;
        int canopyH = (int)(height * (snag ? 0.55f : 0.88f));
        float shadeJitter = (float)rng.NextDouble() * 0.25f; // per-tree tint variation

        // trunk
        var trunkRamp = GamePalette.Trunk;
        for (int y = height / 5; y < height; y++)
            for (int x = 0; x < trunkW; x++)
            {
                var c = snag ? trunkRamp[2] : trunkRamp[x == trunkW - 1 ? 0 : 1];
                img[cx - trunkW / 2 + x, y] = c;
            }

        if (snag)
        {
            // sparse broken branches
            for (int y = 4; y < canopyH; y += rng.Next(3, 6))
            {
                int len = (int)(maxHalf * (0.3f + 0.7f * y / (float)canopyH) * (0.5f + rng.NextDouble() * 0.5f));
                int dir = rng.NextDouble() < 0.5 ? -1 : 1;
                for (int x = 0; x < len; x++)
                {
                    int yy = y + x / 4; // slight droop
                    if (yy < height) img[Math.Clamp(cx + dir * x, 0, w - 1), yy] = trunkRamp[2];
                }
            }
            return img;
        }

        // living canopy: jagged triangle, lit from upper-left
        for (int y = 2; y < canopyH; y++)
        {
            float frac = y / (float)canopyH;
            float half = maxHalf * (0.1f + 0.9f * frac);
            half *= 0.85f + Noise.Hash(y, 0, seed) * 0.45f; // ragged rows
            int hw = Math.Max(1, (int)half);

            for (int x = -hw; x <= hw; x++)
            {
                // branch gaps near the edges
                float edge = MathF.Abs(x) / (half + 0.01f);
                if (Noise.Hash(x, y, seed + 3) < edge * 0.35f) continue;

                // lighting: left/top brighter; dither with hash
                float light = 0.62f - edge * 0.28f - x * 0.10f / (hw + 1) - frac * 0.18f + shadeJitter;
                light += Noise.Hash(x, y, seed + 5) * 0.22f - 0.11f;
                img[Math.Clamp(cx + x, 0, w - 1), y] = GamePalette.Ramp(GamePalette.Conifer, light);
            }
        }
        return img;
    }
}
