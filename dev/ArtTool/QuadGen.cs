using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ArtTool;

/// <summary>
/// Procedural quad (ATV) sprites: a parametric 3D box/cylinder model rendered
/// with a z-buffered voxel rasterizer. Shading snaps to fixed material ramps so
/// the output reads as chunky 16-bit pixel art and always matches the palette.
///
/// v2: 32 compass directions (smooth rotation), and tree boxes render ON the
/// racks — 6 on the rear (3 long x 2 across), 2 on the front, loaded in order.
///
/// Atlas layout: frame index = (boxes * 32 + dir) * 2 + (rider ? 1 : 0),
/// boxes 0..8, dir 0..31 clockwise from N. Packed in rows of 64 frames.
/// </summary>
public static class QuadGen
{
    public const int Directions = 32;
    public const int BoxStates = 9; // 0..8 boxes

    private const float DEPTH = 0.6f;   // ground-depth squash (matches the oblique map bake)
    private const float ZSCALE = 0.9f;  // vertical scale
    private const int CELL_W = 58, CELL_H = 52;

    private record Part(float X0, float X1, float Y0, float Y1, float Z0, float Z1,
        Rgba32[] Ramp, bool Cylinder = false);

    // material ramps (dark -> light)
    private static readonly Rgba32[] Body = { new(110, 22, 22, 255), new(160, 32, 28, 255), new(198, 48, 40, 255), new(230, 84, 64, 255) };
    private static readonly Rgba32[] Tire = { new(14, 14, 16, 255), new(28, 28, 32, 255), new(46, 46, 52, 255) };
    private static readonly Rgba32[] Rack = { new(24, 24, 28, 255), new(42, 42, 48, 255), new(66, 66, 74, 255) };
    private static readonly Rgba32[] SeatM = { new(28, 28, 32, 255), new(44, 44, 50, 255), new(62, 62, 68, 255) };
    private static readonly Rgba32[] BoxW = { new(168, 164, 152, 255), new(204, 200, 188, 255), new(232, 228, 216, 255) };
    private static readonly Rgba32[] BoxLid = { new(30, 72, 42, 255), new(44, 96, 58, 255), new(64, 122, 76, 255) };
    private static readonly Rgba32[] VestR = { new(150, 64, 18, 255), new(222, 104, 32, 255), new(242, 150, 70, 255) };
    private static readonly Rgba32[] HatR = { new(168, 132, 24, 255), new(228, 186, 44, 255), new(246, 216, 92, 255) };
    private static readonly Rgba32[] SkinR = { new(160, 116, 86, 255), new(206, 160, 122, 255), new(224, 184, 148, 255) };
    private static readonly Rgba32[] PantsR = { new(40, 34, 28, 255), new(60, 50, 40, 255), new(80, 68, 54, 255) };
    private static readonly Rgba32[] SleeveR = { new(36, 44, 34, 255), new(52, 62, 48, 255), new(72, 84, 66, 255) };

    public static void ExportAtlas(string outDir, int seed)
    {
        int totalFrames = BoxStates * Directions * 2;
        const int cols = 64;
        int rows = (totalFrames + cols - 1) / cols;

        using var atlas = new Image<Rgba32>(cols * (CELL_W + 1), rows * (CELL_H + 1));
        var rects = new object[totalFrames];

        for (int boxes = 0; boxes < BoxStates; boxes++)
            for (int d = 0; d < Directions; d++)
                for (int rider = 0; rider < 2; rider++)
                {
                    int idx = (boxes * Directions + d) * 2 + rider;
                    using var frame = Render(d, rider == 1, boxes, seed);
                    int ax = idx % cols * (CELL_W + 1);
                    int ay = idx / cols * (CELL_H + 1);
                    for (int yy = 0; yy < CELL_H; yy++)
                        for (int xx = 0; xx < CELL_W; xx++)
                            if (frame[xx, yy].A > 0)
                                atlas[ax + xx, ay + yy] = frame[xx, yy];
                    rects[idx] = new { x = ax, y = ay, w = CELL_W, h = CELL_H };
                }

        Directory.CreateDirectory(outDir);
        atlas.SaveAsPng(Path.Combine(outDir, "QuadAtlas.png"));
        File.WriteAllText(Path.Combine(outDir, "QuadAtlas.json"),
            JsonSerializer.Serialize(new { sprites = rects }));
        Console.WriteLine($"wrote QuadAtlas.png ({atlas.Width}x{atlas.Height}, {Directions} dirs x 2 x {BoxStates} loads) + QuadAtlas.json");
    }

