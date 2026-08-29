using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Crewboss.Rendering;

/// <summary>
/// Screen-anchored overlays: speedometer, gear, box pips, day clock, trees
/// planted, the controls card, the pre-game block overview, and the score
/// screen. Rendered to the pinned UI target (no camera sub-pixel shift).
/// </summary>
public sealed class Hud
{
    private const int Base = 38; // slim strip along the top of the screen

    private readonly GameArt _art;

    public Hud(GameArt art) => _art = art;

    public void Draw(SpriteBatch sb, int viewW, int viewH, DayClock day, QuadController quad,
        PlayerController player, PlanterSystem planters, bool showHelp)
    {
        DrawSpeedometer(sb, quad);
        DrawBoxes(sb, quad, player);
        DrawDay(sb, viewW, day, quad, player, planters);
        if (showHelp) DrawControls(sb, viewH);
        if (day.Over) DrawScore(sb, viewW, viewH, planters);
    }

    private void DrawSpeedometer(SpriteBatch sb, QuadController quad)
    {
        float pct = quad.Speed / Tweaks.GearMax[5];
        sb.Draw(_art.Solid("speedometerBg", Color.DarkGray), new Rectangle(10, Base - 30, 150, 20), Color.White);
        sb.Draw(_art.Solid("speedometerFill", Color.Green), new Rectangle(10, Base - 30, (int)(150 * pct), 20), Color.White);
    }

    /// <summary>Carried-box indicator beside the speedometer.</summary>
    private void DrawBoxes(SpriteBatch sb, QuadController quad, PlayerController player)
    {
        int boxes = player.Mounted ? quad.Boxes : (player.CarryingBox ? 1 : 0);
        if (boxes <= 0) return;
        var face = _art.Solid("boxFace", new Color(226, 222, 210));
        var stripe = _art.Solid("boxStripe", new Color(44, 96, 58));
        for (int i = 0; i < boxes; i++)
        {
            var r = new Rectangle(240 + i * 28, Base - 28, 24, 16);
            sb.Draw(face, r, Color.White);
            sb.Draw(stripe, new Rectangle(r.X, r.Y + 6, 24, 3), Color.White);
        }
    }

    private void DrawDay(SpriteBatch sb, int viewW, DayClock day, QuadController quad,
        PlayerController player, PlanterSystem planters)
    {
        var font = _art.Font;
        if (font == null) return;

        int t = (int)MathF.Ceiling(day.Remaining);
        font.Draw(sb, $"{t / 60}:{t % 60:00}", new Vector2(viewW - 90, Base - 32), 4f, Color.White);

        // gear indicator while riding; gray with the target while the shift is in
        if (player.Mounted)
        {
            string gearText = quad.Shifting
                ? (quad.PendingGear == 0 ? "R.." : $"G{quad.PendingGear}..")
                : (quad.Gear == 0 ? "R" : $"G{quad.Gear}");
            Color gearColor = quad.Shifting ? Color.Gray
                : quad.Gear == 0 ? new Color(230, 120, 80)
                : quad.Speed > Tweaks.GearMax[quad.Gear] * 0.92f ? new Color(255, 222, 92) : Color.White;
            font.Draw(sb, gearText, new Vector2(170, Base - 30), 4f, gearColor);
        }

        if (planters != null)
            font.Draw(sb, planters.TreesPlanted.ToString(), new Vector2(viewW - 150, Base - 28), 3f, new Color(140, 220, 130));
    }

    /// <summary>Controls card, bottom-left. Font charset is 0-9 A-Z : ! - . / — keep text inside it.</summary>
    private void DrawControls(SpriteBatch sb, int viewH)
    {
        var font = _art.Font;
        if (font == null) return;
        string[] lines =
        {
            "DRIVE: WASD/ARROWS",
            "GAS: UP  BRAKE: DOWN/SPACE",
            "DRIFT: HOLD SHIFT",
            "GEARS: X/Z",
            "E: MOUNT  Q: BOXES",
            "F: CREW  C: LINE-IN",
            "T: COACH  R: RESET",
            "ZOOM: PLUS/MINUS",
            "F5: RESTART  H: HIDE",
        };
        const float fs = 1.6f;
        int lineH = (int)(7 * fs);
        int w = 0;
        foreach (var l in lines) w = Math.Max(w, (int)BitmapFont.Measure(l, fs));
        int h = lines.Length * lineH + 12;
        int x = 8, y = viewH - h - 8;
        sb.Draw(_art.Solid("helpBg", Color.Black), new Rectangle(x - 4, y - 4, w + 16, h + 8), Color.White * 0.55f);
        for (int i = 0; i < lines.Length; i++)
            font.Draw(sb, lines[i], new Vector2(x, y + i * lineH), fs, new Color(230, 230, 210));
    }

