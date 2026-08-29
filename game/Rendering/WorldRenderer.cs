using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Crewboss.Rendering;

/// <summary>
/// Draws the block: map art, tire trails, seedlings, then every standing
/// sprite y-sorted far-to-near with drop shadows from one NW sun, then dust
/// and world-anchored prompts. Pure rendering — reads game state, never
/// changes it.
/// </summary>
public sealed class WorldRenderer
{
    private struct WorldEntity
    {
        public float SortY;
        public Texture2D Tex;
        public Rectangle Src;
        public float X, BaseY, Scale;
        public SpriteEffects Fx;
        public bool Shadow;
        public float LiftExtra; // height above the ground (suspension/airtime)
        public float Rot;       // sprite lean (radians) — terrain tilt
    }

    private readonly GameArt _art;
    private readonly WorldMap _map;
    private readonly Camera _cam;
    private readonly QuadController _quad;
    private readonly PlayerController _player;
    private readonly QuadEffects _effects;
    private readonly List<CacheEntity> _caches;
    private readonly List<WorldEntity> _entities = new();

    public PlanterSystem Planters { get; set; }

    public WorldRenderer(GameArt art, WorldMap map, Camera cam, QuadController quad,
        PlayerController player, QuadEffects effects, List<CacheEntity> caches)
    {
        _art = art; _map = map; _cam = cam; _quad = quad;
        _player = player; _effects = effects; _caches = caches;
    }

    private Vector2 W2S(Vector2 w) => _cam.WorldToScreen(w, _map.Lift(w));

    public void Draw(SpriteBatch sb)
    {
        // Terrain relief: the PNG carries MaxElev rows of top padding (baked
        // hill overhang), so world row y sits at PNG row y + MaxElev.
        var dest = new Rectangle(0, 0, _cam.ViewWidth, _cam.ViewHeight);
        var src = new Rectangle((int)_cam.Position.X, (int)_cam.Position.Y + _map.MaxElev,
            (int)(_cam.ViewWidth / _cam.Zoom), (int)(_cam.ViewHeight / _cam.Zoom));
        sb.Draw(_map.Texture, dest, src, Color.White);

        _effects.DrawTrails(sb, _cam, _map);
        DrawSeedlings(sb);
        DrawWorldSprites(sb);
        _effects.DrawDust(sb, _cam, _map);
        DrawPrompts(sb);
        if (_player.Aiming) DrawAimArrow(sb);
    }

    // ---------- sprites ----------