    private static Image<Rgba32> Render(int dirIndex, bool rider, int boxes, int seed)
    {
        var img = new Image<Rgba32>(CELL_W, CELL_H);
        var depthBuf = new float[CELL_W, CELL_H];
        for (int y = 0; y < CELL_H; y++)
            for (int x = 0; x < CELL_W; x++)
                depthBuf[x, y] = float.NegativeInfinity;

        float a = dirIndex * MathF.PI * 2f / Directions; // compass, clockwise from N
        float sinA = MathF.Sin(a), cosA = MathF.Cos(a);
        float zMax = rider ? 26.5f : 18f;
        // ground line near the cell bottom so the wheels touch the anchor (no hover)
        float cx0 = CELL_W / 2f, cy0 = CELL_H - 8f;

        foreach (var p in BuildParts(rider, boxes))
        {
            float rx = (p.X1 - p.X0) / 2f, rz = (p.Z1 - p.Z0) / 2f;
            float ccx = (p.X0 + p.X1) / 2f, ccz = (p.Z0 + p.Z1) / 2f;

            for (float mx = p.X0; mx <= p.X1; mx += 0.5f)
                for (float my = p.Y0; my <= p.Y1; my += 0.5f)
                    for (float mz = p.Z0; mz <= p.Z1; mz += 0.5f)
                    {
                        if (p.Cylinder)
                        {
                            float ex = (mx - ccx) / rx, ez = (mz - ccz) / rz;
                            if (ex * ex + ez * ez > 1f) continue;
                        }

                        // yaw rotate model (x fwd, y right) into screen ground coords
                        float gx = mx * sinA + my * cosA;
                        float gy = -mx * cosA + my * sinA;
                        int sx = (int)MathF.Round(cx0 + gx);
                        int sy = (int)MathF.Round(cy0 + gy * DEPTH - mz * ZSCALE);
                        if (sx < 0 || sy < 0 || sx >= CELL_W || sy >= CELL_H) continue;
                        if (gy <= depthBuf[sx, sy]) continue;
                        depthBuf[sx, sy] = gy;

                        // shading: height + top-face pop + hash dither, snapped to the ramp
                        float shade = 0.45f + 0.5f * (mz / zMax);
                        if (mz > p.Z1 - 0.6f) shade += 0.18f;
                        if (p.Cylinder)
                        {
                            float er = MathF.Sqrt(MathF.Pow((mx - ccx) / rx, 2) + MathF.Pow((mz - ccz) / rz, 2));
                            if (er > 0.8f) shade -= 0.18f;
                            else if (er < 0.3f) shade += 0.25f;
                        }
                        shade += Noise.Hash((int)(mx * 2) + 37, (int)(my * 2) * 57 + (int)(mz * 2), seed) * 0.14f - 0.07f;
                        int idx = Math.Clamp((int)(shade * p.Ramp.Length), 0, p.Ramp.Length - 1);
                        img[sx, sy] = p.Ramp[idx];
                    }
        }
        return img;
    }

