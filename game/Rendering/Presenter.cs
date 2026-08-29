using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Crewboss.Rendering;

/// <summary>
/// The pixel-art presentation rig. The scene renders 1:1 into a logical-size
/// target (window / zoom, +1 overscan), then ONE linear upscale presents it —
/// a single filter instead of thousands of per-sprite resamplings. The HUD
/// renders to its own target and presents unshifted so text never swims with
/// the camera's sub-pixel glide.
/// </summary>
public sealed class Presenter
{
    public int OutWidth { get; }
    public int OutHeight { get; }
    public float Zoom { get; private set; }
    /// <summary>Logical view size — everything renders in these pixels.</summary>
    public int ViewWidth { get; private set; }
    public int ViewHeight { get; private set; }

    private RenderTarget2D _scene, _ui;

    public Presenter(int outWidth, int outHeight, float zoom)
    {
        OutWidth = outWidth;
        OutHeight = outHeight;
        ApplyZoom(zoom);
    }

    /// <summary>Change presentation scale; world math stays 1:1 — zoom only changes how much world fits.</summary>
    public void ApplyZoom(float z)
    {
        Zoom = MathHelper.Clamp(z, 0.8f, 2.5f);
        // +1 overscan: the present pass shifts the frame by up to one logical pixel
        ViewWidth = (int)MathF.Ceiling(OutWidth / Zoom) + 1;
        ViewHeight = (int)MathF.Ceiling(OutHeight / Zoom) + 1;
        _scene?.Dispose(); _scene = null;
        _ui?.Dispose(); _ui = null;
    }

    public RenderTarget2D Scene(GraphicsDevice gd) => _scene ??= new RenderTarget2D(gd, ViewWidth, ViewHeight);
    public RenderTarget2D Ui(GraphicsDevice gd) => _ui ??= new RenderTarget2D(gd, ViewWidth, ViewHeight);

    /// <summary>Upscale scene (shifted by the camera's sub-pixel remainder) then the pinned UI.</summary>
    public void Present(SpriteBatch sb, Vector2 sceneFrac, bool drawUi)
    {
        if (_scene != null)
            sb.Draw(_scene, -sceneFrac * Zoom, null, Color.White, 0f, Vector2.Zero, Zoom, SpriteEffects.None, 0f);
        if (_ui != null && drawUi)
            sb.Draw(_ui, Vector2.Zero, null, Color.White, 0f, Vector2.Zero, Zoom, SpriteEffects.None, 0f);
    }
}