    private void DrawWorldSprites(SpriteBatch sb)
    {
        float zoom = _cam.Zoom;
        float viewWorldH = _cam.ViewHeight / zoom;
        float maxTreeWorld = _map.Trees?.MaxSpriteHeight ?? 80;
        float liftPad = _map.MaxElev; // high-ground objects shift up by as much as this

        var entities = _entities;
        entities.Clear();

        // the quad: with rider when mounted; parked (leaning with its ground) when not
        if (_art.HasQuadAtlas)
        {
            bool rider = _player.Mounted;
            int boxes = Math.Clamp(_quad.Boxes, 0, QuadController.BoxCap);
            var qsrc = _art.QuadFrames[(boxes * 32 + Math.Clamp(_quad.DirIdx, 0, 31)) * 2 + (rider ? 1 : 0)];
            entities.Add(new WorldEntity
            {
                SortY = _quad.Pos.Y + 12,
                Tex = _art.QuadAtlas,
                Src = qsrc,
                X = _quad.Pos.X,
                BaseY = _quad.Pos.Y + 12,
                Scale = zoom,
                LiftExtra = rider ? _quad.AirHeight(_map) : 0f,
                Rot = rider ? _quad.Tilt : _map.TiltAt(_quad.Pos),
                Shadow = true
            });
        }

        if (_art.Cache != null)
            foreach (var c in _caches)
                entities.Add(new WorldEntity
                {
                    SortY = c.Pos.Y, Tex = _art.Cache,
                    Src = new Rectangle(0, 0, _art.Cache.Width, _art.Cache.Height),
                    X = c.Pos.X, BaseY = c.Pos.Y, Scale = zoom, Shadow = true
                });

        // vegetation — pure set dressing
        if (_map.Veg != null)
            foreach (var (vx, vy, vv) in _map.Veg.InRange(
                _cam.Position.Y - 30 - liftPad, _cam.Position.Y + viewWorldH + 30 + liftPad))
                entities.Add(new WorldEntity
                {
                    SortY = vy, Tex = _map.Veg.Atlas, Src = _map.Veg.Sprites[vv % _map.Veg.Sprites.Length],
                    X = vx, BaseY = vy, Scale = zoom,
                    Shadow = vv >= 3 // bushes ground themselves; grass stays light
                });

        // debris obstacles (logs, stumps)
        if (_map.Debris != null)
            foreach (var (dx, dy, dv) in _map.Debris.InRange(
                _cam.Position.Y - maxTreeWorld - liftPad, _cam.Position.Y + viewWorldH + 60 + liftPad))
                entities.Add(new WorldEntity
                {
                    SortY = dy, Tex = _map.Debris.Atlas, Src = _map.Debris.Sprites[dv % _map.Debris.Sprites.Length],
                    X = dx, BaseY = dy, Scale = zoom, Shadow = true
                });

        // planter crew
        if (Planters != null && _art.PlanterAtlas != null && _art.PlanterFrames_ != null)
            foreach (var p in Planters.Planters)
            {
                Rectangle src;
                SpriteEffects fx = SpriteEffects.None;
                if (p.State == PlanterState.Planting)
                    src = _art.PlanterFrames_[p.Variant * GameArt.PlanterFrames + GameArt.PlanterFrames - 1];
                else
                {
                    int dirIdx = p.Dir switch { "S" => 0, "N" => 1, _ => 2 };
                    int frame = p.Walking ? (int)(p.WalkAnim * 8) % GameArt.WalkFrames : 1;
                    src = _art.PlanterFrames_[p.Variant * GameArt.PlanterFrames + dirIdx * GameArt.WalkFrames + frame];
                    if (p.Dir == "W") fx = SpriteEffects.FlipHorizontally;
                }
                entities.Add(new WorldEntity
                {
                    SortY = p.Pos.Y + 11, Tex = _art.PlanterAtlas, Src = src,
                    X = p.Pos.X, BaseY = p.Pos.Y + 11, Scale = zoom, Fx = fx, Shadow = true
                });
            }

        // the foreman on foot
        if (!_player.Mounted && _art.ForemanAtlas != null && _art.ForemanFrames != null)
        {
            int dirIdx = _player.FootDir switch { "S" => 0, "N" => 1, _ => 2 };
            int frame = _player.Walking ? (int)(_player.WalkAnim * 8) % GameArt.WalkFrames : 1;
            entities.Add(new WorldEntity
            {
                SortY = _player.FootPos.Y + 11, Tex = _art.ForemanAtlas,
                Src = _art.ForemanFrames[dirIdx * GameArt.WalkFrames + frame],
                X = _player.FootPos.X, BaseY = _player.FootPos.Y + 11, Scale = zoom,
                Fx = _player.FootDir == "W" ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
                Shadow = true
            });
        }

        entities.Sort((a, b) => a.SortY.CompareTo(b.SortY));

        // merge entities with the y-sorted tree stream
        int e = 0;
        void DrawEntitiesUpTo(float y)
        {
            while (e < entities.Count && entities[e].SortY <= y)
            {
                var en = entities[e++];
                DrawWorldSprite(sb, en.Tex, en.Src, en.X, en.BaseY, en.Scale, en.Fx, en.Shadow, en.LiftExtra, en.Rot);
            }
        }

        if (_map.Trees != null)
            foreach (var (tx, ty, tv) in _map.Trees.InRange(
                _cam.Position.Y - maxTreeWorld * Tweaks.TreeScale - liftPad,
                _cam.Position.Y + viewWorldH + maxTreeWorld * Tweaks.TreeScale + liftPad))
            {
                DrawEntitiesUpTo(ty);
                var src = _map.Trees.Sprites[tv % _map.Trees.Sprites.Length];
                DrawWorldSprite(sb, _map.Trees.Atlas, src, tx, ty, zoom * Tweaks.TreeScale, SpriteEffects.None, shadow: true);
            }
        DrawEntitiesUpTo(float.MaxValue);
    }