    private static List<Part> BuildParts(bool rider, int boxes)
    {
        var P = new List<Part>
        {
            // wheels: cylinders along Y (fat, knobby via dither)
            new(7, 13, 6, 10, 0, 7.5f, Tire, Cylinder: true),
            new(7, 13, -10, -6, 0, 7.5f, Tire, Cylinder: true),
            new(-13, -7, 6, 10.5f, 0, 8f, Tire, Cylinder: true),
            new(-13, -7, -10.5f, -6, 0, 8f, Tire, Cylinder: true),
            // frame between the axles
            new(-9, 9, -4, 4, 3.5f, 6, Rack),
            // red plastics: main body, nose, rear fender
            new(-10, 10, -6, 6, 6, 10, Body),
            new(10, 14.5f, -5, 5, 6.5f, 10.5f, Body),
            new(-14.5f, -10, -5, 5, 6.5f, 10, Body),
            // tank + seat
            new(1, 7, -3, 3, 10, 13, Body),
            new(-8, 1, -3.5f, 3.5f, 10, 12.5f, SeatM),
            // racks: front + a long rear rack that takes 3x2 boxes
            new(10.5f, 17, -5.5f, 5.5f, 10.5f, 11.5f, Rack),
            new(-21, -10.5f, -6, 6, 10.5f, 11.5f, Rack),
            // handlebar stem + bar
            new(6.5f, 8, -0.9f, 0.9f, 13, 16.5f, Rack),
            new(7, 8.5f, -5.5f, 5.5f, 16, 17.5f, Rack),
        };

        // tree boxes: rear rack fills first (3 long x 2 across), then front (2 across).
        // Each box = cream body + green lid strip.
        var slots = new (float x0, float x1, float y0, float y1)[]
        {
            (-14.2f, -10.8f, 0.6f, 5.6f),   // rear row nearest the seat
            (-14.2f, -10.8f, -5.6f, -0.6f),
            (-17.8f, -14.4f, 0.6f, 5.6f),   // rear middle row
            (-17.8f, -14.4f, -5.6f, -0.6f),
            (-21.2f, -18f, 0.6f, 5.6f),     // rear back row
            (-21.2f, -18f, -5.6f, -0.6f),
            (11.2f, 15f, 0.6f, 5.6f),       // front rack
            (11.2f, 15f, -5.6f, -0.6f),
        };
        for (int b = 0; b < Math.Min(boxes, slots.Length); b++)
        {
            var s = slots[b];
            P.Add(new Part(s.x0, s.x1, s.y0, s.y1, 11.5f, 16f, BoxW));
            P.Add(new Part(s.x0 + 0.4f, s.x1 - 0.4f, s.y0 + 0.4f, s.y1 - 0.4f, 16f, 16.6f, BoxLid));
        }

        if (rider)
        {
            // thighs straddling the seat, calves down to the pegs
            P.Add(new Part(-6, 0, 3.5f, 6, 9.5f, 13, PantsR));
            P.Add(new Part(-6, 0, -6, -3.5f, 9.5f, 13, PantsR));
            P.Add(new Part(-6, -3, 4, 6.5f, 4.5f, 9.5f, PantsR));
            P.Add(new Part(-6, -3, -6.5f, -4, 4.5f, 9.5f, PantsR));
            // torso leaning into the bars (orange vest)
            P.Add(new Part(-5, 1, -3, 3, 12.5f, 17, VestR));
            P.Add(new Part(-3.5f, 2.5f, -3, 3, 17, 21, VestR));
            // arms reaching the bars + hands
            P.Add(new Part(1, 7.5f, 2.2f, 4.2f, 16, 18.5f, SleeveR));
            P.Add(new Part(1, 7.5f, -4.2f, -2.2f, 16, 18.5f, SleeveR));
            P.Add(new Part(6.5f, 8.5f, 3, 4.6f, 16.5f, 18, SkinR));
            P.Add(new Part(6.5f, 8.5f, -4.6f, -3, 16.5f, 18, SkinR));
            // head + hardhat with forward brim
            P.Add(new Part(-2, 2, -2, 2, 21, 24.5f, SkinR));
            P.Add(new Part(-2.6f, 2.6f, -2.6f, 2.6f, 24, 26.5f, HatR));
            P.Add(new Part(2.2f, 4.4f, -2.4f, 2.4f, 24, 25, HatR));
        }
        return P;
    }
}
