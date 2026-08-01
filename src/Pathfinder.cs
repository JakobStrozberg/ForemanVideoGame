using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace src;

/// <summary>
/// A* over the tile grid, 8-directional (diagonals only when both orthogonal
/// neighbors are walkable). Cost function maps terrain to a step cost;
/// float.PositiveInfinity blocks the tile.
/// </summary>
public static class Pathfinder
{
    private static readonly (int dx, int dy)[] Dirs =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    /// <summary>Returns world-space waypoints (tile centers) from start to goal, or null.</summary>
    public static List<Vector2> FindPath(TileMap map, Vector2 startWorld, Vector2 goalWorld,
        Func<TileTerrain, float> cost)
    {
        int ts = map.TileSize, th = map.TileHeight;
        int sx = (int)(startWorld.X / ts), sy = (int)(startWorld.Y / th);
        int gx = (int)(goalWorld.X / ts), gy = (int)(goalWorld.Y / th);
        sx = Math.Clamp(sx, 0, map.Width - 1); sy = Math.Clamp(sy, 0, map.Height - 1);
        gx = Math.Clamp(gx, 0, map.Width - 1); gy = Math.Clamp(gy, 0, map.Height - 1);
        if (float.IsPositiveInfinity(cost(map.TerrainAtTile(gx, gy)))) return null;

        int w = map.Width, h = map.Height, n = w * h;
        var g = new float[n];
        var parent = new int[n];
        var closed = new bool[n];
        Array.Fill(g, float.PositiveInfinity);
        Array.Fill(parent, -1);

        int start = sy * w + sx, goal = gy * w + gx;
        g[start] = 0;
        var open = new PriorityQueue<int, float>();
        open.Enqueue(start, Heuristic(sx, sy, gx, gy));

        while (open.TryDequeue(out int cur, out _))
        {
            if (cur == goal) break;
            if (closed[cur]) continue;
            closed[cur] = true;

            int cxx = cur % w, cyy = cur / w;
            foreach (var (dx, dy) in Dirs)
            {
                int nx = cxx + dx, ny = cyy + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int ni = ny * w + nx;
                if (closed[ni]) continue;

                float stepCost = cost(map.TerrainAtTile(nx, ny));
                if (float.IsPositiveInfinity(stepCost)) continue;
                if (dx != 0 && dy != 0)
                {
                    // no corner cutting
                    if (float.IsPositiveInfinity(cost(map.TerrainAtTile(cxx + dx, cyy))) ||
                        float.IsPositiveInfinity(cost(map.TerrainAtTile(cxx, cyy + dy)))) continue;
                    stepCost *= 1.41f;
                }

                float ng = g[cur] + stepCost;
                if (ng < g[ni])
                {
                    g[ni] = ng;
                    parent[ni] = cur;
                    open.Enqueue(ni, ng + Heuristic(nx, ny, gx, gy));
                }
            }
        }

        if (parent[goal] < 0 && goal != start) return null;

        var path = new List<Vector2>();
        for (int cur = goal; cur >= 0; cur = parent[cur])
        {
            path.Add(new Vector2(cur % w * ts + ts / 2f, cur / w * th + th / 2f));
            if (cur == start) break;
        }
        path.Reverse();
        return path;
    }

    private static float Heuristic(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x0 - x1), dy = Math.Abs(y0 - y1);
        return Math.Max(dx, dy) + 0.41f * Math.Min(dx, dy); // octile
    }
}
