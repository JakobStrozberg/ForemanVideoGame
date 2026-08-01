using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ArtTool;

/// <summary>
/// Procedural block compositor. Environment art (ground, trees, roads, boundaries)
/// is generated from code + GamePalette; only man-made props (trucks, cache tents)
/// are sprite brushes. Outputs map PNG + tile terrain JSON from the same geometry,
/// so art and collision can never drift apart.
/// </summary>
public static class Compositor
{
    private const char FOREST = 'F';
    private const char SLASH = 'S';
    private const char CREAM = 'C';
    private const char SWAMP = 'W';
    private const char ROCK = 'X';
    private const char ROAD = 'R';
    private const char TRAIL = 'T';
    private const char OBSTACLE = 'O';

    /// <summary>
    /// Oblique projection bake: world Y is compressed by SQUASH at generation
    /// time (tiles are 32 wide x 22 tall), ground textures squash with it, and
    /// upright objects (trees, props) stay unsquashed — the classic 3/4 view,
    /// baked into the art so the runtime camera stays flat.
    /// </summary>
    public const float SQUASH = 0.7f;

    /// <summary>
    /// Terrain relief: a smooth heightfield displaces every ground column upward
    /// (voxel-terrain style — south slopes stretch, back slopes hide behind
    /// crests) with hillshade baked in. The PNG gets MAX_ELEV rows of transparent
    /// top padding so hilltops near the camera's far edge can poke over the
    /// horizon band at runtime.
    /// </summary>
    public const int MAX_ELEV = 60;

