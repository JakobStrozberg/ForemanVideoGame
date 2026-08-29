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
        public float ShadowLen; // fixed shadow offset; 0 = derive from sprite height
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
        DrawFlagLines(sb);
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
                Shadow = true,
                ShadowLen = 3f // low vehicle seen from above: hugs the ground
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
            int frame = _player.Walking ? _player.WalkFrame : 1;
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
                DrawWorldSprite(sb, en.Tex, en.Src, en.X, en.BaseY, en.Scale, en.Fx, en.Shadow, en.LiftExtra, en.Rot, en.ShadowLen);
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
        SpriteEffects fx = SpriteEffects.None, bool shadow = false, float liftExtra = 0f, float rot = 0f,
        float shadowLen = 0f)
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
            float len = (shadowLen > 0f ? shadowLen : MathF.Min(14f, src.Height * scale * 0.22f)) + liftExtra * zoom * 0.6f;
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

    // ---------- flag lines ----------

    private Vector2 TileCenter(Point t) =>
        new(t.X * _map.Tiles.TileSize + _map.Tiles.TileSize / 2f, t.Y * _map.Tiles.TileHeight + _map.Tiles.TileHeight / 2f);

    /// <summary>Flagging tape: a stake with a tag at every flag, ties along each run, and the open line back to the crewboss.</summary>
    private void DrawFlagLines(SpriteBatch sb)
    {
        if (Planters == null || _map.Tiles == null) return;
        var stake = _art.Solid("flagStake", new Color(70, 50, 30));
        var tape = _art.Solid("flagTape", new Color(255, 110, 40));
        var px = _art.Solid("dbgPx", Color.White);

        foreach (var (a, b) in Planters.FlagSegments)
        {
            Vector2 sa = W2S(TileCenter(a)), sb2 = W2S(TileCenter(b));
            // ties every ~40px along the run
            float len = Vector2.Distance(sa, sb2);
            int ties = Math.Max(1, (int)(len / 40f));
            for (int i = 1; i < ties; i++)
            {
                Vector2 q = Vector2.Lerp(sa, sb2, i / (float)ties);
                sb.Draw(tape, new Rectangle((int)q.X - 2, (int)q.Y - 5, 4, 3), Color.White);
            }
            // the tape itself: a thin faint line so pieces read at a glance
            Line(sb, px, sa, sb2, new Color(255, 140, 60) * 0.35f);
        }
        foreach (var f in Planters.Flags)
        {
            Vector2 q = W2S(TileCenter(f));
            sb.Draw(stake, new Rectangle((int)q.X - 1, (int)q.Y - 12, 2, 12), Color.White);
            sb.Draw(tape, new Rectangle((int)q.X - 1, (int)q.Y - 12, 6, 4), Color.White);
        }
        // where the next flag would run the tape from
        if (Planters.OpenLineEnd is Point open)
            Line(sb, px, W2S(TileCenter(open)), W2S(_player.Pos), new Color(255, 140, 60) * 0.5f);
    }

    // ---------- dev view ----------

    private static readonly Color[] CrewColors =
    {
        new Color(80, 170, 255), new Color(255, 140, 60), new Color(200, 90, 230), new Color(90, 220, 120),
    };

    private void Line(SpriteBatch sb, Texture2D px, Vector2 a, Vector2 b, Color c, float thick = 1f)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 0.5f) return;
        sb.Draw(px, a, null, c, MathF.Atan2(d.Y, d.X), new Vector2(0, 0.5f), new Vector2(len, thick), SpriteEffects.None, 0f);
    }

    /// <summary>
    /// F3 dev view: the tile grid the mechanics run on. Terrain tint, planted
    /// count per tile, cut lines, each planter's piece in their color with
    /// their current line, direction, state and path — so behavior can be
    /// read straight off the ground.
    /// </summary>
    public void DrawDebug(SpriteBatch sb)
    {
        var tiles = _map.Tiles;
        if (tiles == null) return;
        var px = _art.Solid("dbgPx", Color.White);
        var font = _art.Font;
        int ts = tiles.TileSize, th = tiles.TileHeight;
        float zoom = _cam.Zoom;

        int tx0 = Math.Max(0, (int)(_cam.Position.X / ts) - 1);
        int ty0 = Math.Max(0, (int)(_cam.Position.Y / th) - 1);
        int tx1 = Math.Min(tiles.Width - 1, (int)((_cam.Position.X + _cam.ViewWidth / zoom) / ts) + 1);
        int ty1 = Math.Min(tiles.Height - 1, (int)((_cam.Position.Y + _cam.ViewHeight / zoom + tiles.MaxElev) / th) + 1);

        // piece tint by tile: light blue when unowned, the owner's color when worked
        var tint = new Dictionary<int, Color>();
        var unowned = new Color(90, 170, 255) * 0.22f;
        if (Planters != null)
            foreach (var pc in Planters.Pieces)
            {
                Color c = unowned;
                if (pc.Owners.Count > 0)
                    c = CrewColors[Planters.Planters.IndexOf(pc.Owners[0]) % CrewColors.Length] * 0.26f;
                foreach (int ti in pc.Tiles) tint[ti] = c;
            }

        Vector2 Corner(int tx, int ty) => W2S(new Vector2(tx * ts, ty * th));

        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
            {
                var terr = tiles.TerrainAtTile(tx, ty);
                Color fill = terr.Name switch
                {
                    "forest" => new Color(0, 60, 0) * 0.35f,
                    "road" or "trail" => new Color(210, 190, 120) * 0.35f,
                    "swamp" => new Color(40, 80, 160) * 0.35f,
                    "rock" => new Color(160, 160, 170) * 0.35f,
                    "obstacle" => new Color(220, 40, 40) * 0.5f,
                    "cream" => new Color(230, 210, 150) * 0.2f,
                    _ => Color.Transparent
                };
                int ti = ty * tiles.Width + tx;
                if (tint.TryGetValue(ti, out var pieceTint)) fill = pieceTint;

                Vector2 a = Corner(tx, ty), b = Corner(tx + 1, ty + 1);
                var r = new Rectangle((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y));
                if (fill.A > 0) sb.Draw(px, r, fill);
                sb.Draw(px, new Rectangle(r.X, r.Y, r.Width, 1), Color.White * 0.12f);
                sb.Draw(px, new Rectangle(r.X, r.Y, 1, r.Height), Color.White * 0.12f);

                // planted: one dot per spot, green (red if faulted)
                int n = Planters?.PlantedAtTile(tx, ty) ?? 0;
                for (int s = 0; s < n; s++)
                {
                    bool bad = Planters.IsFault(tx, ty, s);
                    sb.Draw(px, new Rectangle(r.X + r.Width / 2 - 2 + s * 5, r.Y + r.Height / 2 - 2, 4, 4),
                        bad ? new Color(230, 60, 50) : new Color(90, 230, 110));
                }
                // cut line: orange frame; flag line: white frame
                if (Planters != null && Planters.IsCutLine(tx, ty))
                {
                    var oc = Planters.IsFlagLine(tx, ty) ? Color.White * 0.9f : new Color(255, 150, 40) * 0.9f;
                    sb.Draw(px, new Rectangle(r.X, r.Y, r.Width, 2), oc);
                    sb.Draw(px, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), oc);
                    sb.Draw(px, new Rectangle(r.X, r.Y, 2, r.Height), oc);
                    sb.Draw(px, new Rectangle(r.Right - 2, r.Y, 2, r.Height), oc);
                }
            }

        // piece labels: number, progress, owner
        if (Planters != null && font != null)
            foreach (var pc in Planters.Pieces)
            {
                int done = Planters.PlantedIn(pc);
                string who = pc.Owners.Count > 0 ? pc.Owners[0].Name.ToUpperInvariant() : "OPEN";
                Vector2 at = W2S(pc.Center);
                if (at.X < -200 || at.X > _cam.ViewWidth + 200 || at.Y < -50 || at.Y > _cam.ViewHeight + 50) continue;
                font.Draw(sb, $"#{pc.Id} {done}/{pc.Tiles.Count} {who}", at + new Vector2(-40, -6), 1.8f, new Color(160, 210, 255));
            }

        // caches
        foreach (var c in _caches)
            font?.Draw(sb, $"CACHE {c.Boxes}", W2S(c.Pos) + new Vector2(-24, 6), 1.6f, new Color(255, 222, 92));

        // planters: line tile, directions, path, state, reason
        if (Planters == null) return;
        for (int i = 0; i < Planters.Planters.Count; i++)
        {
            var p = Planters.Planters[i];
            var col = CrewColors[i % CrewColors.Length];
            Vector2 at = W2S(p.Pos);

            if (p.Path != null)
            {
                Vector2 prev = at;
                for (int k = Math.Max(0, p.PathIdx); k < p.Path.Count; k++)
                {
                    Vector2 nxt = W2S(p.Path[k]);
                    Line(sb, px, prev, nxt, col * 0.7f);
                    prev = nxt;
                }
            }
            if (p.HasFill)
            {
                Vector2 lc = W2S(new Vector2(p.LineTile.X * ts + ts / 2f, p.LineTile.Y * th + th / 2f));
                sb.Draw(px, new Rectangle((int)lc.X - 5, (int)lc.Y - 5, 10, 10), col);
                Vector2 dir = new Vector2(p.PlantingIn ? p.InDir.X : p.FillDir.X, p.PlantingIn ? p.InDir.Y : p.FillDir.Y);
                Line(sb, px, lc, lc + dir * 26f, col, 2f);
                Vector2 side = new Vector2(p.SideDir.X, p.SideDir.Y);
                Line(sb, px, lc, lc + side * 12f, Color.White * 0.8f, 2f);
            }
            if (font != null)
            {
                string state = p.State.ToString().ToUpperInvariant();
                if (p.HasFill && p.State != PlanterState.Idle && p.State != PlanterState.Done)
                    state += p.PlantingIn ? " IN" : " BACKFILL";
                font.Draw(sb, $"{p.Name} {state} BAG {p.Bag}", at + new Vector2(-30, -40), 1.5f, col);
                if (p.State == PlanterState.Idle && p.IdleReason.Length > 0)
                    font.Draw(sb, p.IdleReason, at + new Vector2(-30, -30), 1.5f, new Color(255, 90, 80));
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

        // C badge over the crewboss when the following crew can be put on this piece
        if (_player.CanAssignHere && _art.BadgeC != null)
            DrawBadge(sb, _art.BadgeC, pos + new Vector2(0, -44));

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

        // the cut side: a short tick off the shaft toward the piece
        Vector2 side = _player.CutRight ? new Vector2(-dir.Y, dir.X) : new Vector2(dir.Y, -dir.X);
        for (float f = 0.25f; f < 1f; f += 0.25f)
            sb.Draw(tex, start + dir * (len * f), null, Color.White * 0.9f, MathF.Atan2(side.Y, side.X),
                new Vector2(0, 0.5f), new Vector2(18f, 3f), SpriteEffects.None, 0f);

        // arrowhead: two strokes angled back from the tip
        Vector2 tip = start + dir * len;
        foreach (float da in stackalloc[] { 2.6f, -2.6f })
            sb.Draw(tex, tip, null, Color.White * 0.9f, ang + da, new Vector2(0, 0.5f), new Vector2(26f, 3f), SpriteEffects.None, 0f);
    }
}
