using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ArtTool;

/// <summary>
/// Procedural people sprites, 16x22. Walk cycles are 4 frames
/// (contact - pass - contact - pass) with a 1px body bob on pass frames.
/// Facings: S front, N back, E side (W mirrors E at draw time).
/// Foreman atlas: 3 dirs x 4 frames = 12.
/// Planter atlas: per variant 12 walk frames + 1 planting pose = 13.
/// </summary>
public static class FigureGen
{
    public const int FrameW = 16, FrameH = 22;
    public const int WalkFrames = 4;
    public const int PlanterVariants = 4;

    private static readonly Rgba32[] CapColors =
    {
        new(180, 40, 40, 255), new(40, 80, 170, 255), new(40, 120, 60, 255), new(44, 44, 48, 255),
    };
    private static readonly Rgba32[] ShirtColors =
    {
        new(150, 150, 160, 255), new(178, 168, 120, 255), new(92, 112, 132, 255), new(158, 120, 140, 255),
    };

    public static void ExportAtlas(string outDir)
    {
        var frames = new List<Image<Rgba32>>();
        foreach (string dir in new[] { "S", "N", "E" })
            for (int f = 0; f < WalkFrames; f++)
                frames.Add(Figure(dir, f, foreman: true, variant: 0));
        SaveAtlas(frames, outDir, "ForemanAtlas");
    }

    public static void ExportPlanterAtlas(string outDir)
    {
        var frames = new List<Image<Rgba32>>();
        for (int v = 0; v < PlanterVariants; v++)
        {
            foreach (string dir in new[] { "S", "N", "E" })
                for (int f = 0; f < WalkFrames; f++)
                    frames.Add(Figure(dir, f, foreman: false, variant: v));
            frames.Add(PlanterPlanting(v));
        }
        SaveAtlas(frames, outDir, "PlanterAtlas");
    }

    private static void SaveAtlas(List<Image<Rgba32>> frames, string outDir, string name)
    {
        int atlasW = frames.Count * (FrameW + 1);
        using var atlas = new Image<Rgba32>(atlasW, FrameH);
        var rects = new List<object>();
        int x = 0;
        foreach (var f in frames)
        {
            for (int yy = 0; yy < FrameH; yy++)
                for (int xx = 0; xx < FrameW; xx++)
                    if (f[xx, yy].A > 0)
                        atlas[x + xx, yy] = f[xx, yy];
            rects.Add(new { x, y = 0, w = FrameW, h = FrameH });
            x += FrameW + 1;
            f.Dispose();
        }

        Directory.CreateDirectory(outDir);
        atlas.SaveAsPng(Path.Combine(outDir, $"{name}.png"));
        File.WriteAllText(Path.Combine(outDir, $"{name}.json"),
            JsonSerializer.Serialize(new { sprites = rects }));
        Console.WriteLine($"wrote {name}.png ({atlasW}x{FrameH}, {frames.Count} frames) + {name}.json");
    }

