using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Crewboss.Mechanics.Planters;

public enum PlanterState
{
    Waiting,       // at the trucks, day start
    Following,     // trailing the crewboss
    CuttingIn,     // walking the aimed line from the road, planting the cut
    MovingToPlant, // walking to the next plant spot (or back to an unfinished cut)
    Planting,      // shovel in the ground
    MovingToCache, // bag empty, walking to a cache
    Idle,          // stuck: no trees, no piece, no path — losing money, fix it
    Done,          // the piece is planted out — come move me
}

/// <summary>
/// A region of plantable ground enclosed by walls: boundaries (roads,
/// treeline, swamp, rock) and the lines planters cut. Pieces always tile the
/// block; every wall-to-wall cut splits the piece it crosses in two.
/// </summary>
public sealed class Piece
{
    public int Id;
    public HashSet<int> Tiles = new();
    /// <summary>Tiles along the road-facing edge — where you walk in from.</summary>
    public List<int> Front = new();
    /// <summary>From the front into the piece, toward the back wall.</summary>
    public Point InDir = new(0, -1);
    public List<Planter> Owners = new();
    public Vector2 Center;
}

public class Planter
{
    public string Name = "";
    public int Variant;
    public Vector2 Pos;
    public PlanterState State = PlanterState.Waiting;
    public float StateTimer;
    public int Bag;
    public string Dir = "S";
    public float WalkAnim;
    public bool Walking;

    public List<Vector2> Path;
    public int PathIdx;
    public Vector2? PlantSpot;
    public float RepathTimer;
    public float RetryTimer;

    /// <summary>The piece this planter works. Null = nothing assigned.</summary>
    public Piece Piece;

    // cutting in: walking the aimed bearing from a road cache, planting the line
    public Vector2 LineDir;
    public Vector2 LineStart;
    public int LineTiles;
    public int LastLineTile = -1;
    public readonly List<int> CutTiles = new();
    public Piece CutPiece;          // the piece the cut is splitting
    public bool ResumeCut;          // bag ran out mid-cut: bag up, come back, finish it
    public Vector2 CutResumeAt;

    // Working the piece:
    //   InDir    = "in" — from the front (road/cache) toward the back wall
    //   SideDir  = which side of the cut line the piece is on (in-and-right / in-and-left)
    //   CutStart = front tile of the cut line (or where they were released)
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
    public bool CutRight = true;  // in-and-right (true) or in-and-left
    public string IdleReason = ""; // why a planter is stuck (dev view)

    // quality: hidden meter that drifts down; low meter = faulted trees.
    // Coaching resets it (and pauses them for a moment).
    public float Quality = 100f;
    public float DriftRate;
    public float CoachTimer;
}

/// <summary>
/// Autonomous planter crew working pieces. The crewboss cuts them in and
/// keeps the road caches stocked; planters plant for themselves.
/// </summary>
public class PlanterSystem
{
    public const int BagSize = 100;       // trees per bag-up (one box)
    public const float PlantTime = 1.0f;  // seconds per tree
    public const float WalkSpeed = 78f;
    public const float FollowSpeed = 135f; // hustling behind the boss
    public const int SpotsPerTile = 1;    // one tree per 16x11 tile: lines 16px apart
    public const int MaxLineTiles = 120;  // a cut ends eventually even on open ground

    public readonly List<Planter> Planters = new();
    public readonly List<Piece> Pieces = new();
    public int TreesPlanted { get; private set; }
    public int Faults { get; private set; }
    public float IdleSeconds { get; private set; }

    private readonly TileMap _map;
    private readonly List<CacheEntity> _caches;
    private readonly byte[] _planted;   // per-tile planted spot count
    private readonly byte[] _faultBits; // per-tile fault flags, one bit per spot
    private readonly bool[] _wall;      // lines planters cut: piece boundaries
    private readonly int[] _pieceId;    // per tile, 0 = no piece
    private readonly Dictionary<int, Piece> _byId = new();
    private int _nextPieceId = 1;

    // flag lines: tape run flag to flag by the crewboss — walls with no trees
    private readonly HashSet<int> _flagTiles = new();
    private readonly List<Point> _openLine = new();   // flags of the line being run
    public readonly List<Point> Flags = new();        // every flag dropped (render)
    public readonly List<(Point a, Point b)> FlagSegments = new();
    public const int NewLineDistance = 40;            // tiles: farther than this starts a fresh line

