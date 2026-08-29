using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace Crewboss.Maps;

/// <summary>
/// Everything one block is made of at runtime: the baked map art, the tile
/// terrain grid, the tree/debris/vegetation layers, and the terrain queries
/// every mechanic asks (elevation, micro-bumps, roughness, debris hits).
/// Loaded from Content/Maps/&lt;Block&gt;/ — the output of dev/ArtTool compose.
/// </summary>
public sealed class WorldMap
{
    public string BlockName { get; private set; }
    public Texture2D Texture { get; private set; }
    public TileMap Tiles { get; private set; }
    /// <summary>Logical world size (the PNG carries extra relief padding rows on top).</summary>
    public Rectangle Bounds { get; private set; }

    public TreeLayer Trees { get; private set; }
    public TreeLayer Debris { get; private set; }
    public TreeLayer Veg { get; private set; }

    /// <summary>Quad-only collision: logs and stumps as circles (people climb over).</summary>
    public readonly List<(Vector2 c, float r)> DebrisCircles = new();
    /// <summary>Truck interaction points ('O' tiles).</summary>
    public List<Vector2> TruckCenters { get; private set; } = new();

    public int MaxElev => Tiles?.MaxElev ?? 0;

    public static WorldMap Load(GraphicsDevice gd, string blockName)
    {
        string content = Tweaks.ContentRoot();
        string mapDir = Path.Combine(content, "Maps", blockName);
        string genDir = Path.Combine(content, "GameTextures", "Generated");
        string png = Path.Combine(mapDir, $"{blockName}.png");
        if (!File.Exists(png))
            throw new FileNotFoundException($"Map '{blockName}' not found at {png} — run dev/regen.sh");

        var map = new WorldMap { BlockName = blockName };
        using (var fs = new FileStream(png, FileMode.Open, FileAccess.Read))
            map.Texture = Texture2D.FromStream(gd, fs);
        Console.WriteLine($"Loaded generated block {blockName}");

        string tilesJson = Path.Combine(mapDir, $"{blockName}.tiles.json");
        if (File.Exists(tilesJson))
        {
            map.Tiles = TileMap.Load(tilesJson);
            Console.WriteLine($"Loaded tile data {map.Tiles.Width}x{map.Tiles.Height} @ {map.Tiles.TileSize}px");
        }

        map.Bounds = map.Tiles != null
            ? new Rectangle(0, 0, map.Tiles.Width * map.Tiles.TileSize, map.Tiles.Height * map.Tiles.TileHeight)
            : new Rectangle(0, 0, map.Texture.Width, map.Texture.Height);

        map.Trees = TreeLayer.Load(gd,
            Path.Combine(genDir, "TreeAtlas.png"), Path.Combine(genDir, "TreeAtlas.json"),
            Path.Combine(mapDir, $"{blockName}.trees.json"));
        Console.WriteLine(map.Trees != null ? "Loaded tree layer" : "No tree layer found");

        map.Debris = TreeLayer.Load(gd,
            Path.Combine(genDir, "DebrisAtlas.png"), Path.Combine(genDir, "DebrisAtlas.json"),
            Path.Combine(mapDir, $"{blockName}.debris.json"));
        if (map.Debris != null)
        {
            foreach (var (dx, dy, dv) in map.Debris.InRange(float.MinValue, float.MaxValue))
            {
                var p = new Vector2(dx, dy);
                if (dv < 6)
                {
                    // log: three circles along its angle (y squashed like the sprite)
                    float ang = dv * 30f * MathF.PI / 180f;
                    var off = new Vector2(MathF.Cos(ang), MathF.Sin(ang) * 0.7f) * 13f;
                    map.DebrisCircles.Add((p - off, 7f));
                    map.DebrisCircles.Add((p, 7f));
                    map.DebrisCircles.Add((p + off, 7f));
                }
                else
                {
                    map.DebrisCircles.Add((p, 6f));
                }
            }
            Console.WriteLine($"Loaded debris: {map.DebrisCircles.Count} collision circles");
        }

        map.Veg = TreeLayer.Load(gd,
            Path.Combine(genDir, "VegAtlas.png"), Path.Combine(genDir, "VegAtlas.json"),
            Path.Combine(mapDir, $"{blockName}.veg.json"));

        map.TruckCenters = map.Tiles?.FindTileCenters('O') ?? new List<Vector2>();
        return map;
    }

    public Vector2 Spawn() => Tiles?.FindSpawn() ?? new Vector2(Bounds.Width / 2f, Bounds.Height / 2f);

    // ---------- terrain queries ----------

    /// <summary>Ground elevation lift at a world position (0 when no relief data).</summary>
    public float Lift(float wx, float wy) => Tiles?.ElevationAt(new Vector2(wx, wy)) ?? 0f;
    public float Lift(Vector2 w) => Lift(w.X, w.Y);

    /// <summary>
    /// Position-keyed micro-bump height (world px, unscaled by roughness).
    /// Keyed to WORLD POSITION, not time: fixed ground features — the same
    /// spot always bumps the same way, and a parked quad never moves.
    /// </summary>
    public static float BumpAt(float x, float y) =>
        (MathF.Sin(x * 0.10f + y * 0.06f) * 0.6f
       + MathF.Sin(x * 0.031f + y * 0.113f) * 0.4f) * 2.4f;

    /// <summary>Terrain roughness multiplier for micro-bumps at a position.</summary>
    public float RoughAt(Vector2 pos) =>
        Tiles == null ? 0.6f : Tiles.TerrainAtWorld(pos).Name switch
        {
            "rock" => 1.4f,
            "slash" => 1.0f,
            "swamp" => 0.8f,
            "cream" => 0.5f,
            "trail" => 0.3f,
            "road" => 0.15f,
            _ => 0.6f
        };

    /// <summary>Relief + micro-bump ground height, the surface the quad actually rides.</summary>
    public float SurfaceAt(float x, float y, float rough) => Lift(x, y) + BumpAt(x, y) * rough;

    /// <summary>
    /// Sprite lean for something sitting at pos: ground slope in screen-x
    /// under it (relief + bumps), uphill side up, clamped to ±20°.
    /// </summary>
    public float TiltAt(Vector2 pos)
    {
        float rough = RoughAt(pos);
        float hR = SurfaceAt(pos.X + 11f, pos.Y, rough);
        float hL = SurfaceAt(pos.X - 11f, pos.Y, rough);
        return MathHelper.Clamp(-MathF.Atan2(hR - hL, 22f) * 1.1f, -0.35f, 0.35f);
    }

    public string TerrainName(Vector2 pos) => Tiles?.TerrainAtWorld(pos).Name ?? "";
    public float SpeedAt(Vector2 pos) => Tiles?.SpeedAt(pos) ?? 1f;
    public bool IsPassable(Vector2 pos) => Tiles?.IsPassable(pos) ?? true;

    /// <summary>Quad-only: logs and stumps block the machine (people climb over).</summary>
    public bool HitsDebris(Vector2 pos)
    {
        const float quadR = 10f;
        foreach (var (c, r) in DebrisCircles)
        {
            float rr = r + quadR;
            float dx = pos.X - c.X, dy = pos.Y - c.Y;
            if (dx * dx + dy * dy < rr * rr) return true;
        }
        return false;
    }
}