    /// <summary>
    /// Draw a world sprite anchored bottom-center at (wx, wy). Elevation lifts
    /// the draw; suspension/airtime lifts further and detaches the shadow.
    /// Shadow = the sprite's own outline offset SE (sun high in the NW), drawn
    /// under; offset grows with sprite height and with airtime.
    /// </summary>
    private void DrawWorldSprite(SpriteBatch sb, Texture2D tex, Rectangle src, float wx, float wy, float scale,
        SpriteEffects fx = SpriteEffects.None, bool shadow = false, float liftExtra = 0f, float rot = 0f)
    {
        float zoom = _cam.Zoom;
        float destX = (wx - _cam.Position.X) * zoom;
        if (destX < -150 || destX > _cam.ViewWidth + 150) return;

        float groundY = (wy - _map.Lift(wx, wy) - _cam.Position.Y) * zoom;
        float destY = groundY - liftExtra * zoom;
        if (destY - src.Height * scale > _cam.ViewHeight || destY < -20) return;

        bool airborne = liftExtra > 2.5f;
        if (shadow || airborne)
        {
            float len = MathF.Min(14f, src.Height * scale * 0.22f) + liftExtra * zoom * 0.6f;
            var shPos = new Vector2(destX + len * 0.9f, groundY + len * 0.55f);
            float a = airborne ? 0.15f : 0.24f;
            sb.Draw(tex, shPos, src, Color.Black * a, rot, new Vector2(src.Width / 2f, src.Height), scale, fx, 0f);
        }

        // rotation pivots at the feet: wheels stay planted, body leans with the slope
        sb.Draw(tex, new Vector2(destX, destY), src, Color.White, rot, new Vector2(src.Width / 2f, src.Height), scale, fx, 0f);
    }

    // ---------- ground layer ----------

    /// <summary>Seedlings in the visible tile range, flat on the ground, with quality reveals.</summary>
    private void DrawSeedlings(SpriteBatch sb)
    {
        if (Planters == null || _art.SeedlingAtlas == null || _art.SeedlingFrames == null || _map.Tiles == null) return;

        var tiles = _map.Tiles;
        int ts = tiles.TileSize, th = tiles.TileHeight;
        float zoom = _cam.Zoom;
        int tx0 = Math.Max(0, (int)(_cam.Position.X / ts) - 1);
        int ty0 = Math.Max(0, (int)(_cam.Position.Y / th) - 1);
        int tx1 = Math.Min(tiles.Width - 1, (int)((_cam.Position.X + _cam.ViewWidth / zoom) / ts) + 1);
        int ty1 = Math.Min(tiles.Height - 1, (int)((_cam.Position.Y + _cam.ViewHeight / zoom + tiles.MaxElev) / th) + 1);

        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
            {
                int count = Planters.PlantedAtTile(tx, ty);
                for (int s = 0; s < count; s++)
                {
                    Vector2 pos = PlanterSystem.SpotPos(tx, ty, s, ts, th);
                    var src = _art.SeedlingFrames[(tx * 31 + ty * 17 + s) % _art.SeedlingFrames.Length];
                    Vector2 screen = W2S(pos);
                    sb.Draw(_art.SeedlingAtlas, screen, src, Color.White, 0f,
                        new Vector2(src.Width / 2f, src.Height), zoom * 0.7f, SpriteEffects.None, 0f);

                    // quality reveal: green = good tree, red = fault
                    if (_player.Reveals.TryGetValue((tx, ty, s), out float ttl))
                    {
                        bool bad = Planters.IsFault(tx, ty, s);
                        var mark = _art.Solid(bad ? "revealBad" : "revealGood",
                            bad ? new Color(214, 60, 48) : new Color(84, 200, 90));
                        float alpha = Math.Min(1f, ttl / 0.6f);
                        sb.Draw(mark, new Rectangle((int)screen.X - 3, (int)(screen.Y - src.Height * zoom * 0.7f) - 10, 7, 7),
                            Color.White * alpha);
                    }
                }
            }
    }