    private static readonly string[] CrewNames = { "Maya", "Cole", "Jess", "Theo" };

    public PlanterSystem(TileMap map, List<CacheEntity> caches)
    {
        _map = map;
        _caches = caches;
        int n = map.Width * map.Height;
        _planted = new byte[n];
        _faultBits = new byte[n];
        _wall = new bool[n];
        _pieceId = new int[n];
        BuildInitialPieces();
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

    // ---------- queries ----------

    public bool IsFault(int tx, int ty, int spot) =>
        InBounds(tx, ty) && (_faultBits[ty * _map.Width + tx] & (1 << spot)) != 0;

    public bool IsCutLine(int tx, int ty) => InBounds(tx, ty) && _wall[ty * _map.Width + tx];
    public bool IsFlagLine(int tx, int ty) => InBounds(tx, ty) && _flagTiles.Contains(ty * _map.Width + tx);
    /// <summary>Where the line being run currently ends (null when no line is open).</summary>
    public Point? OpenLineEnd => _openLine.Count > 0 ? _openLine[^1] : null;

    public byte PlantedAtTile(int tx, int ty) => InBounds(tx, ty) ? _planted[ty * _map.Width + tx] : (byte)0;

    public Piece PieceAt(int tx, int ty) =>
        InBounds(tx, ty) && _pieceId[ty * _map.Width + tx] != 0 ? _byId[_pieceId[ty * _map.Width + tx]] : null;

    public Piece PieceAt(Vector2 world) => PieceAt((int)(world.X / _map.TileSize), (int)(world.Y / _map.TileHeight));

    public int PlantedIn(Piece pc)
    {
        int n = 0;
        foreach (int ti in pc.Tiles) n += _planted[ti];
        return n;
    }

    private bool InBounds(int tx, int ty) => tx >= 0 && ty >= 0 && tx < _map.Width && ty < _map.Height;

    private bool IsGround(Point t)
    {
        if (!InBounds(t.X, t.Y)) return false;
        var terr = _map.TerrainAtTile(t.X, t.Y);
        return terr.Name == "slash" || terr.Name == "cream";
    }

    private bool IsRoad(Point t)
    {
        if (!InBounds(t.X, t.Y)) return false;
        var terr = _map.TerrainAtTile(t.X, t.Y);
        return terr.Name == "road" || terr.Name == "trail";
    }

    private Point TileOf(Vector2 pos) => new((int)(pos.X / _map.TileSize), (int)(pos.Y / _map.TileHeight));
    private Vector2 CenterOf(Point t) => new(t.X * _map.TileSize + _map.TileSize / 2f, t.Y * _map.TileHeight + _map.TileHeight / 2f);
    private int Index(Point t) => t.Y * _map.Width + t.X;
    private Point TileOfIndex(int ti) => new(ti % _map.Width, ti / _map.Width);

    // ---------- pieces ----------

    /// <summary>The block's natural regions: plantable ground split by roads, forest, swamp, rock.</summary>
    private void BuildInitialPieces()
    {
        for (int ty = 0; ty < _map.Height; ty++)
            for (int tx = 0; tx < _map.Width; tx++)
            {
                var t = new Point(tx, ty);
                if (!IsGround(t) || _pieceId[Index(t)] != 0) continue;
                MakePiece(Flood(t, null), null);
            }
    }

    /// <summary>Connected plantable tiles from start, not crossing walls; optionally limited to a set.</summary>
    private HashSet<int> Flood(Point start, HashSet<int> within)
    {
        var region = new HashSet<int>();
        var queue = new Queue<Point>();
        int si = Index(start);
        if (!IsGround(start) || _wall[si] || (within != null && !within.Contains(si))) return region;
        region.Add(si);
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            Span<Point> next = stackalloc[] { new Point(c.X + 1, c.Y), new Point(c.X - 1, c.Y), new Point(c.X, c.Y + 1), new Point(c.X, c.Y - 1) };
            foreach (var nb in next)
            {
                if (!IsGround(nb)) continue;
                int ni = Index(nb);
                if (region.Contains(ni) || _wall[ni]) continue;
                if (within != null && !within.Contains(ni)) continue;
                region.Add(ni);
                queue.Enqueue(nb);
            }
        }
        return region;
    }

