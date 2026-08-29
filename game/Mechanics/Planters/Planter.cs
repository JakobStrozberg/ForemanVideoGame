using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Crewboss.Mechanics.Planters;

public enum PlanterState
{
    Waiting,       // at the trucks, day start
    Following,     // trailing the crewboss
    CuttingIn,     // walking the boss's exact path, planting the cut line
    MovingToPlant, // walking to the next plant spot
    Planting,      // shovel in the ground
    MovingToCache, // bag empty, walking to a cache
    Idle,          // stuck: no trees available — losing money, fix it
    Done,          // piece (or area around the anchor) fully planted
}

public class Planter
{
    public string Name = "";
    public int Variant;
    public Vector2 Pos;
    public PlanterState State = PlanterState.Waiting;
    public float StateTimer;
    public int Bag;
    public Vector2 Anchor;
    public string Dir = "S";
    public float WalkAnim;
    public bool Walking;

    public List<Vector2> Path;
    public int PathIdx;
    public Vector2? PlantSpot;
    public float RepathTimer;
    public float RetryTimer;

    public HashSet<int> PieceTiles;   // assigned piece (null = open planting)

    // line-in: aimed from a cache, walked solo
    public Vector2 LineDir;
    public Vector2 LineStart;
    public int LineTiles;
    public int LastLineTile = -1;

    // quality: hidden meter that drifts down; low meter = faulted trees.
    // Coaching resets it (and pauses them for a moment).
    public float Quality = 100f;
    public float DriftRate;
    public float CoachTimer;
}

/// <summary>
/// Autonomous planter crew. Planters plant for themselves — the player just
/// moves them, and keeps caches stocked so they never go idle.
/// </summary>
public class PlanterSystem
{
    public const int BagSize = 40;        // trees per bag-up (one box)
    public const float PlantTime = 1.4f;  // seconds per tree
    public const float WalkSpeed = 78f;
    public const float FollowSpeed = 135f; // hustling behind the boss
    public const int SpotsPerTile = 4;    // 2x2 spots per plantable tile
    public const int AnchorRadiusTiles = 14;

    public const int PieceCapTiles = 800; // bigger than this = not really "cut in"
    public const int MaxLineTiles = 45;   // a line-in ends eventually even on open ground

    public readonly List<Planter> Planters = new();
    public int TreesPlanted { get; private set; }
    public int Faults { get; private set; }
    public float IdleSeconds { get; private set; }

    private readonly TileMap _map;
    private readonly List<CacheEntity> _caches;
    private readonly byte[] _planted;   // per-tile planted spot count
    private readonly byte[] _faultBits; // per-tile fault flags, one bit per spot
    private readonly bool[] _cutLine;   // tiles that are part of a cut line (piece boundaries)

    private static readonly string[] CrewNames = { "Maya", "Cole", "Jess", "Theo" };

    public PlanterSystem(TileMap map, List<CacheEntity> caches)
    {
        _map = map;
        _caches = caches;
        _planted = new byte[map.Width * map.Height];
        _faultBits = new byte[map.Width * map.Height];
        _cutLine = new bool[map.Width * map.Height];
    }

    public void SpawnCrew(Vector2 near)
    {
        for (int i = 0; i < CrewNames.Length; i++)
            Planters.Add(new Planter
            {
                Name = CrewNames[i],
                Variant = i,
                Pos = near + new Vector2(-40 + i * 26, -28),
                State = PlanterState.Waiting,
                DriftRate = 0.10f + i * 0.05f, // Theo slips fastest — watch him
            });
    }

    public bool IsFault(int tx, int ty, int spot) =>
        tx >= 0 && ty >= 0 && tx < _map.Width && ty < _map.Height &&
        (_faultBits[ty * _map.Width + tx] & (1 << spot)) != 0;

    /// <summary>Coach the planter: quality snaps back to 100, they pause a beat.</summary>
    public void Coach(Planter p)
    {
        p.Quality = 100f;
        p.CoachTimer = 2.5f;
    }

