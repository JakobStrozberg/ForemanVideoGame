using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ArtTool;

/// <summary>Procedural prop sprites (man-made objects built from the master palette).</summary>
public static class PropGen
{
    /// <summary>
    /// Tree cache: silver tarp A-frame over stacked seedling boxes, crossed poles at the peak.
    /// Width/height chosen by caller via targetW; aspect fixed.
    /// </summary>
    public static Image<Rgba32> Cache(int seed, int targetW)
    {
        const int W = 96, H = 74;
        var img = new Image<Rgba32>(W, H);
        var rng = new Random(seed);

        int apexX = W / 2, apexY = 8;
        int baseY = 52;   // tarp hem
        int groundY = H - 2;

        // crossed peak poles
        for (int i = 0; i < 10; i++)
        {
            Put(img, apexX - 5 + i, apexY - 8 + i, GamePalette.Trunk[1]);
            Put(img, apexX + 5 - i, apexY - 8 + i, GamePalette.Trunk[1]);
        }

        // tarp: A-frame, lit left face, shaded right face, ragged hem
        for (int y = apexY; y <= baseY; y++)
        {
            float t = (y - apexY) / (float)(baseY - apexY);
            int hw = (int)(t * (W / 2 - 4));
            int hem = y == baseY ? (int)(Noise.Hash(y, 0, seed) * 3) : 0;
            for (int x = -hw + hem; x <= hw - hem; x++)
            {
                float lt = x < 0 ? 0.8f : 0.45f;                       // left face lit
                lt += Noise.Hash(x, y, seed + 2) * 0.15f - 0.07f;      // fabric noise
                if (MathF.Abs(x) > hw - 3) lt -= 0.25f;                // rolled edge
                Put(img, apexX + x, y, GamePalette.Ramp(GamePalette.Tarp, lt));
            }
        }

        // stacked seedling boxes under the tarp mouth (front row + half-hidden back row)
        DrawBoxRow(img, rng, y: baseY + 2, count: 3, offsetX: 14, boxW: 20, boxH: 9, dim: true);
        DrawBoxRow(img, rng, y: baseY + 8, count: 4, offsetX: 6, boxW: 20, boxH: 11, dim: false);

        // ground shadow line
        for (int x = 4; x < W - 4; x++)
            if (Noise.Hash(x, 0, seed + 5) > 0.3f)
                Put(img, x, groundY, GamePalette.Soil[0] with { A = 120 });

        // scale to target width
        if (targetW != W)
        {
            int targetH = H * targetW / W;
            var scaled = img.Clone(c => c.Resize(targetW, targetH, KnownResamplers.NearestNeighbor));
            img.Dispose();
            return scaled;
        }
        return img;
    }

    private static void DrawBoxRow(Image<Rgba32> img, Random rng, int y, int count, int offsetX, int boxW, int boxH, bool dim)
    {
        for (int b = 0; b < count; b++)
        {
            int bx = offsetX + b * (boxW + 2) + rng.Next(-1, 2);
            for (int yy = 0; yy < boxH; yy++)
                for (int xx = 0; xx < boxW; xx++)
                {
                    Rgba32 c;
                    if (yy == boxH - 1 || xx == boxW - 1) c = GamePalette.BoxSide;      // bottom/right shade
                    else if (yy >= boxH / 2 - 1 && yy <= boxH / 2) c = GamePalette.BoxStripe; // green band
                    else c = GamePalette.BoxFace;
                    if (dim) c = new Rgba32((byte)(c.R * 0.75f), (byte)(c.G * 0.75f), (byte)(c.B * 0.75f), 255);
                    Put(img, bx + xx, y + yy, c);
                }
        }
    }