    public static void Compose(BlockDef def, string brushDir, string outDir)
    {
        int ts = def.TileSize;
        int th = (int)(ts * SQUASH);
        int wPx = def.Width * ts, hPx = def.Height * th;
        Directory.CreateDirectory(outDir);

        // --- geometry ---
        var spans = BuildBoundarySpans(def, wPx, hPx);
        bool InsideBlock(float x, float y)
        {
            if (y < 0 || y >= hPx) return false;
            foreach (var (xs, xe) in spans[(int)y])
                if (x >= xs && x < xe) return true;
            return false;
        }

        var leaveBlobs = def.LeavePatches.Select((b, i) => MakeBlob(b, ts, th, def.Seed + 80 + i)).ToList();
        var roadPaths = RoadPath.Build(def, ts, th);

        bool InAny(List<(Rectangle bbox, Func<float, float, bool> test)> blobs, float x, float y)
        {
            foreach (var (bbox, test) in blobs)
                if (x >= bbox.Left && x < bbox.Right && y >= bbox.Top && y < bbox.Bottom && test(x, y))
                    return true;
            return false;
        }

        // --- procedural textures ---
        using var slashTex = TextureGen.Slash(def.Seed);
        using var creamTex = TextureGen.Cream(def.Seed);
        using var swampTex = TextureGen.Swamp(def.Seed);
        using var rockTex = TextureGen.Rock(def.Seed);
        using var floorTex = TextureGen.ForestFloor(def.Seed);

        // land-type regions: later entries paint over earlier ones
        var landBlobs = def.LandRegions.Select((r, i) =>
        {
            var (bbox, test) = MakeBlob(r, ts, th, def.Seed + 40 + i);
            (Image<Rgba32> tex, char mark) = r.Type.ToLowerInvariant() switch
            {
                "cream" => (creamTex, CREAM),
                "swamp" => (swampTex, SWAMP),
                "rock" => (rockTex, ROCK),
                _ => throw new ArgumentException($"unknown land type {r.Type}")
            };
            return (bbox, test, tex, mark);
        }).ToList();

        using var canvas = new Image<Rgba32>(wPx, hPx);

        // --- 1. ground ---
        int g = TextureGen.GroundSize;

        // Per-texture mean colors: ground pixels blend toward their mean so the
        // raw texture mottle stays QUIET — elevation shading, contours, and
        // hypsometric tint own the value range instead of fighting dirt noise
        Rgba32 MeanOf(Image<Rgba32> t)
        {
            long mr = 0, mg = 0, mb = 0; int n = 0;
            for (int yy = 0; yy < t.Height; yy += 8)
                for (int xx = 0; xx < t.Width; xx += 8)
                { var c = t[xx, yy]; mr += c.R; mg += c.G; mb += c.B; n++; }
            return new Rgba32((byte)(mr / n), (byte)(mg / n), (byte)(mb / n), 255);
        }
        var texMeans = new Dictionary<Image<Rgba32>, Rgba32>
        {
            [slashTex] = MeanOf(slashTex), [creamTex] = MeanOf(creamTex),
            [swampTex] = MeanOf(swampTex), [rockTex] = MeanOf(rockTex),
            [floorTex] = MeanOf(floorTex),
        };
        const float CALM = 0.40f;
        for (int y = 0; y < hPx; y++)
            for (int x = 0; x < wPx; x++)
            {
                // Region boundaries jittered by low-frequency noise: transitions
                // become ragged organic edges instead of visible arcs/lines
                float bx = x + (Noise.Fbm(x, y, def.Seed + 300, 2, 26f, 65536) - 0.5f) * 16f;
                float by = y + (Noise.Fbm(x, y, def.Seed + 310, 2, 26f, 65536) - 0.5f) * 16f;

                Image<Rgba32> tex;
                if (!InsideBlock(x, y) || InAny(leaveBlobs, bx, by)) tex = floorTex;
                else
                {
                    tex = slashTex;
                    foreach (var lb in landBlobs)
                        if (bx >= lb.bbox.Left && bx < lb.bbox.Right && by >= lb.bbox.Top && by < lb.bbox.Bottom
                            && lb.test(bx, by))
                            tex = lb.tex; // later regions win
                }

                // sample ground through the oblique squash; PLAIN wrap — the
                // old per-cell random flips broke the textures' seamless tiling
                // and stamped visible grid lines at every cell edge
                int wy = (int)(y / SQUASH);
                var raw = tex[x % g, wy % g];
                var mean = texMeans[tex];
                canvas[x, y] = new Rgba32(
                    (byte)(raw.R + (mean.R - raw.R) * CALM),
                    (byte)(raw.G + (mean.G - raw.G) * CALM),
                    (byte)(raw.B + (mean.B - raw.B) * CALM),
                    raw.A);
            }

        // --- 2. roads: smooth splines rendered by distance field. The strip texture's
        //     x-axis follows arc length (ruts bend with the curve), y-axis is the
        //     signed side offset; noisy strip alpha dissolves the edges into ground. ---
        foreach (var path in roadPaths.OrderBy(p => p.IsTrail)) // roads first, trails on top
        {
            int widthPx = (int)(path.Half * 2);
            using var strip = TextureGen.RoadStrip(def.Seed + 7, widthPx, path.IsTrail);
            float reach = path.Half + path.EdgePx + 2;

            int x0 = Math.Max(0, (int)(path.MinX - reach)), x1 = Math.Min(wPx, (int)(path.MaxX + reach) + 1);
            int y0 = Math.Max(0, (int)(path.MinY - reach)), y1 = Math.Min(hPx, (int)(path.MaxY + reach) + 1);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    if (!path.Query(x, y, reach, out _, out float u, out float v)) continue;
                    int vv = (int)(v + strip.Height / 2f);
                    if (vv < 0 || vv >= strip.Height) continue;
                    var s = strip[Noise.Mod((int)u, strip.Width), vv];
                    if (s.A == 0) continue;
                    if (s.A == 255) { canvas[x, y] = s; continue; }
                    var d = canvas[x, y];
                    float a = s.A / 255f;
                    canvas[x, y] = new Rgba32(
                        (byte)(s.R * a + d.R * (1 - a)),
                        (byte)(s.G * a + d.G * (1 - a)),
                        (byte)(s.B * a + d.B * (1 - a)), 255);
                }
        }

        // --- 3. trees: NOT baked into the map. Positions export to <Name>.trees.json;
        //     the game draws them at runtime (y-sorted, cresting the horizon tips-first).
        //     Variant index picks a sprite from the TreeAtlas (arttool sprites). ---
        var trees = new List<(int x, int y, int v)>();
        const int cell = 18;
        for (int gy = -2; gy < hPx / cell + 2; gy++)
            for (int gx = -2; gx < wPx / cell + 2; gx++)
            {
                float px = gx * cell + Noise.Hash(gx, gy, def.Seed + 11) * cell;
                float py = gy * cell + Noise.Hash(gx, gy, def.Seed + 12) * cell;
                bool forest = !InsideBlock(Math.Clamp(px, 0, wPx - 1), Math.Clamp(py, 0, hPx - 1))
                              || InAny(leaveBlobs, px, py);
                if (!forest) continue;
                // generous corridor: keep trees well clear of every road (fsr,
                // block road, trails) so the way onto the block reads open
                if (RoadPath.NearAny(roadPaths, px, py, 26)) continue;
                int v = (int)(Noise.Hash(gx, gy, def.Seed + 13) * TreeGen.AtlasVariants) % TreeGen.AtlasVariants;
                trees.Add(((int)px, (int)py, v));
            }
        trees.Sort((a, b) => a.y.CompareTo(b.y));

