using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.IO;

namespace Crewboss.Core;

/// <summary>Tiny 3x5 bitmap font (FontAtlas.png from ArtTool). Uppercases everything.</summary>
public class BitmapFont
{
    private const string Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ:!-./";
    private readonly Texture2D _atlas;

    private BitmapFont(Texture2D atlas) => _atlas = atlas;

    public static BitmapFont Load(GraphicsDevice gd, string path)
    {
        if (!File.Exists(path)) return null;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        return new BitmapFont(Texture2D.FromStream(gd, fs));
    }

    /// <summary>Width of a string in pixels at the given scale.</summary>
    public static float Measure(string text, float scale) => text.Length * 4 * scale;

    public void Draw(SpriteBatch sb, string text, Vector2 pos, float scale, Color color)
    {
        text = text.ToUpperInvariant();
        float x = pos.X;
        foreach (char c in text)
        {
            int idx = Chars.IndexOf(c);
            if (idx >= 0)
                sb.Draw(_atlas, new Vector2(x, pos.Y), new Rectangle(idx * 4, 0, 3, 5),
                    color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            x += 4 * scale; // space and unknown chars just advance
        }
    }

    public void DrawCentered(SpriteBatch sb, string text, float centerX, float y, float scale, Color color)
        => Draw(sb, text, new Vector2(centerX - Measure(text, scale) / 2f, y), scale, color);
}
