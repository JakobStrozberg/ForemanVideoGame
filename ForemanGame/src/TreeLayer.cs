using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace src;

/// <summary>
/// Runtime tree layer: a sprite atlas (TreeAtlas.png/json) plus per-block tree
/// positions (Block*.trees.json, y-sorted). The game draws trees itself so they
/// can crest the horizon tips-first and y-sort against the player.
/// </summary>
public class TreeLayer
{
    public Texture2D Atlas { get; private set; }
    public Rectangle[] Sprites { get; private set; }
    public int MaxSpriteHeight { get; private set; }

    // sorted by Y ascending
    private (int x, int y, int v)[] _trees = Array.Empty<(int, int, int)>();

    public static TreeLayer Load(GraphicsDevice gd, string atlasPng, string atlasJson, string treesJson)
    {
        if (!File.Exists(atlasPng) || !File.Exists(atlasJson) || !File.Exists(treesJson)) return null;

        var layer = new TreeLayer();
        using (var fs = new FileStream(atlasPng, FileMode.Open, FileAccess.Read))
        {
            layer.Atlas = Texture2D.FromStream(gd, fs);
        }

        using (var doc = JsonDocument.Parse(File.ReadAllText(atlasJson)))
        {
            var list = new List<Rectangle>();
            foreach (var s in doc.RootElement.GetProperty("sprites").EnumerateArray())
                list.Add(new Rectangle(
                    s.GetProperty("x").GetInt32(), s.GetProperty("y").GetInt32(),
                    s.GetProperty("w").GetInt32(), s.GetProperty("h").GetInt32()));
            layer.Sprites = list.ToArray();
            foreach (var r in layer.Sprites)
                layer.MaxSpriteHeight = Math.Max(layer.MaxSpriteHeight, r.Height);
        }

        using (var doc = JsonDocument.Parse(File.ReadAllText(treesJson)))
        {
            var list = new List<(int, int, int)>();
            foreach (var t in doc.RootElement.GetProperty("trees").EnumerateArray())
            {
                int i = 0, x = 0, y = 0, v = 0;
                foreach (var n in t.EnumerateArray())
                {
                    if (i == 0) x = n.GetInt32();
                    else if (i == 1) y = n.GetInt32();
                    else v = n.GetInt32();
                    i++;
                }
                list.Add((x, y, v));
            }
            layer._trees = list.ToArray();
        }
        return layer;
    }

    /// <summary>Trees with base Y in [y0, y1), ascending. Binary search on the sorted list.</summary>
    public IEnumerable<(int x, int y, int v)> InRange(float y0, float y1)
    {
        int lo = 0, hi = _trees.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_trees[mid].y < y0) lo = mid + 1; else hi = mid;
        }
        for (int i = lo; i < _trees.Length && _trees[i].y < y1; i++)
            yield return _trees[i];
    }
}