    private Piece MakePiece(HashSet<int> tiles, Point? inDir)
    {
        var pc = new Piece { Id = _nextPieceId++, Tiles = tiles };
        foreach (int ti in tiles) _pieceId[ti] = pc.Id;
        _byId[pc.Id] = pc;
        Pieces.Add(pc);
        ComputeFront(pc, inDir);
        return pc;
    }

    /// <summary>Front = tiles touching a road (else any wall); InDir = from the front toward the middle.</summary>
    private void ComputeFront(Piece pc, Point? inDir)
    {
        pc.Front.Clear();
        var fallback = new List<int>();
        float cx = 0, cy = 0;
        foreach (int ti in pc.Tiles)
        {
            var t = TileOfIndex(ti);
            cx += t.X; cy += t.Y;
            bool road = false, edge = false;
            Span<Point> next = stackalloc[] { new Point(t.X + 1, t.Y), new Point(t.X - 1, t.Y), new Point(t.X, t.Y + 1), new Point(t.X, t.Y - 1) };
            foreach (var nb in next)
            {
                if (IsRoad(nb)) road = true;
                else if (!IsGround(nb) || _wall[Index(nb)]) edge = true;
            }
            if (road) pc.Front.Add(ti);
            else if (edge) fallback.Add(ti);
        }
        if (pc.Front.Count == 0) pc.Front.AddRange(fallback);
        cx /= Math.Max(1, pc.Tiles.Count); cy /= Math.Max(1, pc.Tiles.Count);
        pc.Center = new Vector2(cx * _map.TileSize + _map.TileSize / 2f, cy * _map.TileHeight + _map.TileHeight / 2f);

        if (inDir.HasValue) { pc.InDir = inDir.Value; return; }
        if (pc.Front.Count == 0) { pc.InDir = new Point(0, -1); return; }
        float fx = 0, fy = 0;
        foreach (int ti in pc.Front) { var t = TileOfIndex(ti); fx += t.X; fy += t.Y; }
        fx /= pc.Front.Count; fy /= pc.Front.Count;
        pc.InDir = Axis(new Vector2(cx - fx, cy - fy));
    }

    /// <summary>
    /// Walls changed inside a piece (a cut went wall to wall): re-flood it into
    /// its connected parts. Owners keep the part their current line sits in.
    /// </summary>
    private void SplitPiece(Piece old, Point? inDir)
    {
        var owners = new List<Planter>(old.Owners);
        var remaining = new HashSet<int>();
        foreach (int ti in old.Tiles) { _pieceId[ti] = 0; if (!_wall[ti]) remaining.Add(ti); }
        Pieces.Remove(old);
        _byId.Remove(old.Id);

        var parts = new List<Piece>();
        foreach (int ti in remaining)
        {
            if (_pieceId[ti] != 0) continue;
            var region = Flood(TileOfIndex(ti), remaining);
            if (region.Count == 0) continue;
            parts.Add(MakePiece(region, inDir));
        }

        foreach (var o in owners)
        {
            o.Piece = null;
            var at = PieceAt(o.LineTile.X, o.LineTile.Y) ?? PieceAt(o.Pos);
            if (at == null)
            {
                float best = float.MaxValue;
                foreach (var pc in parts)
                {
                    float d = Vector2.DistanceSquared(pc.Center, o.Pos);
                    if (d < best) { best = d; at = pc; }
                }
            }
            if (at != null) { o.Piece = at; at.Owners.Add(o); }
        }
    }

    private static Point Axis(Vector2 v) => MathF.Abs(v.X) >= MathF.Abs(v.Y)
        ? new Point(v.X >= 0 ? 1 : -1, 0)
        : new Point(0, v.Y >= 0 ? 1 : -1);

    // ---------- flag lines ----------