        // --- 4. props. Caches are NOT baked into maps — the player places them at
        //     runtime (the game draws the generated Cache.png sprite). ---
        using var truck = CropToOpaque(Image.Load<Rgba32>(Path.Combine(brushDir, "Truck.png")));
        var propMarks = new List<(int x, int y, int w, char mark)>();
        foreach (var p in def.Props)
        {
            if (p.Type.ToLowerInvariant() != "truck")
                throw new ArgumentException($"unknown prop type {p.Type} (caches are player-placed, not map props)");
            const int widthTiles = 4;
            int tw = widthTiles * ts;
            using var sprite = truck.Clone(c => c.Resize(tw, truck.Height * tw / truck.Width));
            int cx = p.Tile[0] * ts + ts / 2, cy = p.Tile[1] * th + th / 2;
            Blit(canvas, sprite, cx - sprite.Width / 2, cy - sprite.Height / 2);
            propMarks.Add((p.Tile[0], p.Tile[1], widthTiles, OBSTACLE));
        }

        // --- 5. terrain grid from the same geometry ---
        var grid = new char[def.Height, def.Width];
        for (int tyy = 0; tyy < def.Height; tyy++)
            for (int txx = 0; txx < def.Width; txx++)
            {
                float cxp = txx * ts + ts / 2f, cyp = tyy * th + th / 2f;
                char c;
                // pad by ~1/3 tile: narrow roads still mark a continuous corridor
                var road = RoadPath.CharAt(roadPaths, cxp, cyp, ts * 0.35f);
                if (road == 'R' || road == 'T') c = road;
                else if (!InsideBlock(cxp, cyp) || InAny(leaveBlobs, cxp, cyp)) c = FOREST;
                else
                {
                    c = SLASH;
                    foreach (var lb in landBlobs)
                        if (cxp >= lb.bbox.Left && cxp < lb.bbox.Right && cyp >= lb.bbox.Top && cyp < lb.bbox.Bottom
                            && lb.test(cxp, cyp))
                            c = lb.mark; // later regions win
                }
                grid[tyy, txx] = c;
            }
        foreach (var (px, py, w, mark) in propMarks)
            for (int x = px - w / 2; x < px - w / 2 + w; x++)
                if (x >= 0 && x < def.Width && py >= 0 && py < def.Height)
                    grid[py, x] = mark;

        // --- 6. terrain relief: heightfield + hillshade + column displacement ---
        var elev = new float[wPx * hPx];
        for (int y = 0; y < hPx; y++)
            for (int x = 0; x < wPx; x++)
            {
                // fBm clusters around 0.5 — stretch it so valleys hit 0 and hills hit MAX_ELEV.
                // Big cells: broad, readable hills instead of high-frequency mottle
                float n = Noise.Fbm(x, y / SQUASH, def.Seed + 400, 3, 560f, 65536);
                n = Math.Clamp((n - 0.30f) / 0.40f, 0f, 1f);
                n = n * n * (3 - 2 * n); // smoothstep for soft crests

                // terrain ZONING: a much larger mask gates the hills, so big
                // stretches of the map are genuinely flat (horizon opens to the
                // sky View) and others roll — contrast makes both feel real
                float zone = Noise.Fbm(x, y / SQUASH, def.Seed + 450, 2, 1200f, 65536);
                zone = Math.Clamp((zone - 0.42f) / 0.25f, 0f, 1f);
                zone = zone * zone * (3 - 2 * zone);

                elev[y * wPx + x] = n * zone * MAX_ELEV * (float)def.Hilliness;
            }
        float E(int x, int y) => elev[Math.Clamp(y, 0, hPx - 1) * wPx + Math.Clamp(x, 0, wPx - 1)];