    // ---------- prompts ----------

    private void DrawBadge(SpriteBatch sb, Texture2D badge, Vector2 worldPos)
    {
        Vector2 s = W2S(worldPos);
        sb.Draw(badge, new Rectangle((int)s.X - 14, (int)s.Y - 14, 28, 28), Color.White);
    }

    /// <summary>Prompt badges over interaction targets, plus box pips over caches.</summary>
    private void DrawPrompts(SpriteBatch sb)
    {
        Vector2 pos = _player.Pos;

        if (_art.BadgeE != null && !_player.Mounted && Vector2.Distance(pos, _quad.Pos) < PlayerController.InteractRange)
            DrawBadge(sb, _art.BadgeE, _quad.Pos + new Vector2(0, -34));

        // planter state badges: idle "!" (fix it) and done "✓" (come move them)
        if (Planters != null)
            foreach (var p in Planters.Planters)
            {
                if (p.State == PlanterState.Idle && _art.BadgeAlert != null)
                    DrawBadge(sb, _art.BadgeAlert, p.Pos + new Vector2(0, -30));
                else if (p.State == PlanterState.Done && _art.BadgeDone != null)
                    DrawBadge(sb, _art.BadgeDone, p.Pos + new Vector2(0, -30));
            }

        // T badge: on foot next to a working planter = coach them
        if (!_player.Mounted && _art.BadgeT != null && Planters != null)
        {
            var coachee = Planters.FindCoachTarget(pos);
            if (coachee != null && coachee.CoachTimer <= 0)
                DrawBadge(sb, _art.BadgeT, coachee.Pos + new Vector2(22, -30));
        }

        var (action, target) = _player.GetBoxAction();
        if (action != BoxAction.None && _art.BadgeQ != null)
            DrawBadge(sb, _art.BadgeQ, target + new Vector2(0, action == BoxAction.PlaceCache ? -10 : -48));

        // C badge over a cache when a line-in is possible from it
        if (!_player.Aiming && _art.BadgeC != null && Planters != null)
            foreach (var c in _caches)
                if (Vector2.Distance(pos, c.Pos) < 70f)
                {
                    if (Planters.FindLinePlanter(c.Pos) != null)
                        DrawBadge(sb, _art.BadgeC, c.Pos + new Vector2(22, -48));
                    break;
                }

        // cache fill pips
        if (_art.Cache == null) return;
        var pip = _art.Solid("cachePip", new Color(226, 222, 210));
        var pipStripe = _art.Solid("cachePipStripe", new Color(44, 96, 58));
        foreach (var c in _caches)
        {
            Vector2 screen = W2S(c.Pos + new Vector2(0, -_art.Cache.Height + 6));
            int n = Math.Min(c.Boxes, 8);
            for (int i = 0; i < n; i++)
            {
                var r = new Rectangle((int)(screen.X - n * 7 + i * 14), (int)screen.Y, 12, 8);
                sb.Draw(pip, r, Color.White);
                sb.Draw(pipStripe, new Rectangle(r.X, r.Y + 3, 12, 2), Color.White);
            }
        }
    }

    /// <summary>The line-in aim arrow, drawn from the cache along the chosen bearing.</summary>
    private void DrawAimArrow(SpriteBatch sb)
    {
        var tex = _art.Solid("aimArrow", new Color(255, 222, 92));
        Vector2 start = W2S(_player.AimCache.Pos);
        float len = 130f * _cam.Zoom;
        float ang = _player.AimAngle;
        var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));

        sb.Draw(tex, start, null, Color.White * 0.9f, ang, new Vector2(0, 0.5f), new Vector2(len, 3f), SpriteEffects.None, 0f);

        // arrowhead: two strokes angled back from the tip
        Vector2 tip = start + dir * len;
        foreach (float da in stackalloc[] { 2.6f, -2.6f })
            sb.Draw(tex, tip, null, Color.White * 0.9f, ang + da, new Vector2(0, 0.5f), new Vector2(26f, 3f), SpriteEffects.None, 0f);
    }
}