    public Planter FindCoachTarget(Vector2 pos)
    {
        Planter best = null;
        float bestD = 55f;
        foreach (var p in Planters)
        {
            if (p.State == PlanterState.Waiting || p.State == PlanterState.Following) continue;
            float d = Vector2.Distance(p.Pos, pos);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    public byte PlantedAtTile(int tx, int ty) =>
        tx < 0 || ty < 0 || tx >= _map.Width || ty >= _map.Height ? (byte)0 : _planted[ty * _map.Width + tx];

    /// <summary>
    /// F key: every non-following planter within reach joins the line behind you.
    /// If there's no one new to grab, the crew you're leading gets released here
    /// (they anchor and start planting).
    /// </summary>
    public void ToggleCrew(Vector2 playerPos)
    {
        bool pickedAny = false;
        foreach (var p in Planters)
        {
            if (p.State == PlanterState.Following || p.State == PlanterState.CuttingIn) continue;
            if (Vector2.Distance(p.Pos, playerPos) < 90f)
            {
                p.State = PlanterState.Following;
                p.Path = null;
                p.PlantSpot = null;
                p.PieceTiles = null;
                p.RepathTimer = 0;
                pickedAny = true;
            }
        }
        if (pickedAny) return;

        // release the crew: each planter gets the piece enclosing where they stand
        foreach (var p in Planters)
            if (p.State == PlanterState.Following)
            {
                p.Anchor = p.Pos;
                p.Path = null;
                p.PieceTiles = FloodPiece(p.Pos);
                if (p.Bag > 0) PickNextSpot(p);
                else GoBagUp(p);
            }
    }

    /// <summary>Who gets the line-in: the lead follower, else the nearest free planter by the cache.</summary>
    public Planter FindLinePlanter(Vector2 cachePos)
    {
        foreach (var p in Planters)
            if (p.State == PlanterState.Following) return p;

        Planter best = null;
        float bestD = 110f;
        foreach (var p in Planters)
        {
            if (p.State != PlanterState.Waiting && p.State != PlanterState.Idle &&
                p.State != PlanterState.Done) continue;
            float d = Vector2.Distance(p.Pos, cachePos);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    /// <summary>
    /// Aimed line-in: the planter bags up at the cache, then marches the given
    /// bearing on their own, planting the line as they go. The line ends at
    /// unplantable ground, MaxLineTiles, or an empty bag — then they work off it.
    /// </summary>
    public void StartLineIn(Planter p, CacheEntity cache, Vector2 dir)
    {
        if (p.Bag <= 0 && cache.Boxes > 0)
        {
            cache.Boxes--;
            p.Bag = BagSize;
        }
        p.Pos = cache.Pos + dir * 14f;
        p.State = PlanterState.CuttingIn;
        p.LineDir = dir;
        p.LineStart = p.Pos;
        p.LineTiles = 0;
        p.LastLineTile = -1;
        p.Path = null;
        p.PieceTiles = null;
        p.PlantSpot = null;
    }

    /// <summary>
    /// The piece enclosing a point: flood fill over plantable ground bounded by
    /// cut lines, roads, forest, swamp, rock. Returns null when the region is
    /// open-ended (bigger than PieceCapTiles) or the start isn't plantable —
    /// the planter then falls back to open radius planting.
    /// </summary>
    private HashSet<int> FloodPiece(Vector2 pos)
    {
        int w = _map.Width, h = _map.Height;
        int sx = Math.Clamp((int)(pos.X / _map.TileSize), 0, w - 1);
        int sy = Math.Clamp((int)(pos.Y / _map.TileHeight), 0, h - 1);
        if (!IsPieceGround(sx, sy)) return null;

        var piece = new HashSet<int>();
        var queue = new Queue<int>();
        int start = sy * w + sx;
        piece.Add(start);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            if (piece.Count > PieceCapTiles) return null; // open ground, not a piece
            int cur = queue.Dequeue();
            int cx = cur % w, cy = cur / w;
            Span<(int, int)> next = stackalloc[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) };
            foreach (var (nx, ny) in next)
            {
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int ni = ny * w + nx;
                if (piece.Contains(ni) || !IsPieceGround(nx, ny)) continue;
                piece.Add(ni);
                queue.Enqueue(ni);
            }
        }
        return piece;
    }

    private bool IsPieceGround(int tx, int ty)
    {
        if (_cutLine[ty * _map.Width + tx]) return false;
        var t = _map.TerrainAtTile(tx, ty);
        return t.Name == "slash" || t.Name == "cream";
    }

    public void Update(float dt, Vector2 playerPos)
    {
        foreach (var p in Planters)
        {
            p.Walking = false;

            // being coached: stand and listen
            if (p.CoachTimer > 0)
            {
                p.CoachTimer -= dt;
                continue;
            }

            // quality drifts while working; idle time is score damage
            if (p.State != PlanterState.Waiting && p.State != PlanterState.Following)
                p.Quality = MathF.Max(0f, p.Quality - p.DriftRate * dt);
            if (p.State == PlanterState.Idle)
                IdleSeconds += dt;

            switch (p.State)
            {
                case PlanterState.Following:
                    UpdateFollowing(p, dt, playerPos);
                    break;

                case PlanterState.CuttingIn:
                {
                    // march the aimed bearing, planting the line
                    StepToward(p, p.Pos + p.LineDir * 12f, dt);
                    MarkCutLine(p);

                    Vector2 ahead = p.Pos + p.LineDir * (_map.TileSize * 0.8f);
                    var ta = _map.TerrainAtWorld(ahead);
                    bool blocked = ta.Name != "slash" && ta.Name != "cream";
                    if (blocked || p.LineTiles >= MaxLineTiles || p.Bag <= 0)
                    {
                        // line's in — now work off it
                        p.Anchor = (p.LineStart + p.Pos) / 2f;
                        if (p.Bag > 0) PickNextSpot(p);
                        else GoBagUp(p);
                    }
                    break;
                }

                case PlanterState.MovingToPlant:
                    if (MoveAlong(p, dt))
                    {
                        // path ends at the tile center; step to the exact spot
                        if (p.PlantSpot.HasValue && Vector2.Distance(p.Pos, p.PlantSpot.Value) > 3)
                            StepToward(p, p.PlantSpot.Value, dt);
                        else
                        {
                            p.State = PlanterState.Planting;
                            p.StateTimer = PlantTime;
                        }
                    }
                    break;

                case PlanterState.Planting:
                    p.StateTimer -= dt;
                    if (p.StateTimer <= 0)
                    {
                        CommitTree(p);
                        p.Bag--;
                        if (p.Bag <= 0) GoBagUp(p);
                        else PickNextSpot(p);
                    }
                    break;

                case PlanterState.MovingToCache:
                    if (MoveAlong(p, dt))
                    {
                        var cache = NearestCacheWithTrees(p.Pos, 60f);
                        if (cache != null)
                        {
                            cache.Boxes--;
                            p.Bag = BagSize;
                            PickNextSpot(p);
                        }
                        else GoBagUp(p); // it drained while we walked
                    }
                    break;

                case PlanterState.Idle:
                    p.RetryTimer -= dt;
                    if (p.RetryTimer <= 0)
                    {
                        p.RetryTimer = 2f;
                        if (p.Bag > 0) PickNextSpot(p);
                        else GoBagUp(p);
                    }
                    break;
            }
        }
    }

    // ---------- state helpers ----------

    /// <summary>Mark the tile the line planter stands on as line and plant its row (2 spots).</summary>
    private void MarkCutLine(Planter p)
    {
        int tx = (int)(p.Pos.X / _map.TileSize), ty = (int)(p.Pos.Y / _map.TileHeight);
        if (tx < 0 || ty < 0 || tx >= _map.Width || ty >= _map.Height) return;
        int ti = ty * _map.Width + tx;
        if (ti == p.LastLineTile) return;
        p.LastLineTile = ti;

        var t = _map.TerrainAtTile(tx, ty);
        if (t.Name != "slash" && t.Name != "cream") return;
        _cutLine[ti] = true;
        p.LineTiles++;

        while (_planted[ti] < 2 && p.Bag > 0)
        {
            RollFault(p, ti, _planted[ti]);
            _planted[ti]++;
            p.Bag--;
            TreesPlanted++;
        }
    }

    /// <summary>Low quality meter = chance this tree goes in bad (silently flagged).</summary>
    private void RollFault(Planter p, int tile, int spot)
    {
        if (p.Quality >= 65f) return;
        uint h = (uint)(tile * 7349 + spot * 131 + 977);
        h = (h ^ (h >> 13)) * 1274126177u;
        float roll = (h & 0xFFFF) / 65536f;
        if (roll < (65f - p.Quality) / 65f * 0.85f)
        {
            _faultBits[tile] |= (byte)(1 << spot);
            Faults++;
        }
    }

    private void UpdateFollowing(Planter p, float dt, Vector2 playerPos)
    {
        float dist = Vector2.Distance(p.Pos, playerPos);
        if (dist < 46) { p.Path = null; return; }

        p.RepathTimer -= dt;
        if (p.Path == null || p.RepathTimer <= 0)
        {
            p.Path = Pathfinder.FindPath(_map, p.Pos, playerPos, PlanterCost);
            p.PathIdx = 0;
            p.RepathTimer = 0.4f;
        }
        MoveAlong(p, dt, FollowSpeed);
    }

    private void GoBagUp(Planter p)
    {
        var cache = NearestCacheWithTrees(p.Pos, float.MaxValue);
        if (cache == null)
        {
            p.State = PlanterState.Idle; // no trees anywhere — the crewboss failed
            p.RetryTimer = 2f;
            return;
        }
        p.Path = Pathfinder.FindPath(_map, p.Pos, cache.Pos, PlanterCost);
        p.PathIdx = 0;
        p.State = p.Path != null ? PlanterState.MovingToCache : PlanterState.Idle;
        p.RetryTimer = 2f;
    }

    private void PickNextSpot(Planter p)
    {
        int ts = _map.TileSize, th = _map.TileHeight;
        int anchorTx = (int)(p.Anchor.X / ts), anchorTy = (int)(p.Anchor.Y / th);
        int fromTx = (int)(p.Pos.X / ts), fromTy = (int)(p.Pos.Y / th);

        int bestTx = -1, bestTy = -1;
        float bestD = float.MaxValue;

        if (p.PieceTiles != null)
        {
            // cut-in piece: work only tiles inside the piece
            foreach (int ti in p.PieceTiles)
            {
                if (_planted[ti] >= SpotsPerTile) continue;
                int tx = ti % _map.Width, ty = ti / _map.Width;
                float d = Vector2.DistanceSquared(new Vector2(tx, ty), new Vector2(fromTx, fromTy));
                if (d < bestD) { bestD = d; bestTx = tx; bestTy = ty; }
            }
        }
        else
        {
            // open planting: ring search around the planter, limited to the anchor's radius
            for (int r = 0; r <= AnchorRadiusTiles * 2 && bestTx < 0; r++)
            {
                for (int ty = fromTy - r; ty <= fromTy + r; ty++)
                    for (int tx = fromTx - r; tx <= fromTx + r; tx++)
                    {
                        if (Math.Max(Math.Abs(tx - fromTx), Math.Abs(ty - fromTy)) != r) continue; // ring only
                        if (tx < 0 || ty < 0 || tx >= _map.Width || ty >= _map.Height) continue;
                        if (Math.Max(Math.Abs(tx - anchorTx), Math.Abs(ty - anchorTy)) > AnchorRadiusTiles) continue;
                        if (_planted[ty * _map.Width + tx] >= SpotsPerTile) continue;
                        var t = _map.TerrainAtTile(tx, ty);
                        if (t.Name != "slash" && t.Name != "cream") continue;
                        float d = Vector2.DistanceSquared(new Vector2(tx, ty), new Vector2(fromTx, fromTy));
                        if (d < bestD) { bestD = d; bestTx = tx; bestTy = ty; }
                    }
            }
        }

        if (bestTx < 0)
        {
            p.State = PlanterState.Done; // piece finished — come move me, boss
            return;
        }

        int spot = _planted[bestTy * _map.Width + bestTx];
        p.PlantSpot = SpotPos(bestTx, bestTy, spot, ts, th);
        var target = new Vector2(bestTx * ts + ts / 2f, bestTy * th + th / 2f);
        p.Path = Pathfinder.FindPath(_map, p.Pos, target, PlanterCost);
        p.PathIdx = 0;
        p.State = p.Path != null ? PlanterState.MovingToPlant : PlanterState.Idle;
    }

    private void CommitTree(Planter p)
    {
        int ts = _map.TileSize, th = _map.TileHeight;
        if (!p.PlantSpot.HasValue) return;
        int tx = (int)(p.PlantSpot.Value.X / ts), ty = (int)(p.PlantSpot.Value.Y / th);
        if (tx >= 0 && ty >= 0 && tx < _map.Width && ty < _map.Height &&
            _planted[ty * _map.Width + tx] < SpotsPerTile)
        {
            int ti = ty * _map.Width + tx;
            RollFault(p, ti, _planted[ti]);
            _planted[ti]++;
            TreesPlanted++;
        }
    }

    /// <summary>Deterministic sub-tile position for spot index 0..3 (2x2 + jitter).</summary>
    public static Vector2 SpotPos(int tx, int ty, int spot, int ts, int th)
    {
        int qx = spot % 2, qy = spot / 2;
        uint h = (uint)(tx * 7349 + ty * 9241 + spot * 131);
        h = (h ^ (h >> 13)) * 1274126177u;
        float jx = (h & 0xFF) / 255f * 6f - 3f;
        float jy = ((h >> 8) & 0xFF) / 255f * 4f - 2f;
        return new Vector2(
            tx * ts + ts / 4f + qx * ts / 2f + jx,
            ty * th + th / 4f + qy * th / 2f + jy);
    }

    // ---------- movement ----------

    /// <summary>Advance along the current path. True when the path is finished (or absent).</summary>
    private bool MoveAlong(Planter p, float dt, float speed = WalkSpeed)
    {
        if (p.Path == null || p.PathIdx >= p.Path.Count) return true;
        Vector2 target = p.Path[p.PathIdx];
        if (Vector2.Distance(p.Pos, target) < 4)
        {
            p.PathIdx++;
            return p.PathIdx >= p.Path.Count;
        }
        StepToward(p, target, dt, speed);
        return false;
    }

    private void StepToward(Planter p, Vector2 target, float dt, float speed = WalkSpeed)
    {
        Vector2 d = target - p.Pos;
        if (d == Vector2.Zero) return;
        d.Normalize();
        float mult = TerrainMult(p.Pos);
        p.Pos += d * speed * mult * dt;
        p.Walking = true;
        p.WalkAnim += dt;
        p.Dir = MathF.Abs(d.X) >= MathF.Abs(d.Y) ? (d.X >= 0 ? "E" : "W") : (d.Y >= 0 ? "S" : "N");
    }

    private float TerrainMult(Vector2 pos)
    {
        var t = _map.TerrainAtWorld(pos);
        return t.Name switch
        {
            "forest" => 0.4f,
            "swamp" => 0.5f,
            "obstacle" => 0.4f, // squeeze past, don't get stuck
            "outside" => 0.4f,
            _ => MathF.Max(t.Speed, 0.55f)
        };
    }

    private float PlanterCost(TileTerrain t) => t.Name switch
    {
        "obstacle" => float.PositiveInfinity,
        "outside" => float.PositiveInfinity,
        "forest" => 3f,
        "swamp" => 2f,
        "road" => 1f,
        "trail" => 1.1f,
        "cream" => 1.2f,
        _ => 1.4f
    };

    private CacheEntity NearestCacheWithTrees(Vector2 pos, float maxDist)
    {
        CacheEntity best = null;
        float bestD = maxDist;
        foreach (var c in _caches)
        {
            if (c.Boxes <= 0) continue;
            float d = Vector2.Distance(pos, c.Pos);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }
}
