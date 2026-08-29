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

    // The piece, the way it's worked on the job:
    //   InDir    = "in" — from the front (road/cache) toward the back wall
    //   SideDir  = which side of the cut line the piece is on (in-and-right / in-and-left)
    //   CutStart = front tile of the cut line
    // Back line first (along the back wall), then BACKFILL: each line one
    // spacing toward the front, back and forth, so the back creeps toward the
    // cache. After every bag-up, PLANT IN along the next line off the cut
    // line (no dead walking) until you reach your back, then backfill on.
    public bool HasFill;
    public Point InDir;
    public Point SideDir;
    public Point CutStart;
    public Point FillDir;
    public Point StepDir;
    public Point LineTile;
    public bool PlantingIn;
    public int BagUps;

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
    /// If there's no one new to grab, the crew you're leading gets released here:
    /// they plant IN from this spot in the direction they were walking (that
    /// line is their cut line), then work the piece off it.
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
                p.HasFill = false;
                p.RepathTimer = 0;
                pickedAny = true;
            }
        }
        if (pickedAny) return;

        foreach (var p in Planters)
            if (p.State == PlanterState.Following)
            {
                p.Anchor = p.Pos;
                p.Path = null;
                InitPiece(p, DirVector(p.Dir), TileOf(p.Pos), TileOf(p.Pos), plantInFirst: true);
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
    /// bearing on their own, planting the cut line as they go. The line ends at
    /// unplantable ground, MaxLineTiles, or an empty bag — then they work the piece.
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
        p.HasFill = false;
    }

    /// <summary>
    /// The piece enclosing a tile: flood fill over plantable ground bounded by
    /// cut lines, roads, forest, swamp, rock. Returns null when the region is
    /// open-ended (bigger than PieceCapTiles) or the start isn't plantable —
    /// the planter then falls back to open radius planting.
    /// </summary>
    private HashSet<int> FloodPiece(Point start)
    {
        int w = _map.Width, h = _map.Height;
        int sx = Math.Clamp(start.X, 0, w - 1), sy = Math.Clamp(start.Y, 0, h - 1);
        if (!IsPieceGround(sx, sy)) return null;

        var piece = new HashSet<int>();
        var queue = new Queue<int>();
        int first = sy * w + sx;
        piece.Add(first);
        queue.Enqueue(first);

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
                    // march the aimed bearing, planting the cut line
                    StepToward(p, p.Pos + p.LineDir * 12f, dt);
                    MarkCutLine(p);

                    Vector2 ahead = p.Pos + p.LineDir * (_map.TileSize * 0.8f);
                    var ta = _map.TerrainAtWorld(ahead);
                    bool blocked = ta.Name != "slash" && ta.Name != "cream";
                    if (blocked || p.LineTiles >= MaxLineTiles || p.Bag <= 0)
                    {
                        // the cut's in. Back line along the wall, then backfill.
                        p.Anchor = (p.LineStart + p.Pos) / 2f;
                        InitPiece(p, p.LineDir, TileOf(p.LineStart), TileOf(p.Pos), plantInFirst: false);
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
                            StartPlantIn(p); // never dead-walk: plant in along the next line off the cut
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

    // ---------- the piece ----------

    private Point TileOf(Vector2 pos) => new((int)(pos.X / _map.TileSize), (int)(pos.Y / _map.TileHeight));

    private static Vector2 DirVector(string dir) => dir switch
    {
        "N" => new Vector2(0, -1),
        "S" => new Vector2(0, 1),
        "E" => new Vector2(1, 0),
        _ => new Vector2(-1, 0)
    };

    private static Point Axis(Vector2 v) => MathF.Abs(v.X) >= MathF.Abs(v.Y)
        ? new Point(v.X >= 0 ? 1 : -1, 0)
        : new Point(0, v.Y >= 0 ? 1 : -1);

    /// <summary>
    /// Set up the piece off a cut line running from cutStart to cutEnd along
    /// `inward`. Side = whichever side of the cut has more open ground
    /// ("in and right" / "in and left"). Line-in planters start their back
    /// line at the wall; released followers plant IN first (that line is
    /// their cut), then back-line and backfill.
    /// </summary>
    private void InitPiece(Planter p, Vector2 inward, Point cutStart, Point cutEnd, bool plantInFirst)
    {
        p.InDir = Axis(inward);
        p.CutStart = cutStart;
        p.BagUps = 0;
        p.HasFill = true;

        Point right = new(-p.InDir.Y, p.InDir.X); // right of "in" (screen y is down)
        Point left = new(p.InDir.Y, -p.InDir.X);
        p.SideDir = OpenGround(p, cutEnd, right) >= OpenGround(p, cutEnd, left) ? right : left;

        // the piece is the enclosed region beside the cut (null = open ground)
        p.PieceTiles = FloodPiece(cutEnd + p.SideDir) ?? FloodPiece(cutStart + p.SideDir);

        if (plantInFirst)
        {
            p.PlantingIn = true;
            p.FillDir = p.InDir;
            p.LineTile = cutStart;
        }
        else
        {
            // back line: along the wall, starting beside the cut's end
            p.PlantingIn = false;
            p.FillDir = p.SideDir;
            p.LineTile = cutEnd + p.SideDir;
        }
        p.StepDir = new Point(-p.InDir.X, -p.InDir.Y); // every step-over is one line toward the front
    }

    /// <summary>How much plantable ground sits on one side of a tile (5-deep probe, 6 wide).</summary>
    private int OpenGround(Planter p, Point at, Point side)
    {
        int n = 0;
        Point along = new(side.Y, side.X); // perpendicular to side (the "in" axis)
        for (int k = 1; k <= 5; k++)
            for (int j = -3; j <= 3; j++)
            {
                var t = new Point(at.X + side.X * k + along.X * j, at.Y + side.Y * k + along.Y * j);
                if (IsGround(t) && !IsCut(t)) n++;
            }
        return n;
    }

    /// <summary>Bag's full again at the cache: plant in along the next line off the cut line.</summary>
    private void StartPlantIn(Planter p)
    {
        if (!p.HasFill) return;
        p.BagUps++;
        var start = new Point(p.CutStart.X + p.SideDir.X * p.BagUps, p.CutStart.Y + p.SideDir.Y * p.BagUps);
        if (IsFillable(p, start) && HasRoom(start))
        {
            p.PlantingIn = true;
            p.FillDir = p.InDir;
            p.LineTile = start;
        }
        else
        {
            p.PlantingIn = false; // that line's already in — resume the backfill from wherever it stands
            p.FillDir = p.SideDir;
        }
    }

    private bool IsGround(Point t)
    {
        if (t.X < 0 || t.Y < 0 || t.X >= _map.Width || t.Y >= _map.Height) return false;
        var terr = _map.TerrainAtTile(t.X, t.Y);
        return terr.Name == "slash" || terr.Name == "cream";
    }

    private bool IsCut(Point t) => _cutLine[t.Y * _map.Width + t.X];

    /// <summary>Can this planter put a tree here: plantable ground, not a cut line, inside their piece / anchor radius.</summary>
    private bool IsFillable(Planter p, Point t)
    {
        if (!IsGround(t) || IsCut(t)) return false; // cut lines are walls: planted rows
        int ti = t.Y * _map.Width + t.X;
        if (p.PieceTiles != null) return p.PieceTiles.Contains(ti);
        int ax = (int)(p.Anchor.X / _map.TileSize), ay = (int)(p.Anchor.Y / _map.TileHeight);
        return Math.Max(Math.Abs(t.X - ax), Math.Abs(t.Y - ay)) <= AnchorRadiusTiles;
    }

    private bool HasRoom(Point t) => _planted[t.Y * _map.Width + t.X] < SpotsPerTile;

    private bool Open(Planter p, Point t) => IsFillable(p, t) && HasRoom(t);

    /// <summary>
    /// Next tile in the pattern. Planting in: straight along InDir until the
    /// back (trees already in, or a wall). Backfill: along FillDir to the
    /// wall, then one line toward the front and back the other way.
    /// </summary>
    private bool NextLineTile(Planter p, out Point tile)
    {
        if (Open(p, p.LineTile)) { tile = p.LineTile; return true; } // finish this tile

        if (p.PlantingIn)
        {
            Point ahead = p.LineTile + p.InDir;
            if (Open(p, ahead)) { tile = ahead; return true; }
            // reached the back: carry on backfilling from here, away from the cut
            p.PlantingIn = false;
            p.FillDir = p.SideDir;
        }

        Point next = p.LineTile + p.FillDir;
        if (Open(p, next)) { tile = next; return true; }

        // wall: step toward the front (skipping a small gap), turn around
        for (int k = 1; k <= 3; k++)
        {
            var over = new Point(p.LineTile.X + p.StepDir.X * k, p.LineTile.Y + p.StepDir.Y * k);
            if (Open(p, over))
            {
                p.FillDir = new Point(-p.FillDir.X, -p.FillDir.Y);
                tile = over;
                return true;
            }
        }
        tile = default;
        return false;
    }

    /// <summary>Anything left to plant for this planter (piece, or anchor radius)? Restart the pattern there.</summary>
    private bool NearestOpenTile(Planter p, out Point tile)
    {
        int ts = _map.TileSize, th = _map.TileHeight;
        int fromTx = (int)(p.Pos.X / ts), fromTy = (int)(p.Pos.Y / th);
        int bestTx = -1, bestTy = -1;
        float bestD = float.MaxValue;

        if (p.PieceTiles != null)
        {
            foreach (int ti in p.PieceTiles)
            {
                var t = new Point(ti % _map.Width, ti / _map.Width);
                if (!Open(p, t)) continue;
                float d = Vector2.DistanceSquared(new Vector2(t.X, t.Y), new Vector2(fromTx, fromTy));
                if (d < bestD) { bestD = d; bestTx = t.X; bestTy = t.Y; }
            }
        }
        else
        {
            for (int r = 0; r <= AnchorRadiusTiles * 2 && bestTx < 0; r++)
                for (int ty = fromTy - r; ty <= fromTy + r; ty++)
                    for (int tx = fromTx - r; tx <= fromTx + r; tx++)
                    {
                        if (Math.Max(Math.Abs(tx - fromTx), Math.Abs(ty - fromTy)) != r) continue; // ring only
                        var t = new Point(tx, ty);
                        if (!Open(p, t)) continue;
                        float d = Vector2.DistanceSquared(new Vector2(tx, ty), new Vector2(fromTx, fromTy));
                        if (d < bestD) { bestD = d; bestTx = tx; bestTy = ty; }
                    }
        }
        tile = new Point(bestTx, bestTy);
        return bestTx >= 0;
    }

    private void PickNextSpot(Planter p)
    {
        if (!p.HasFill) InitPiece(p, DirVector(p.Dir), TileOf(p.Pos), TileOf(p.Pos), plantInFirst: true);

        if (!NextLineTile(p, out Point tile))
        {
            // pattern exhausted; sweep any pocket the lines missed, else the piece is done
            if (!NearestOpenTile(p, out tile))
            {
                p.State = PlanterState.Done; // piece finished — come move me, boss
                return;
            }
            p.PlantingIn = false;
            p.FillDir = p.SideDir;
        }
        p.LineTile = tile;

        int ts = _map.TileSize, th = _map.TileHeight;
        int spot = _planted[tile.Y * _map.Width + tile.X];
        p.PlantSpot = SpotPos(tile.X, tile.Y, spot, ts, th);
        var target = new Vector2(tile.X * ts + ts / 2f, tile.Y * th + th / 2f);
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