    /// <summary>
    /// G: drop a flag. The first flag starts a line; each next flag runs tape
    /// straight from the last one — a wall for pieces, no trees. Landing on a
    /// boundary or another line closes the line (and splits the pieces it
    /// crossed); the next flag starts fresh. A flag far from the last one
    /// starts a new line instead of an accidental wall.
    /// </summary>
    public void DropFlag(Vector2 worldPos)
    {
        var t = TileOf(worldPos);
        if (!InBounds(t.X, t.Y)) return;

        if (_openLine.Count > 0 && Math.Max(Math.Abs(t.X - _openLine[^1].X), Math.Abs(t.Y - _openLine[^1].Y)) > NewLineDistance)
            _openLine.Clear();

        if (_openLine.Count == 0)
        {
            _openLine.Add(t);
            Flags.Add(t);
            // a first flag dropped on ground is itself a wall tile
            if (IsGround(t) && !_wall[Index(t)]) WallTile(t, null);
            return;
        }

        var a = _openLine[^1];
        if (a == t) return;
        bool closes = !IsGround(t) || _wall[Index(t)];

        var touched = new HashSet<Piece>();
        foreach (var tile in Raster(a, t))
        {
            if (!IsGround(tile) || _wall[Index(tile)]) continue;
            WallTile(tile, touched);
        }
        foreach (var pc in touched)
            if (Pieces.Contains(pc)) SplitPiece(pc, null);

        FlagSegments.Add((a, t));
        Flags.Add(t);
        if (closes) _openLine.Clear();
        else _openLine.Add(t);
    }

    private void WallTile(Point tile, HashSet<Piece> touched)
    {
        int ti = Index(tile);
        var pc = PieceAt(tile.X, tile.Y);
        _wall[ti] = true;
        _flagTiles.Add(ti);
        if (pc != null)
        {
            pc.Tiles.Remove(ti);
            _pieceId[ti] = 0;
            touched?.Add(pc);
            if (touched == null) SplitPiece(pc, null);
        }
    }