    public void DrawPreGame(SpriteBatch sb, int viewW, int viewH, Texture2D mapTexture, string blockName)
    {
        sb.Draw(_art.Solid("pregameBg", new Color(14, 20, 16)), new Rectangle(0, 0, viewW, viewH), Color.White);

        if (mapTexture != null)
        {
            // fit the whole block on screen
            float s = Math.Min((viewW - 200f) / mapTexture.Width, (viewH - 160f) / mapTexture.Height);
            int w = (int)(mapTexture.Width * s), h = (int)(mapTexture.Height * s);
            sb.Draw(mapTexture, new Rectangle((viewW - w) / 2, 90, w, h), Color.White);
        }

        var font = _art.Font;
        if (font == null) return;
        font.DrawCentered(sb, blockName.ToUpperInvariant(), viewW / 2f, 30, 6f, Color.White);
        font.DrawCentered(sb, "PRESS ANY KEY TO START THE DAY", viewW / 2f, viewH - 44, 3f, new Color(255, 222, 92));
    }

    /// <summary>Pause overlay: dim the world, list the options, highlight the selection.</summary>
    public void DrawPause(SpriteBatch sb, int viewW, int viewH, string[] items, int selected)
    {
        sb.Draw(_art.Solid("pauseDim", Color.Black), new Rectangle(0, 0, viewW, viewH), Color.White * 0.6f);
        var font = _art.Font;
        if (font == null) return;
        float cx = viewW / 2f;
        font.DrawCentered(sb, "PAUSED", cx, viewH * 0.28f, 7f, Color.White);
        for (int i = 0; i < items.Length; i++)
        {
            bool sel = i == selected;
            font.DrawCentered(sb, (sel ? "- " : "") + items[i] + (sel ? " -" : ""), cx, viewH * 0.45f + i * 44, 4f,
                sel ? new Color(255, 222, 92) : Color.White);
        }
        font.DrawCentered(sb, "UP/DOWN . ENTER . ESC RESUMES", cx, viewH * 0.45f + items.Length * 44 + 30, 2.4f, Color.Gray);
    }

    private void DrawScore(SpriteBatch sb, int viewW, int viewH, PlanterSystem planters)
    {
        sb.Draw(_art.Solid("scoreDim", Color.Black), new Rectangle(0, 0, viewW, viewH), Color.White * 0.72f);
        var font = _art.Font;
        if (font == null || planters == null) return;

        int trees = planters.TreesPlanted;
        int faults = planters.Faults;
        int idle = (int)planters.IdleSeconds;
        int stars = DayClock.Stars(trees, faults, idle);

        float cx = viewW / 2f;
        var red = new Color(214, 60, 48);
        font.DrawCentered(sb, "DAY OVER", cx, 170, 8f, Color.White);
        font.DrawCentered(sb, $"TREES PLANTED: {trees}", cx, 280, 4f, new Color(140, 220, 130));
        font.DrawCentered(sb, $"FAULTS: {faults}", cx, 330, 4f, faults > trees * 0.1f ? red : Color.White);
        font.DrawCentered(sb, $"IDLE TIME: {idle / 60}:{idle % 60:00}", cx, 380, 4f, idle > 180 ? red : Color.White);
        font.DrawCentered(sb, $"STARS: {stars}/3", cx, 450, 5f, new Color(255, 222, 92));
        font.DrawCentered(sb, "PRESS R FOR A NEW DAY", cx, 540, 3f, Color.Gray);
    }
}