    /// <summary>One walk frame. Frames 0/2 = contact (stride, alternating lead leg), 1/3 = pass (legs together, body bobs up).</summary>
    private static Image<Rgba32> Figure(string dir, int frame, bool foreman, int variant)
    {
        var img = new Image<Rgba32>(FrameW, FrameH);
        int cx = FrameW / 2;
        bool side = dir == "E";
        bool contact = frame % 2 == 0;
        bool leadLeft = frame == 0;
        int oy = contact ? 0 : -1; // pass frames bob the body up 1px

        var pants = GamePalette.Pants;
        var boot = GamePalette.Boot;

        // ---- legs ----
        if (!side)
        {
            if (contact)
            {
                // planted lead leg full length; trail leg lifted and pulled in
                int lead = leadLeft ? cx - 3 : cx + 1;
                int trail = leadLeft ? cx + 1 : cx - 3;
                int trailIn = leadLeft ? 1 : -1;
                DrawRect(img, lead, 15, 2, 5, pants);
                DrawRect(img, lead, 20, 2, 2, boot);
                DrawRect(img, trail + trailIn, 15, 2, 4, pants);
                DrawRect(img, trail + trailIn, 18, 2, 2, boot);
            }
            else
            {
                DrawRect(img, cx - 3, 15 + oy, 2, 5, pants);
                DrawRect(img, cx + 1, 15 + oy, 2, 5, pants);
                DrawRect(img, cx - 3, 20 + oy, 2, 2, boot);
                DrawRect(img, cx + 1, 20 + oy, 2, 2, boot);
            }
        }
        else
        {
            if (contact)
            {
                int spread = leadLeft ? 2 : -2;
                DrawRect(img, cx - 1 + spread, 15, 2, 5, pants); // front leg
                DrawRect(img, cx - 1 + spread, 20, 2, 2, boot);
                DrawRect(img, cx - 1 - spread, 15, 2, 4, pants); // back leg lifted
                DrawRect(img, cx - 1 - spread, 18, 2, 2, boot);
            }
            else
            {
                DrawRect(img, cx - 1, 15 + oy, 2, 5, pants);
                DrawRect(img, cx - 1, 20 + oy, 2, 2, boot);
            }
        }

        // ---- torso ----
        var shirt = foreman ? GamePalette.Vest : ShirtColors[variant];
        int torsoW = side ? 5 : 8;
        DrawRect(img, cx - torsoW / 2, 8 + oy, torsoW, 7, shirt);

        if (foreman)
        {
            var stripe = GamePalette.VestStripe;
            if (!side)
            {
                DrawRect(img, cx - 3, 8 + oy, 1, 7, stripe);
                DrawRect(img, cx + 2, 8 + oy, 1, 7, stripe);
                DrawRect(img, cx - torsoW / 2, 11 + oy, torsoW, 1, stripe);
            }
            else DrawRect(img, cx - 1, 8 + oy, 1, 7, stripe);
        }
        else
        {
            // planting bags on the hips
            var bagCol = GamePalette.BoxFace;
            if (!side)
            {
                DrawRect(img, cx - torsoW / 2 - 2, 12 + oy, 3, 4, bagCol);
                DrawRect(img, cx + torsoW / 2 - 1, 12 + oy, 3, 4, bagCol);
            }
            else DrawRect(img, cx - 4, 12 + oy, 3, 4, bagCol);
        }

        // ---- arms (swing opposite the lead leg) ----
        var sleeve = foreman
            ? GamePalette.Sleeve
            : new Rgba32((byte)(shirt.R * 0.7f), (byte)(shirt.G * 0.7f), (byte)(shirt.B * 0.7f), 255);
        if (!side)
        {
            int swing = contact ? (leadLeft ? 1 : -1) : 0;
            DrawRect(img, cx - torsoW / 2 - 1, 9 + oy + swing, 1, 4, sleeve);
            DrawRect(img, cx + torsoW / 2, 9 + oy - swing, 1, 4, sleeve);
        }
        else
        {
            int swing = contact ? (leadLeft ? 2 : -2) : 0;
            DrawRect(img, cx - 1 + swing, 9 + oy, 2, 4, sleeve);
        }

        // ---- head ----
        var skin = dir == "N" ? GamePalette.SkinShade : GamePalette.Skin;
        DrawRect(img, cx - 2, 4 + oy, side ? 4 : 5, 4, skin);
        if (dir == "S")
        {
            img[cx - 1, 5 + oy] = boot;
            img[cx + 1, 5 + oy] = boot;
        }

        // ---- headgear ----
        if (foreman)
        {
            DrawRect(img, cx - 3, 1 + oy, side ? 5 : 6, 3, GamePalette.HardHat);
            DrawRect(img, cx - 4, 3 + oy, side ? 7 : 8, 1, GamePalette.HardHatShade);
        }
        else
        {
            var cap = CapColors[variant];
            DrawRect(img, cx - 3, 2 + oy, side ? 5 : 6, 2, cap);
            if (dir == "S") DrawRect(img, cx - 3, 4 + oy, 6, 1, cap);
            else if (side) DrawRect(img, cx + 1, 4 + oy, 3, 1, cap);
        }

        return img;
    }

    /// <summary>Bent-over side-view planting pose with shovel.</summary>
    private static Image<Rgba32> PlanterPlanting(int variant)
    {
        var img = new Image<Rgba32>(FrameW, FrameH);
        int cx = FrameW / 2;
        var shirt = ShirtColors[variant];
        var cap = CapColors[variant];

        DrawRect(img, cx - 2, 15, 2, 5, GamePalette.Pants);
        DrawRect(img, cx + 1, 15, 2, 5, GamePalette.Pants);
        DrawRect(img, cx - 2, 20, 2, 2, GamePalette.Boot);
        DrawRect(img, cx + 1, 20, 2, 2, GamePalette.Boot);
        DrawRect(img, cx - 3, 11, 8, 4, shirt);                 // horizontal torso
        DrawRect(img, cx - 4, 13, 3, 4, GamePalette.BoxFace);   // hip bag hangs
        DrawRect(img, cx + 4, 12, 3, 3, GamePalette.Skin);      // head down front
        DrawRect(img, cx + 3, 11, 4, 2, cap);
        DrawRect(img, cx + 5, 14, 1, 6, GamePalette.Trunk[1]);  // shovel shaft
        DrawRect(img, cx + 4, 20, 3, 1, GamePalette.Trunk[3]);  // blade
        return img;
    }

    private static void DrawRect(Image<Rgba32> img, int x, int y, int w, int h, Rgba32 col)
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                if (xx >= 0 && yy >= 0 && xx < img.Width && yy < img.Height)
                    img[xx, yy] = col;
    }
}