    /// <summary>Tiles on the straight segment a-b (Bresenham).</summary>
    private static IEnumerable<Point> Raster(Point a, Point b)
    {
        int dx = Math.Abs(b.X - a.X), sx = a.X < b.X ? 1 : -1;
        int dy = -Math.Abs(b.Y - a.Y), sy = a.Y < b.Y ? 1 : -1;
        int err = dx + dy;
        int x = a.X, y = a.Y;
        while (true)
        {
            yield return new Point(x, y);
            if (x == b.X && y == b.Y) yield break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    // ---------- crew commands ----------

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

    /// <summary>
    /// F: every non-following planter within reach joins the line behind you.
    /// Nobody new to grab? The crew you're leading is dropped here and waits.
    /// </summary>
    public void ToggleCrew(Vector2 playerPos, bool cutRight)
    {
        bool pickedAny = false;
        foreach (var p in Planters)
        {
            if (p.State == PlanterState.Following || p.State == PlanterState.CuttingIn) continue;
            if (Vector2.Distance(p.Pos, playerPos) < 90f)
            {
                Unassign(p);
                p.State = PlanterState.Following;
                p.Path = null;
                p.PlantSpot = null;
                p.RepathTimer = 0;
                pickedAny = true;
            }
        }
        if (pickedAny) return;

        // nobody new to grab: drop the crew here. They wait for an order —
        // a cut-in from a cache (C) or a piece to work (C inside a piece).
        foreach (var p in Planters)
            if (p.State == PlanterState.Following)
            {
                p.Path = null;
                p.State = PlanterState.Waiting;
            }
    }

    /// <summary>Any planter following the crewboss right now?</summary>
    public bool HasFollowers
    {
        get { foreach (var p in Planters) if (p.State == PlanterState.Following) return true; return false; }
    }

    /// <summary>
    /// C inside a piece with a crew following: they take this piece and plant
    /// in from where you stand along the piece's in-direction — no cut.
    /// Returns false when you're not standing in a piece.
    /// </summary>
    public bool AssignFollowersHere(Vector2 playerPos, bool cutRight)
    {
        var here = TileOf(playerPos);
        var pc = PieceAt(here.X, here.Y);
        if (pc == null) return false;
        // start at the front (the road side) nearest the boss: lines grow in
        // from a boundary, never from the middle of the piece
        Point start = here;
        float best = float.MaxValue;
        foreach (int ti in pc.Front)
        {
            var ft = TileOfIndex(ti);
            float d = Vector2.DistanceSquared(CenterOf(ft), playerPos);
            if (d < best) { best = d; start = ft; }
        }
        foreach (var p in Planters)
            if (p.State == PlanterState.Following)
            {
                p.Path = null;
                p.CutRight = cutRight;
                Assign(p, pc, start, start, pc.InDir, plantInFirst: true);
                if (p.Bag > 0) PickNextSpot(p);
                else GoBagUp(p);
            }
        return true;
    }

    private void Unassign(Planter p)
    {
        p.Piece?.Owners.Remove(p);
        p.Piece = null;
        p.HasFill = false;
        p.CutTiles.Clear();
        p.CutPiece = null;
        p.ResumeCut = false;
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
    /// Aimed line-in from a road cache: the planter bags up, then walks the
    /// bearing planting the cut until it hits a wall. That splits the piece;
    /// they take the side you chose. Aimed along a line that's already in,
    /// they just take that side of it — no new cut.
    /// </summary>
    public void StartLineIn(Planter p, CacheEntity cache, Vector2 dir, bool cutRight)
    {
        Unassign(p);
        if (p.Bag <= 0 && cache.Boxes > 0)
        {
            cache.Boxes--;
            p.Bag = BagSize;
        }
        p.CutRight = cutRight;
        p.Pos = cache.Pos + dir * 14f;
        p.State = PlanterState.CuttingIn;
        p.LineDir = dir;
        p.LineStart = p.Pos;
        p.LineTiles = 0;
        p.LastLineTile = -1;
        p.Path = null;
        p.PlantSpot = null;
    }

    // ---------- update ----------

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
                    UpdateCutting(p, dt);
                    break;

                case PlanterState.MovingToPlant:
                    if (MoveAlong(p, dt))
                    {
                        if (p.PlantSpot.HasValue && Vector2.Distance(p.Pos, p.PlantSpot.Value) > 3)
                            StepToward(p, p.PlantSpot.Value, dt); // path ends at the tile center; step to the exact spot
                        else if (p.ResumeCut)
                        {
                            p.ResumeCut = false;   // back at the end of the cut with a full bag: finish it
                            p.State = PlanterState.CuttingIn;
                        }
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
                            if (p.ResumeCut)
                            {
                                // walk back to where the cut stopped and carry on cutting
                                p.PlantSpot = p.CutResumeAt;
                                p.Path = Pathfinder.FindPath(_map, p.Pos, p.CutResumeAt, PlanterCost);
                                p.PathIdx = 0;
                                p.State = p.Path != null ? PlanterState.MovingToPlant : PlanterState.Idle;
                                p.IdleReason = p.Path != null ? "" : "NO PATH BACK TO CUT";
                            }
                            else
                            {
                                StartPlantIn(p); // never dead-walk: plant in along the next line off the cut
                                PickNextSpot(p);
                            }
                        }
                        else GoBagUp(p); // it drained while we walked
                    }
                    break;

                case PlanterState.Idle:
                    p.RetryTimer -= dt;
                    if (p.RetryTimer <= 0)
                    {
                        p.RetryTimer = 2f;
                        if (p.ResumeCut) GoBagUp(p);
                        else if (p.Piece == null) p.IdleReason = "NO PIECE - CUT ME IN";
                        else if (p.Bag > 0) PickNextSpot(p);
                        else GoBagUp(p);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Walk the aimed bearing. On the road: just walk. On ground: plant the
    /// cut tile by tile (a wall). Hitting a wall or boundary after cutting
    /// ends the cut. Starting on a line that's already in: take its side.
    /// </summary>
    private void UpdateCutting(Planter p, float dt)
    {
        StepToward(p, p.Pos + p.LineDir * 12f, dt);
        var t = TileOf(p.Pos);
        int ti = InBounds(t.X, t.Y) ? Index(t) : -1;
        if (ti < 0) { FinishCut(p); return; }
        if (ti == p.LastLineTile) return;
        p.LastLineTile = ti;

        if (IsGround(t))
        {
            if (_wall[ti])
            {
                // an existing line: lining in along it means "take that side" — no new cut
                FinishCut(p);
                return;
            }
            if (p.CutTiles.Count == 0) p.CutPiece = PieceAt(t.X, t.Y);
            _wall[ti] = true;
            _pieceId[ti] = 0;
            p.CutPiece?.Tiles.Remove(ti);
            p.CutTiles.Add(ti);
            p.LineTiles++;
            if (_planted[ti] < SpotsPerTile && p.Bag > 0)
            {
                RollFault(p, ti, _planted[ti]);
                _planted[ti]++;
                p.Bag--;
                TreesPlanted++;
            }
            if (p.LineTiles >= MaxLineTiles) { FinishCut(p); return; }
            if (p.Bag <= 0)
            {
                // out of trees mid-cut: bag up, come back, finish the line
                p.ResumeCut = true;
                p.CutResumeAt = CenterOf(t);
                GoBagUp(p);
            }
        }
        else if (p.CutTiles.Count > 0 || !IsRoad(t))
        {
            // off the ground after cutting (or wandered off the road before finding ground): the cut's in
            FinishCut(p);
        }
    }

    /// <summary>The cut is complete: split the piece it crossed, take the chosen side.</summary>
    private void FinishCut(Planter p)
    {
        p.ResumeCut = false;
        Point axis = Axis(p.LineDir);
        Point right = new(-axis.Y, axis.X), left = new(axis.Y, -axis.X);
        Point side = p.CutRight ? right : left;

        Point cutStart, cutEnd;
        if (p.CutTiles.Count > 0)
        {
            cutStart = TileOfIndex(p.CutTiles[0]);
            cutEnd = TileOfIndex(p.CutTiles[^1]);
            if (p.CutPiece != null && Pieces.Contains(p.CutPiece)) SplitPiece(p.CutPiece, axis);
        }
        else
        {
            cutStart = cutEnd = TileOf(p.Pos);
        }
        p.CutTiles.Clear();
        p.CutPiece = null;

        var sideEnd = cutEnd + side;
        var sideStart = cutStart + side;
        var pc = PieceAt(sideEnd.X, sideEnd.Y) ?? PieceAt(sideStart.X, sideStart.Y) ?? PieceAt(p.Pos);
        if (pc == null)
        {
            p.State = PlanterState.Idle;
            p.IdleReason = "NO PIECE ON THAT SIDE";
            p.RetryTimer = 2f;
            return;
        }
        Assign(p, pc, cutStart, cutEnd, axis, plantInFirst: false);
        if (p.Bag > 0) PickNextSpot(p);
        else GoBagUp(p);
    }

    /// <summary>
    /// Give a planter a piece and set up how they work it. With a cut: back
    /// line first from the end of the cut. Without: plant in from where they
    /// stand, then back-line and backfill.
    /// </summary>
    private void Assign(Planter p, Piece pc, Point cutStart, Point cutEnd, Point inDir, bool plantInFirst)
    {
        p.Piece?.Owners.Remove(p);
        p.Piece = pc;
        if (!pc.Owners.Contains(p)) pc.Owners.Add(p);

        p.InDir = inDir;
        Point right = new(-p.InDir.Y, p.InDir.X); // right of "in" (screen y is down)
        Point left = new(p.InDir.Y, -p.InDir.X);
        p.SideDir = p.CutRight ? right : left;
        p.CutStart = cutStart;
        p.BagUps = 0;
        p.HasFill = true;
        p.StepDir = new Point(-p.InDir.X, -p.InDir.Y); // every step-over is one line toward the front

        if (plantInFirst)
        {
            p.PlantingIn = true;
            p.FillDir = p.InDir;
            p.LineTile = cutStart;
        }
        else
        {
            p.PlantingIn = false;
            p.FillDir = p.SideDir;
            p.LineTile = cutEnd + p.SideDir; // back line: along the wall, beside the cut's end
        }
        p.IdleReason = "";
    }

    /// <summary>Bag's full again: plant in along the next line off the cut, from the front.</summary>
    private void StartPlantIn(Planter p)
    {
        if (!p.HasFill || p.Piece == null) return;
        p.BagUps++;
        var start = new Point(p.CutStart.X + p.SideDir.X * p.BagUps, p.CutStart.Y + p.SideDir.Y * p.BagUps);
        if (Open(p, start))
        {
            p.PlantingIn = true;
            p.FillDir = p.InDir;
            p.LineTile = start;
            return;
        }
        // that line's in already (or off the piece): plant in from the nearest open front tile
        int bestTi = -1; float best = float.MaxValue;
        foreach (int ti in p.Piece.Front)
        {
            if (_planted[ti] >= SpotsPerTile) continue;
            float d = Vector2.DistanceSquared(CenterOf(TileOfIndex(ti)), p.Pos);
            if (d < best) { best = d; bestTi = ti; }
        }
        if (bestTi >= 0)
        {
            p.PlantingIn = true;
            p.FillDir = p.InDir;
            p.LineTile = TileOfIndex(bestTi);
        }
        else
        {
            p.PlantingIn = false;
            p.FillDir = p.SideDir;
        }
    }

    // ---------- the fill pattern ----------

    private bool Open(Planter p, Point t)
    {
        if (p.Piece == null || !InBounds(t.X, t.Y)) return false;
        int ti = Index(t);
        return p.Piece.Tiles.Contains(ti) && !_wall[ti] && _planted[ti] < SpotsPerTile && Supported(t);
    }

    /// <summary>
    /// The rule: a tree goes in beside another tree or against a boundary —
    /// a planted neighbor (8-way) or a wall/edge next door (4-way). No ghost
    /// lines floating in the middle of a piece; the fill stays contiguous.
    /// </summary>
    private bool Supported(Point t)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = new Point(t.X + dx, t.Y + dy);
                bool straight = dx == 0 || dy == 0;
                if (!InBounds(n.X, n.Y)) { if (straight) return true; continue; }
                int ni = Index(n);
                if (_planted[ni] > 0) return true;
                if (straight && (!IsGround(n) || _wall[ni])) return true;
            }
        return false;
    }

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
            p.PlantingIn = false;      // reached the back: backfill from here, away from the cut
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

    /// <summary>Anything left in the piece? Restart the pattern at the nearest open tile.</summary>
    private bool NearestOpenTile(Planter p, out Point tile)
    {
        int bestTi = -1; float best = float.MaxValue;
        if (p.Piece != null)
            foreach (int ti in p.Piece.Tiles)
            {
                if (_wall[ti] || _planted[ti] >= SpotsPerTile) continue;
                float d = Vector2.DistanceSquared(CenterOf(TileOfIndex(ti)), p.Pos);
                if (d < best) { best = d; bestTi = ti; }
            }
        tile = bestTi >= 0 ? TileOfIndex(bestTi) : default;
        return bestTi >= 0;
    }

    private void PickNextSpot(Planter p)
    {
        if (p.Piece == null)
        {
            p.State = PlanterState.Idle;
            p.IdleReason = "NO PIECE - CUT ME IN";
            p.RetryTimer = 2f;
            return;
        }
        if (!p.HasFill) Assign(p, p.Piece, TileOf(p.Pos), TileOf(p.Pos), p.Piece.InDir, plantInFirst: true);

        if (!NextLineTile(p, out Point tile))
        {
            if (!NearestOpenTile(p, out tile))
            {
                p.State = PlanterState.Done; // piece planted out — come move me, boss
                return;
            }
            p.PlantingIn = false;
            p.FillDir = p.SideDir;
        }
        p.LineTile = tile;

        int spot = _planted[Index(tile)];
        p.PlantSpot = SpotPos(tile.X, tile.Y, spot, _map.TileSize, _map.TileHeight);
        p.Path = Pathfinder.FindPath(_map, p.Pos, CenterOf(tile), PlanterCost);
        p.PathIdx = 0;
        p.State = p.Path != null ? PlanterState.MovingToPlant : PlanterState.Idle;
        p.IdleReason = p.Path != null ? "" : "NO PATH TO SPOT";
    }

    private void CommitTree(Planter p)
    {
        if (!p.PlantSpot.HasValue) return;
        var t = TileOf(p.PlantSpot.Value);
        if (!InBounds(t.X, t.Y)) return;
        int ti = Index(t);
        if (_planted[ti] >= SpotsPerTile) return;
        RollFault(p, ti, _planted[ti]);
        _planted[ti]++;
        TreesPlanted++;
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

    /// <summary>Deterministic planting position in a tile: the center, jittered so lines look hand-planted.</summary>
    public static Vector2 SpotPos(int tx, int ty, int spot, int ts, int th)
    {
        uint h = (uint)(tx * 7349 + ty * 9241 + spot * 131);
        h = (h ^ (h >> 13)) * 1274126177u;
        float jx = (h & 0xFF) / 255f * 5f - 2.5f;
        float jy = ((h >> 8) & 0xFF) / 255f * 3f - 1.5f;
        return new Vector2(tx * ts + ts / 2f + jx, ty * th + th / 2f + jy);
    }

    // ---------- movement ----------

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
            p.IdleReason = "NO TREES IN ANY CACHE";
            p.RetryTimer = 2f;
            return;
        }
        p.Path = Pathfinder.FindPath(_map, p.Pos, cache.Pos, PlanterCost);
        p.PathIdx = 0;
        p.State = p.Path != null ? PlanterState.MovingToCache : PlanterState.Idle;
        p.IdleReason = p.Path != null ? "" : "NO PATH TO CACHE";
        p.RetryTimer = 2f;
    }

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