        int reliefH = hPx + MAX_ELEV;
        var relief = new Image<Rgba32>(wPx, reliefH); // transparent where sky shows through
        for (int x = 0; x < wPx; x++)
        {
            int minPy = reliefH; // nothing drawn yet; walk near -> far, only draw above
            for (int y = hPx - 1; y >= 0; y--)
            {
                float e = E(x, y);
                int py = y + MAX_ELEV - (int)e;
                if (py >= minPy) continue; // hidden behind nearer terrain

                // hillshade with strong lit/shade asymmetry: NW-facing slopes
                // bright, SE-facing slopes clearly dark — each hill gets a sun
                // side and a shade side, the primary depth read
                float light = (E(x, y + 4) - e) * 0.90f
                            + (E(x - 4, y) - e) * 0.42f
                            + e / MAX_ELEV * 0.34f - 0.10f;

                // curvature light: convex crests catch the sun as a bright rim,
                // concave hollows sink into ambient shade — the "form" cue
                float eAvg = (E(x, y + 6) + E(x, y - 6) + E(x + 6, y) + E(x - 6, y)) * 0.25f;
                light += (e - eAvg) * 0.06f;

                // contour banding: periodic tint by elevation makes knolls and
                // dips legible at a glance
                light += MathF.Sin(e / MAX_ELEV * MathF.PI * 6f) * 0.085f;

                // terrain cast shadows: the SAME sun (high NW) the sprites use.
                // March the ray toward the sun; taller ground on it throws a
                // real shadow lobe SE — distance-faded penumbra
                for (int k = 6; k <= 72; k += 6)
                    if (E(x - k, y - (int)(k * SQUASH)) > e + k * 0.16f)
                    {
                        light -= 0.26f * (1f - k / 90f);
                        break;
                    }

                // topo contour lines + terracing: each elevation band is a
                // visibly distinct brightness step, separated by a crisp dark
                // contour line — cartographic depth that cannot be misread as
                // dirt color. Five bands over MAX_ELEV.
                float bandStep = MAX_ELEV / 5f;
                int band = (int)(e / bandStep);
                light += (band - 2) * 0.05f;
                if (band != (int)(E(x, y + 2) / bandStep) || band != (int)(E(x + 2, y) / bandStep))
                    light -= 0.35f;

                light = Math.Clamp(light, -0.40f, 0.55f);

                // hypsometric tint: valleys cool and dark, hilltops warm and
                // pale — altitude carried by HUE, not just brightness
                float alt = e / MAX_ELEV;
                float rm = (1f + light) * (0.90f + 0.20f * alt);
                float gm = (1f + light) * (0.94f + 0.11f * alt);
                float bm = (1f + light) * (1.05f - 0.14f * alt);

                var src = canvas[x, y];
                var col = new Rgba32(
                    (byte)Math.Clamp((int)(src.R * rm), 0, 255),
                    (byte)Math.Clamp((int)(src.G * gm), 0, 255),
                    (byte)Math.Clamp((int)(src.B * bm), 0, 255),
                    src.A);

                for (int fy = minPy - 1; fy >= py; fy--)
                    relief[x, fy] = col;
                minPy = py;
            }
        }

        // --- 7. unify pixel grid ---
        if (def.PixelSize > 1)
        {
            relief.Mutate(c =>
            {
                c.Resize(new ResizeOptions
                {
                    Size = new Size(wPx / def.PixelSize, reliefH / def.PixelSize),
                    Sampler = KnownResamplers.NearestNeighbor
                });
                c.Resize(new ResizeOptions
                {
                    Size = new Size(wPx, reliefH),
                    Sampler = KnownResamplers.NearestNeighbor
                });
            });
        }

        // --- output ---
        string pngPath = Path.Combine(outDir, $"{def.Name}.png");
        relief.SaveAsPng(pngPath);
        relief.Dispose();

        var rows = new string[def.Height];
        for (int y = 0; y < def.Height; y++)
        {
            var chars = new char[def.Width];
            for (int x = 0; x < def.Width; x++) chars[x] = grid[y, x];
            rows[y] = new string(chars);
        }

        // per-tile elevation, hex 0..F of MAX_ELEV (game interpolates between tiles)
        var elevRows = new string[def.Height];
        for (int y = 0; y < def.Height; y++)
        {
            var chars = new char[def.Width];
            for (int x = 0; x < def.Width; x++)
            {
                float e = E(x * ts + ts / 2, y * th + th / 2);
                chars[x] = "0123456789ABCDEF"[Math.Clamp((int)(e / MAX_ELEV * 15.99f), 0, 15)];
            }
            elevRows[y] = new string(chars);
        }