    /// <summary>Key-prompt / state badge: rounded square with a white pixel glyph.</summary>
    public static Image<Rgba32> Badge(char letter, Rgba32? background = null)
    {
        string[] glyph = letter switch
        {
            'E' => new[] { "1111", "1000", "1110", "1000", "1111" },
            'Q' => new[] { "0110", "1001", "1001", "1011", "0111" },
            'C' => new[] { "0111", "1000", "1000", "1000", "0111" },
            'T' => new[] { "1111", "0110", "0110", "0110", "0110" },
            '!' => new[] { "0110", "0110", "0110", "0000", "0110" },
            'V' => new[] { "0001", "0011", "1110", "1100", "0000" }, // checkmark
            _ => new[] { "1111", "1001", "1001", "1001", "1111" }
        };

        const int S = 14;
        var img = new Image<Rgba32>(S, S);
        var bg = background ?? new Rgba32(38, 38, 46, 235);
        var border = new Rgba32(210, 214, 220, 255);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                bool corner = (x == 0 || x == S - 1) && (y == 0 || y == S - 1);
                if (corner) continue;
                bool edge = x == 0 || y == 0 || x == S - 1 || y == S - 1;
                img[x, y] = edge ? border : bg;
            }
        // letter, 4x5 glyph scaled x2, centered
        for (int gy = 0; gy < 5; gy++)
            for (int gx = 0; gx < 4; gx++)
            {
                if (glyph[gy][gx] != '1') continue;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                        img[3 + gx * 2 + sx, 2 + gy * 2 + sy] = new Rgba32(240, 240, 244, 255);
            }
        return img;
    }

    /// <summary>
    /// Debris obstacles: fallen logs at 6 angles + 2 stumps. These are world
    /// objects the quad must drive around (people just climb over them).
    /// Sprites drawn in the oblique view (y squashed 0.7), anchored bottom-center.
    /// </summary>
    public static void ExportDebrisAtlas(string outDir)
    {
        const int W = 56, H = 40, N = 8;
        using var atlas = new Image<Rgba32>(N * (W + 1), H);
        var rects = new List<object>();

        for (int v = 0; v < N; v++)
        {
            using var cell = new Image<Rgba32>(W, H);
            if (v < 6)
            {
                // log: thick shaded cylinder at angle v*30deg, squashed in y
                float ang = v * 30f * MathF.PI / 180f;
                float ca = MathF.Cos(ang), sa = MathF.Sin(ang) * 0.7f;
                var rng = new Random(v * 41 + 3);
                float halfLen = 20f + rng.Next(0, 5);
                for (float t = -halfLen; t <= halfLen; t += 0.5f)
                {
                    int cx = W / 2 + (int)(ca * t);
                    int cy = H / 2 + (int)(sa * t);
                    for (int dy = -4; dy <= 4; dy++)
                        for (int dx = -4; dx <= 4; dx++)
                        {
                            if (dx * dx + dy * dy > 16) continue;
                            int px = cx + dx, py = cy + dy - 3; // lifted: it's a round log
                            if (px < 0 || px >= W || py < 0 || py >= H) continue;
                            var col = dy < -1 ? GamePalette.Wood[2] : dy > 2 ? GamePalette.Wood[0] : GamePalette.Wood[1];
                            if (Noise.Hash(px, py, v) > 0.9f) col = GamePalette.Wood[0]; // bark noise
                            cell[px, py] = col;
                        }
                }
                // cut face at the near end (its rect is exported as the
                // horizon cross-section source)
                int ex = W / 2 + (int)(ca * halfLen);
                int ey = H / 2 + (int)(sa * halfLen) - 3;
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        if (dx * dx / 9f + dy * dy / 16f > 1) continue;
                        int px = ex + dx, py = ey + dy;
                        if (px < 0 || px >= W || py < 0 || py >= H) continue;
                        cell[px, py] = dx * dx + dy * dy < 6 ? GamePalette.Wood[3] : GamePalette.Wood[2];
                    }
            }
            else
            {
                // stump: side wall + lit top face with growth ring
                int r = 6 + (v - 6) * 2;
                int cx = W / 2, topY = H / 2;
                for (int y = 0; y < 8; y++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int px = cx + dx, py = topY + y;
                        if (px < 0 || px >= W || py >= H) continue;
                        if (MathF.Abs(dx) > r - 1) continue;
                        cell[px, py] = Noise.Hash(px, py, v) > 0.85f ? GamePalette.Trunk[0] : GamePalette.Trunk[1];
                    }
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx / (float)(r * r) + dy * dy / 16f > 1) continue;
                        int px = cx + dx, py = topY + dy;
                        if (px < 0 || px >= W || py < 0) continue;
                        float rr = dx * dx / (float)(r * r) + dy * dy / 16f;
                        cell[px, py] = rr > 0.72f ? GamePalette.Wood[1] : rr > 0.3f ? GamePalette.Wood[3] : GamePalette.Wood[2];
                    }
            }

            // bottom-align: the lowest opaque row must sit on the anchor line,
            // otherwise the object hovers above its shadow in-game
            int maxRow = -1, minRow = H, minCol = W, maxCol = -1;
            for (int y = H - 1; y >= 0; y--)
                for (int x = 0; x < W; x++)
                    if (cell[x, y].A > 0)
                    {
                        if (maxRow < 0) maxRow = y;
                        if (y < minRow) minRow = y;
                        if (x < minCol) minCol = x;
                        if (x > maxCol) maxCol = x;
                    }
            int shift = maxRow >= 0 ? H - 1 - maxRow : 0;

            int ox = v * (W + 1);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (cell[x, y].A == 0) continue;
                    int py = y + shift;
                    if (py < H) atlas[ox + x, py] = cell[x, y];
                }
            if (v < 6)
            {
                // axis line of the log within the (bottom-aligned) cell:
                // column(row) = x0 + (row - y0) * dx
                float angV = v * 30f * MathF.PI / 180f;
                float saV = MathF.Sin(angV);
                float dxPerRow = MathF.Abs(saV) < 0.15f ? 0f : MathF.Cos(angV) / (0.7f * saV);
                rects.Add(new { x = ox, y = 0, w = W, h = H,
                    axis = new { x0 = W / 2f, y0 = H / 2f - 3f + shift, dx = dxPerRow,
                        top = minRow + shift, left = minCol, right = maxCol } });
            }
            else
            {
                rects.Add(new { x = ox, y = 0, w = W, h = H });
            }
        }

        atlas.SaveAsPng(Path.Combine(outDir, "DebrisAtlas.png"));
        File.WriteAllText(Path.Combine(outDir, "DebrisAtlas.json"),
            System.Text.Json.JsonSerializer.Serialize(new { sprites = rects }));
        Console.WriteLine("wrote DebrisAtlas.png (6 logs + 2 stumps, bottom-aligned) + DebrisAtlas.json");
    }

    /// <summary>
    /// Vegetation: 3 grass tufts + 3 low bushes, bottom-aligned. Scattered as
    /// world objects (no collision — just life on the block).
    /// </summary>
    public static void ExportVegAtlas(string outDir)
    {
        const int W = 20, H = 16, N = 6;
        using var atlas = new Image<Rgba32>(N * (W + 1), H);
        var rects = new List<object>();

        for (int v = 0; v < N; v++)
        {
            int ox = v * (W + 1);
            var rng = new Random(v * 53 + 9);
            if (v < 3)
            {
                // grass tuft: fanned blades from a base point
                int blades = 6 + rng.Next(4);
                for (int b = 0; b < blades; b++)
                {
                    float ang = -MathF.PI / 2 + (b - blades / 2f) * 0.28f
                        + ((float)rng.NextDouble() - 0.5f) * 0.15f;
                    int len = 6 + rng.Next(5);
                    var col = GamePalette.Grass[1 + rng.Next(3)];
                    for (int t = 0; t < len; t++)
                    {
                        int px = ox + W / 2 + (int)(MathF.Cos(ang) * t * 0.6f);
                        int py = H - 1 - (int)(MathF.Sin(-ang) * t);
                        if (px >= ox && px < ox + W && py >= 0 && py < H)
                            atlas[px, py] = col;
                    }
                }
            }
            else
            {
                // low bush: dithered mound, darker at the base
                int rw = 7 + rng.Next(3), rh = 5 + rng.Next(2);
                for (int y = -rh; y <= 0; y++)
                    for (int x = -rw; x <= rw; x++)
                    {
                        float e = x * (float)x / (rw * rw) + y * (float)y / (rh * rh);
                        if (e > 1f) continue;
                        if (Noise.Hash(ox + x, y, v) < e * 0.55f) continue; // ragged edge
                        float lt = 0.65f - e * 0.3f + (Noise.Hash(x, y, v * 7) - 0.5f) * 0.3f;
                        int px = ox + W / 2 + x, py = H - 3 + y;
                        if (px >= ox && px < ox + W && py >= 0 && py < H)
                            atlas[px, py] = GamePalette.Ramp(GamePalette.Grass, lt);
                    }
                // base shadow
                for (int x = -rw + 1; x < rw; x++)
                    if (Noise.Hash(x, 0, v) > 0.4f)
                        atlas[ox + W / 2 + x, H - 2] = GamePalette.Grass[0];
            }
            rects.Add(new { x = ox, y = 0, w = W, h = H });
        }

        atlas.SaveAsPng(Path.Combine(outDir, "VegAtlas.png"));
        File.WriteAllText(Path.Combine(outDir, "VegAtlas.json"),
            System.Text.Json.JsonSerializer.Serialize(new { sprites = rects }));
        Console.WriteLine("wrote VegAtlas.png (3 grass + 3 bush) + VegAtlas.json");
    }

    /// <summary>Tiny planted-seedling sprites, 4 variants in a row (7x10 each).</summary>
    public static void ExportSeedlingAtlas(string outDir)
    {
        const int W = 7, H = 10, N = 4;
        using var atlas = new Image<Rgba32>(N * (W + 1), H);
        var rects = new List<object>();
        for (int v = 0; v < N; v++)
        {
            int ox = v * (W + 1);
            var rng = new Random(v * 71 + 5);
            // stem
            for (int y = 5; y < H; y++)
                atlas[ox + W / 2, y] = GamePalette.Trunk[1];
            // foliage: small ragged diamond
            for (int y = 0; y < 7; y++)
            {
                int hw = y < 4 ? y / 2 + 1 : (7 - y);
                for (int x = -hw; x <= hw; x++)
                {
                    if (rng.NextDouble() < 0.25) continue;
                    var col = GamePalette.Conifer[2 + rng.Next(3)];
                    int px = ox + W / 2 + x;
                    if (px >= ox && px < ox + W) atlas[px, y] = col;
                }
            }
            rects.Add(new { x = ox, y = 0, w = W, h = H });
        }
        atlas.SaveAsPng(Path.Combine(outDir, "SeedlingAtlas.png"));
        File.WriteAllText(Path.Combine(outDir, "SeedlingAtlas.json"),
            System.Text.Json.JsonSerializer.Serialize(new { sprites = rects }));
        Console.WriteLine("wrote SeedlingAtlas.png + SeedlingAtlas.json");
    }

    /// <summary>
    /// Minimal 3x5 bitmap font (digits, A-Z, a little punctuation) for HUD and
    /// score text. Atlas order matches FontChars; the game scales it up.
    /// </summary>
    public const string FontChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ:!-./";

    private static readonly string[][] FontGlyphs =
    {
        new[]{"111","101","101","101","111"}, // 0
        new[]{"010","110","010","010","111"}, // 1
        new[]{"111","001","111","100","111"}, // 2
        new[]{"111","001","111","001","111"}, // 3
        new[]{"101","101","111","001","001"}, // 4
        new[]{"111","100","111","001","111"}, // 5
        new[]{"111","100","111","101","111"}, // 6
        new[]{"111","001","010","010","010"}, // 7
        new[]{"111","101","111","101","111"}, // 8
        new[]{"111","101","111","001","111"}, // 9
        new[]{"010","101","111","101","101"}, // A
        new[]{"110","101","110","101","110"}, // B
        new[]{"011","100","100","100","011"}, // C
        new[]{"110","101","101","101","110"}, // D
        new[]{"111","100","110","100","111"}, // E
        new[]{"111","100","110","100","100"}, // F
        new[]{"011","100","101","101","011"}, // G
        new[]{"101","101","111","101","101"}, // H
        new[]{"111","010","010","010","111"}, // I
        new[]{"001","001","001","101","010"}, // J
        new[]{"101","110","100","110","101"}, // K
        new[]{"100","100","100","100","111"}, // L
        new[]{"101","111","111","101","101"}, // M
        new[]{"110","101","101","101","101"}, // N
        new[]{"010","101","101","101","010"}, // O
        new[]{"110","101","110","100","100"}, // P
        new[]{"010","101","101","110","011"}, // Q
        new[]{"110","101","110","110","101"}, // R
        new[]{"011","100","010","001","110"}, // S
        new[]{"111","010","010","010","010"}, // T
        new[]{"101","101","101","101","111"}, // U
        new[]{"101","101","101","101","010"}, // V
        new[]{"101","101","111","111","101"}, // W
        new[]{"101","101","010","101","101"}, // X
        new[]{"101","101","010","010","010"}, // Y
        new[]{"111","001","010","100","111"}, // Z
        new[]{"000","010","000","010","000"}, // :
        new[]{"010","010","010","000","010"}, // !
        new[]{"000","000","111","000","000"}, // -
        new[]{"000","000","000","000","010"}, // .
        new[]{"001","001","010","100","100"}, // /
    };

    public static void ExportFont(string outDir)
    {
        int n = FontGlyphs.Length;
        using var atlas = new Image<Rgba32>(n * 4, 5);
        var white = new Rgba32(255, 255, 255, 255);
        for (int g = 0; g < n; g++)
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 3; x++)
                    if (FontGlyphs[g][y][x] == '1')
                        atlas[g * 4 + x, y] = white;

        atlas.SaveAsPng(Path.Combine(outDir, "FontAtlas.png"));
        Console.WriteLine($"wrote FontAtlas.png ({n} glyphs)");
    }

    private static void Put(Image<Rgba32> img, int x, int y, Rgba32 col)
    {
        if (x >= 0 && y >= 0 && x < img.Width && y < img.Height) img[x, y] = col;
    }
}
