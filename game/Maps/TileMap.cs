using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Crewboss.Maps;

public class TileTerrain
{
    public string Name = "unknown";
    public bool Passable = true;
    public float Speed = 1f;
}

/// <summary>
/// Terrain data layer generated alongside the map art by dev/ArtTool.
/// One char per tile; the legend defines passability and speed multipliers.
/// </summary>
public class TileMap
{
    public int TileSize { get; private set; }
    public int TileHeight { get; private set; } // oblique bake: tiles are wider than tall
    public int MaxElev { get; private set; }    // terrain relief amplitude (world px)
    public int Width { get; private set; }
    public int Height { get; private set; }

    private float[] _elev; // per-tile elevation in world px

    private string[] _grid = Array.Empty<string>();
    private readonly Dictionary<char, TileTerrain> _legend = new();
    private static readonly TileTerrain Default = new();

    public static TileMap Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        var map = new TileMap
        {
            TileSize = root.GetProperty("tileSize").GetInt32(),
            Width = root.GetProperty("width").GetInt32(),
            Height = root.GetProperty("height").GetInt32()
        };
        map.TileHeight = root.TryGetProperty("tileHeight", out var thEl) ? thEl.GetInt32() : map.TileSize;
        map.MaxElev = root.TryGetProperty("maxElev", out var meEl) ? meEl.GetInt32() : 0;

        if (root.TryGetProperty("elev", out var elevEl))
        {
            map._elev = new float[map.Width * map.Height];
            int row = 0;
            foreach (var r in elevEl.EnumerateArray())
            {
                string s = r.GetString() ?? "";
                for (int x = 0; x < map.Width && x < s.Length; x++)
                {
                    int v = s[x] <= '9' ? s[x] - '0' : s[x] - 'A' + 10;
                    map._elev[row * map.Width + x] = v / 15f * map.MaxElev;
                }
                row++;
            }
        }

        foreach (var entry in root.GetProperty("legend").EnumerateObject())
        {
            map._legend[entry.Name[0]] = new TileTerrain
            {
                Name = entry.Value.GetProperty("name").GetString() ?? "unknown",
                Passable = entry.Value.GetProperty("passable").GetBoolean(),
                Speed = (float)entry.Value.GetProperty("speed").GetDouble()
            };
        }

        var rows = new List<string>();
        foreach (var row in root.GetProperty("grid").EnumerateArray())
            rows.Add(row.GetString() ?? "");
        map._grid = rows.ToArray();

        return map;
    }

    public TileTerrain TerrainAtTile(int tx, int ty)
    {
        if (tx < 0 || ty < 0 || tx >= Width || ty >= Height) return new TileTerrain { Passable = false, Speed = 0f, Name = "outside" };
        char c = _grid[ty][tx];
        return _legend.TryGetValue(c, out var t) ? t : Default;
    }

    public TileTerrain TerrainAtWorld(Vector2 worldPos) =>
        TerrainAtTile((int)(worldPos.X / TileSize), (int)(worldPos.Y / TileHeight));

    public bool IsPassable(Vector2 worldPos) => TerrainAtWorld(worldPos).Passable;

    /// <summary>Ground elevation in world px, bilinearly interpolated between tile centers.</summary>
    public float ElevationAt(Vector2 worldPos)
    {
        if (_elev == null) return 0f;
        float fx = worldPos.X / TileSize - 0.5f;
        float fy = worldPos.Y / TileHeight - 0.5f;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float tx = fx - x0, ty = fy - y0;

        float S(int x, int y) => _elev[Math.Clamp(y, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)];
        float top = S(x0, y0) * (1 - tx) + S(x0 + 1, y0) * tx;
        float bot = S(x0, y0 + 1) * (1 - tx) + S(x0 + 1, y0 + 1) * tx;
        return top * (1 - ty) + bot * ty;
    }

    public float SpeedAt(Vector2 worldPos) => TerrainAtWorld(worldPos).Speed;

    /// <summary>World-space centers of every tile matching the given legend char.</summary>
    public List<Vector2> FindTileCenters(char c)
    {
        var result = new List<Vector2>();
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (_grid[y][x] == c)
                    result.Add(new Vector2(x * TileSize + TileSize / 2f, y * TileHeight + TileHeight / 2f));
        return result;
    }

    /// <summary>
    /// Spawn point: a road tile on the lowest road row (the main FSR),
    /// closest to the horizontal center — i.e. where the trucks park.
    /// </summary>
    public Vector2 FindSpawn()
    {
        int bestX = -1, bestY = -1;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (_grid[y][x] != 'R') continue;
                if (y > bestY || (y == bestY && Math.Abs(x - Width / 2) < Math.Abs(bestX - Width / 2)))
                {
                    bestY = y;
                    bestX = x;
                }
            }
        if (bestX < 0) return new Vector2(Width * TileSize / 2f, Height * TileHeight / 2f);
        return new Vector2(bestX * TileSize + TileSize / 2f, bestY * TileHeight + TileHeight / 2f);
    }
}