        var tiles = new
        {
            name = def.Name,
            tileSize = ts,
            tileHeight = th,
            maxElev = MAX_ELEV,
            width = def.Width,
            height = def.Height,
            legend = new Dictionary<string, object>
            {
                ["F"] = new { name = "forest", passable = false, speed = 0.0 },
                ["S"] = new { name = "slash", passable = true, speed = 0.55 },
                ["C"] = new { name = "cream", passable = true, speed = 0.85 },
                ["W"] = new { name = "swamp", passable = true, speed = 0.25 },
                ["X"] = new { name = "rock", passable = true, speed = 0.7 },
                ["R"] = new { name = "road", passable = true, speed = 1.0 },
                ["T"] = new { name = "trail", passable = true, speed = 0.8 },
                ["O"] = new { name = "obstacle", passable = false, speed = 0.0 },
            },
            grid = rows,
            elev = elevRows
        };
        File.WriteAllText(Path.Combine(outDir, $"{def.Name}.tiles.json"),
            JsonSerializer.Serialize(tiles, new JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllText(Path.Combine(outDir, $"{def.Name}.trees.json"),
            JsonSerializer.Serialize(new
            {
                trees = trees.Select(t => new[] { t.x, t.y, t.v }).ToArray()
            }));

        // debris obstacles: fallen logs + stumps scattered on slash, off the roads.
        // Same {trees:[[x,y,v]]} shape so the game reuses the TreeLayer loader.
        var debris = new List<(int x, int y, int v)>();
        for (int tyy = 0; tyy < def.Height; tyy++)
            for (int txx = 0; txx < def.Width; txx++)
            {
                if (grid[tyy, txx] != SLASH) continue;
                float roll = Noise.Hash(txx, tyy, def.Seed + 500);
                if (roll > 0.16f) continue;
                float px = txx * ts + ts / 2f + (Noise.Hash(txx, tyy, def.Seed + 501) - 0.5f) * ts * 0.8f;
                float py = tyy * th + th / 2f + (Noise.Hash(txx, tyy, def.Seed + 502) - 0.5f) * th * 0.8f;
                if (RoadPath.NearAny(roadPaths, px, py, 30)) continue;
                int v = Noise.Hash(txx, tyy, def.Seed + 503) < 0.72f
                    ? (int)(Noise.Hash(txx, tyy, def.Seed + 504) * 6) % 6
                    : 6 + ((int)(Noise.Hash(txx, tyy, def.Seed + 505) * 2) % 2);
                debris.Add(((int)px, (int)py, v));
            }
        debris.Sort((a, b) => a.y.CompareTo(b.y));

        // vegetation: grass tufts + bushes, no collision, denser than debris
        var veg = new List<(int x, int y, int v)>();
        for (int tyy = 0; tyy < def.Height; tyy++)
            for (int txx = 0; txx < def.Width; txx++)
            {
                char gch = grid[tyy, txx];
                float density = gch switch { 'S' => 0.40f, 'C' => 0.55f, 'W' => 0.30f, _ => 0f };
                if (density <= 0) continue;
                for (int k = 0; k < 2; k++)
                {
                    if (Noise.Hash(txx * 2 + k, tyy, def.Seed + 600) > density) continue;
                    float px = txx * ts + Noise.Hash(txx, tyy, def.Seed + 601 + k) * ts;
                    float py = tyy * th + Noise.Hash(txx, tyy, def.Seed + 602 + k) * th;
                    if (RoadPath.NearAny(roadPaths, px, py, 18)) continue;
                    int vv = (int)(Noise.Hash(txx, tyy, def.Seed + 603 + k) * 6) % 6;
                    veg.Add(((int)px, (int)py, vv));
                }
            }
        veg.Sort((a, b) => a.y.CompareTo(b.y));
        File.WriteAllText(Path.Combine(outDir, $"{def.Name}.veg.json"),
            JsonSerializer.Serialize(new
            {
                trees = veg.Select(d => new[] { d.x, d.y, d.v }).ToArray()
            }));
        File.WriteAllText(Path.Combine(outDir, $"{def.Name}.debris.json"),
            JsonSerializer.Serialize(new
            {
                trees = debris.Select(d => new[] { d.x, d.y, d.v }).ToArray()
            }));

        Console.WriteLine($"wrote {pngPath} ({wPx}x{reliefH}, {MAX_ELEV}px relief), {trees.Count} trees to {def.Name}.trees.json, {def.Name}.tiles.json");
    }

    // ---------- geometry ----------

    /// <summary>Irregular rectangle-ish radial polygon → per-row x spans of the block interior.</summary>
    private static List<(float xs, float xe)>[] BuildBoundarySpans(BlockDef def, int wPx, int hPx)
    {
        int ts = def.TileSize;
        int th = (int)(ts * SQUASH);
        var b = def.Boundary;
        float cx = (b.CenterTile != null ? b.CenterTile[0] : def.Width / 2f) * ts;
        float cy = (b.CenterTile != null ? b.CenterTile[1] : def.Height / 2f) * th;
        float hw = (b.ExtentTiles != null ? b.ExtentTiles[0] : def.Width / 2f - b.MarginTiles) * ts;
        float hh = (b.ExtentTiles != null ? b.ExtentTiles[1] : def.Height / 2f - b.MarginTiles) * th;

        const int N = 96;
        var pts = new PointF[N];
        for (int i = 0; i < N; i++)
        {
            float a = MathF.PI * 2 * i / N;
            float ca = MathF.Cos(a), sa = MathF.Sin(a);
            float rRect = MathF.Min(
                MathF.Abs(ca) > 1e-4f ? hw / MathF.Abs(ca) : float.MaxValue,
                MathF.Abs(sa) > 1e-4f ? hh / MathF.Abs(sa) : float.MaxValue);
            float n = Noise.Value(i / 6f, 0.37f, def.Seed + 21, N / 6);
            float r = rRect * (1f - (float)b.Roughness * n);
            pts[i] = new PointF(cx + ca * r, cy + sa * r);
        }

        var spans = new List<(float, float)>[hPx];
        for (int y = 0; y < hPx; y++)
        {
            float yc = y + 0.5f;
            var xs = new List<float>();
            for (int i = 0; i < N; i++)
            {
                var a = pts[i];
                var c = pts[(i + 1) % N];
                if ((a.Y <= yc && c.Y > yc) || (c.Y <= yc && a.Y > yc))
                    xs.Add(a.X + (yc - a.Y) * (c.X - a.X) / (c.Y - a.Y));
            }
            xs.Sort();
            var row = new List<(float, float)>();
            for (int i = 0; i + 1 < xs.Count; i += 2) row.Add((xs[i], xs[i + 1]));
            spans[y] = row;
        }
        return spans;
    }

    /// <summary>Organic blob = union of seeded circles with fbm-noisy edges. Returns bbox + test.</summary>
    private static (Rectangle bbox, Func<float, float, bool> test) MakeBlob(BlobDef b, int ts, int th, int seed)
    {
        var rng = new Random(seed);
        var rect = new RectangleF(b.X * ts, b.Y * th, b.W * ts, b.H * th);
        int k = Math.Clamp(b.W * b.H / 24, 4, 12);
        var circles = new (float cx, float cy, float r)[k];
        for (int i = 0; i < k; i++)
        {
            circles[i] = (
                rect.Left + rect.Width * (0.2f + 0.6f * (float)rng.NextDouble()),
                rect.Top + rect.Height * (0.2f + 0.6f * (float)rng.NextDouble()),
                MathF.Min(rect.Width, rect.Height) * (0.22f + 0.2f * (float)rng.NextDouble()));
        }
        var bbox = new Rectangle((int)rect.Left - 24, (int)rect.Top - 24, (int)rect.Width + 48, (int)rect.Height + 48);

        bool Test(float x, float y)
        {
            float edge = (Noise.Fbm(x, y, seed + 5, 2, 14f, 4096) - 0.5f) * 20f;
            foreach (var (cx, cy, r) in circles)
            {
                float dx = x - cx, dy = y - cy;
                if (MathF.Sqrt(dx * dx + dy * dy) < r + edge) return true;
            }
            return false;
        }
        return (bbox, Test);
    }

    // ---------- raster helpers ----------

    private static void Blit(Image<Rgba32> dst, Image<Rgba32> src, int dx, int dy)
    {
        int x0 = Math.Max(0, -dx), y0 = Math.Max(0, -dy);
        int x1 = Math.Min(src.Width, dst.Width - dx), y1 = Math.Min(src.Height, dst.Height - dy);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var s = src[x, y];
                if (s.A == 0) continue;
                if (s.A == 255) { dst[x + dx, y + dy] = s; continue; }
                var d = dst[x + dx, y + dy];
                float a = s.A / 255f;
                dst[x + dx, y + dy] = new Rgba32(
                    (byte)(s.R * a + d.R * (1 - a)),
                    (byte)(s.G * a + d.G * (1 - a)),
                    (byte)(s.B * a + d.B * (1 - a)), 255);
            }
    }

    private static Image<Rgba32> CropToOpaque(Image<Rgba32> img)
    {
        int minX = img.Width, minY = img.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                if (img[x, y].A > 20)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return img;
        var cropped = img.Clone(c => c.Crop(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)));
        img.Dispose();
        return cropped;
    }
}
