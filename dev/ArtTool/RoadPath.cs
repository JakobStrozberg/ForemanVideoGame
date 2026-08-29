namespace ArtTool;

/// <summary>
/// A smooth road: Catmull-Rom spline through control points, sampled densely,
/// with bucketed nearest-sample queries. Rendering and terrain-grid marking both
/// query the same field, so the drawn road and the drivable road always match.
/// </summary>
public class RoadPath
{
    public List<float> Sx = new(), Sy = new();   // sample positions
    public List<float> Tx = new(), Ty = new();   // unit tangents
    public List<float> L = new();                // cumulative arc length
    public float Half;                           // body half-width px
    public int EdgePx;                           // fade zone px
    public bool IsTrail;
    public float MinX, MinY, MaxX, MaxY;

    private readonly Dictionary<(int bx, int by), List<int>> _buckets = new();
    private const int B = 32;

    public static List<RoadPath> Build(BlockDef def, int ts, int th)
    {
        var paths = new List<RoadPath>();
        foreach (var road in def.Roads)
        {
            if (road.Points.Count < 2) continue;
            var path = new RoadPath
            {
                Half = (float)(road.Width * ts / 2.0),
                IsTrail = road.Kind.ToLowerInvariant() == "trail",
            };
            path.EdgePx = path.IsTrail ? 4 : 6;

            var pts = road.Points.Select(p => (x: p[0] * ts + ts / 2f, y: p[1] * th + th / 2f)).ToList();
            // duplicate endpoints for Catmull-Rom
            var ext = new List<(float x, float y)> { pts[0] };
            ext.AddRange(pts);
            ext.Add(pts[^1]);

            for (int i = 0; i + 3 < ext.Count + 0; i++)
            {
                var (p0, p1, p2, p3) = (ext[i], ext[i + 1], ext[i + 2], ext[i + 3]);
                float chord = Dist(p1, p2);
                int steps = Math.Max(2, (int)(chord / 4f));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    var pos = CatmullRom(p0, p1, p2, p3, t);
                    path.AddSample(pos.x, pos.y);
                }
            }
            path.AddSample(pts[^1].x, pts[^1].y);
            path.Finish();
            paths.Add(path);
        }
        return paths;
    }

    private void AddSample(float x, float y)
    {
        if (Sx.Count > 0)
        {
            float dx = x - Sx[^1], dy = y - Sy[^1];
            float d = MathF.Sqrt(dx * dx + dy * dy);
            if (d < 1.5f) return; // dedupe
            L.Add(L[^1] + d);
            Tx.Add(dx / d);
            Ty.Add(dy / d);
        }
        else
        {
            L.Add(0);
            Tx.Add(1);
            Ty.Add(0);
        }
        Sx.Add(x);
        Sy.Add(y);
    }

    private void Finish()
    {
        MinX = Sx.Min(); MaxX = Sx.Max(); MinY = Sy.Min(); MaxY = Sy.Max();
        for (int i = 0; i < Sx.Count; i++)
        {
            var key = ((int)(Sx[i] / B), (int)(Sy[i] / B));
            if (!_buckets.TryGetValue(key, out var list)) _buckets[key] = list = new List<int>();
            list.Add(i);
        }
    }

    /// <summary>Distance/arc-length/side-offset of a point relative to the path, if within reach.</summary>
    public bool Query(float x, float y, float reach, out float dist, out float u, out float v)
    {
        dist = u = v = 0;
        if (x < MinX - reach || x > MaxX + reach || y < MinY - reach || y > MaxY + reach) return false;

        int r = (int)(reach / B) + 1;
        int bx = (int)(x / B), by = (int)(y / B);
        int best = -1;
        float bestD2 = reach * reach;
        for (int gy = by - r; gy <= by + r; gy++)
            for (int gx = bx - r; gx <= bx + r; gx++)
            {
                if (!_buckets.TryGetValue((gx, gy), out var list)) continue;
                foreach (int i in list)
                {
                    float dx = x - Sx[i], dy = y - Sy[i];
                    float d2 = dx * dx + dy * dy;
                    if (d2 < bestD2) { bestD2 = d2; best = i; }
                }
            }
        if (best < 0) return false;

        float ddx = x - Sx[best], ddy = y - Sy[best];
        dist = MathF.Sqrt(bestD2);
        u = L[best] + ddx * Tx[best] + ddy * Ty[best];
        v = ddx * -Ty[best] + ddy * Tx[best];
        return true;
    }

    public static bool NearAny(List<RoadPath> paths, float x, float y, float pad)
    {
        foreach (var p in paths)
            if (p.Query(x, y, p.Half + pad, out float d, out _, out _) && d <= p.Half + pad)
                return true;
        return false;
    }

    /// <summary>'R' for roads, 'T' for trails, '\0' for neither. Roads win at crossings.
    /// pad widens the test beyond the drawn body — tile-grid marking needs it so a
    /// road narrower than the tile size still produces a continuous drivable corridor.</summary>
    public static char CharAt(List<RoadPath> paths, float x, float y, float pad = 0f)
    {
        char result = '\0';
        foreach (var p in paths)
            if (p.Query(x, y, p.Half + pad, out float d, out _, out _) && d <= p.Half + pad)
            {
                if (!p.IsTrail) return 'R';
                result = 'T';
            }
        return result;
    }

    private static (float x, float y) CatmullRom((float x, float y) p0, (float x, float y) p1,
        (float x, float y) p2, (float x, float y) p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return (
            0.5f * (2 * p1.x + (-p0.x + p2.x) * t + (2 * p0.x - 5 * p1.x + 4 * p2.x - p3.x) * t2 +
                    (-p0.x + 3 * p1.x - 3 * p2.x + p3.x) * t3),
            0.5f * (2 * p1.y + (-p0.y + p2.y) * t + (2 * p0.y - 5 * p1.y + 4 * p2.y - p3.y) * t2 +
                    (-p0.y + 3 * p1.y - 3 * p2.y + p3.y) * t3));
    }

    private static float Dist((float x, float y) a, (float x, float y) b)
    {
        float dx = a.x - b.x, dy = a.y - b.y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
