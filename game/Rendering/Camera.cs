using Microsoft.Xna.Framework;
using System;

namespace Crewboss.Rendering;

/// <summary>
/// Smooth-following, integer-snapped camera with a sub-pixel remainder.
/// World renders on an integer pixel grid (crisp, no shimmer); the dropped
/// fraction (Frac) offsets the final present so scrolling glides instead of
/// stepping whole pixels. Look-ahead leads along travel so the scene flows
/// steadily rather than lurching with every steer.
/// </summary>
public sealed class Camera
{
    /// <summary>Integer world position of the view's top-left.</summary>
    public Vector2 Position;
    /// <summary>Sub-pixel remainder in [0,1) — applied at present time.</summary>
    public Vector2 Frac;
    public float Zoom = 1f;
    public int ViewWidth, ViewHeight;

    private Vector2 _smooth;
    private bool _init;

    public void Reset() => _init = false;

    public void Update(Vector2 focus, Vector2 lead, Rectangle bounds, float dt)
    {
        var target = new Vector2(
            focus.X + lead.X - ViewWidth / (2f * Zoom),
            focus.Y + lead.Y - ViewHeight / (2f * Zoom));

        // exponential follow, no deadzone (a deadzone made camera velocity
        // discontinuous — hold, then catch up — which read as jitter)
        if (!_init) { _smooth = target; _init = true; }
        float k = 1f - MathF.Exp(-12f * MathF.Max(dt, 1e-4f));
        _smooth += (target - _smooth) * k;

        float maxX = bounds.Width - ViewWidth / Zoom;
        float maxY = bounds.Height - ViewHeight / Zoom;
        _smooth.X = MathHelper.Clamp(_smooth.X, 0, MathF.Max(0, maxX));
        _smooth.Y = MathHelper.Clamp(_smooth.Y, 0, MathF.Max(0, maxY));

        Position.X = MathF.Floor(_smooth.X);
        Position.Y = MathF.Floor(_smooth.Y);
        Frac = _smooth - Position;
    }

    /// <summary>World to view pixels; lift is the ground elevation at that point.</summary>
    public Vector2 WorldToScreen(Vector2 world, float lift) => new(
        (world.X - Position.X) * Zoom,
        (world.Y - lift - Position.Y) * Zoom);
}
