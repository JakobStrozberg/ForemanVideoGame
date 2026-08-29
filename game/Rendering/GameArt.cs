using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.IO;

namespace Crewboss.Rendering;

/// <summary>
/// Every runtime-loaded sprite the gameplay screen draws: generated atlases
/// (foreman, planters, seedlings, quad), prompt badges, the cache tent, the
/// bitmap font, plus a cache of 1x1 solid textures for UI rectangles.
/// Loaded from Content/GameTextures/Generated (output of dev/ArtTool).
/// </summary>
public sealed class GameArt
{
    public const int WalkFrames = 4;      // contact-pass-contact-pass
    public const int PlanterFrames = 13;  // 3 dirs x 4 walk frames + planting pose

    public Texture2D ForemanAtlas, PlanterAtlas, SeedlingAtlas, QuadAtlas, Cache;
    public Rectangle[] ForemanFrames, PlanterFrames_, SeedlingFrames, QuadFrames;
    public Texture2D BadgeE, BadgeQ, BadgeC, BadgeT, BadgeAlert, BadgeDone;
    public BitmapFont Font;

    /// <summary>True when the quad atlas is the v2 layout: (boxes * 32 + dir) * 2 + rider.</summary>
    public bool HasQuadAtlas => QuadAtlas != null && QuadFrames != null && QuadFrames.Length >= 32 * 2 * 9;

    private readonly GraphicsDevice _gd;
    private readonly Dictionary<string, Texture2D> _solids = new();

    private GameArt(GraphicsDevice gd) => _gd = gd;

    public static GameArt Load(GraphicsDevice gd)
    {
        string dir = Path.Combine(Tweaks.ContentRoot(), "GameTextures", "Generated");
        var art = new GameArt(gd)
        {
            ForemanAtlas = TryLoadTexture(gd, Path.Combine(dir, "ForemanAtlas.png")),
            ForemanFrames = LoadAtlasRects(Path.Combine(dir, "ForemanAtlas.json")),
            PlanterAtlas = TryLoadTexture(gd, Path.Combine(dir, "PlanterAtlas.png")),
            PlanterFrames_ = LoadAtlasRects(Path.Combine(dir, "PlanterAtlas.json")),
            SeedlingAtlas = TryLoadTexture(gd, Path.Combine(dir, "SeedlingAtlas.png")),
            SeedlingFrames = LoadAtlasRects(Path.Combine(dir, "SeedlingAtlas.json")),
            QuadAtlas = TryLoadTexture(gd, Path.Combine(dir, "QuadAtlas.png")),
            QuadFrames = LoadAtlasRects(Path.Combine(dir, "QuadAtlas.json")),
            Cache = TryLoadTexture(gd, Path.Combine(dir, "Cache.png")),
            BadgeE = TryLoadTexture(gd, Path.Combine(dir, "BadgeE.png")),
            BadgeQ = TryLoadTexture(gd, Path.Combine(dir, "BadgeQ.png")),
            BadgeC = TryLoadTexture(gd, Path.Combine(dir, "BadgeC.png")),
            BadgeT = TryLoadTexture(gd, Path.Combine(dir, "BadgeT.png")),
            BadgeAlert = TryLoadTexture(gd, Path.Combine(dir, "BadgeAlert.png")),
            BadgeDone = TryLoadTexture(gd, Path.Combine(dir, "BadgeDone.png")),
            Font = BitmapFont.Load(gd, Path.Combine(dir, "FontAtlas.png")),
        };
        return art;
    }

    /// <summary>Solid 1x1 texture for UI rectangles, created once per name.</summary>
    public Texture2D Solid(string name, Color color)
    {
        if (_solids.TryGetValue(name, out var cached)) return cached;
        var tex = new Texture2D(_gd, 1, 1);
        tex.SetData(new[] { color });
        _solids[name] = tex;
        return tex;
    }

    public static Texture2D TryLoadTexture(GraphicsDevice gd, string path)
    {
        if (!File.Exists(path)) return null;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        return Texture2D.FromStream(gd, fs);
    }

    public static Rectangle[] LoadAtlasRects(string jsonPath)
    {
        if (!File.Exists(jsonPath)) return null;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
        var list = new List<Rectangle>();
        foreach (var s in doc.RootElement.GetProperty("sprites").EnumerateArray())
            list.Add(new Rectangle(
                s.GetProperty("x").GetInt32(), s.GetProperty("y").GetInt32(),
                s.GetProperty("w").GetInt32(), s.GetProperty("h").GetInt32()));
        return list.ToArray();
    }
}
