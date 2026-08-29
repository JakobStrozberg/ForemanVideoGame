using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Crewboss.Mechanics.Player;

/// <summary>What Q would do right now. Priority: truck, cache, parked quad, open ground.</summary>
public enum BoxAction { None, LoadFromTruck, AddToCache, TakeFromAtv, LoadAtvFromHands, PlaceCache }

/// <summary>
/// The crewboss: mounted on the quad or on foot. Mount/dismount, walking
/// (bush and swamp are passable on foot, just slow), boxes and caches,
/// line-in aiming, coaching, and walk-the-line quality reveals.
/// </summary>
public sealed class PlayerController
{
    public const float InteractRange = 55f;

    private readonly QuadController _quad;
    private readonly WorldMap _map;
    private readonly List<CacheEntity> _caches;
    public PlanterSystem Planters { get; set; }

    public bool Mounted { get; private set; } = true;
    public Vector2 FootPos;
    public string FootDir { get; private set; } = "S";
    public float WalkAnim { get; private set; }
    public bool Walking { get; private set; }
    public bool CarryingBox { get; private set; }
    /// <summary>Cut-in side for the next line-in or release: in-and-right (true) or in-and-left.</summary>
    public bool CutRight { get; private set; } = true;

    /// <summary>Where the crewboss is: on the quad, or on foot.</summary>
    public Vector2 Pos => Mounted ? _quad.Pos : FootPos;

    // line-in aiming: C at a cache -> arrow from the cache, A/D rotates,
    // C confirms, Q cancels. The chosen planter marches the bearing solo.
    public bool Aiming { get; private set; }
    public float AimAngle { get; private set; }
    public CacheEntity AimCache { get; private set; }
    private Planter _aimPlanter;

    /// <summary>Walk-the-line quality reveals: (tileX, tileY, spot) -> seconds left to show.</summary>
    public readonly Dictionary<(int, int, int), float> Reveals = new();

    public PlayerController(QuadController quad, WorldMap map, List<CacheEntity> caches)
    {
        _quad = quad;
        _map = map;
        _caches = caches;
    }

    public void Reset()
    {
        Mounted = true;
        CarryingBox = false;
        Aiming = false;
        Reveals.Clear();
    }

    public Vector2 Facing => Mounted
        ? (_quad.Heading == Vector2.Zero ? new Vector2(0, 1) : _quad.Heading)
        : FootDir switch
        {
            "N" => new Vector2(0, -1),
            "S" => new Vector2(0, 1),
            "E" => new Vector2(1, 0),
            _ => new Vector2(-1, 0)
        };

    // ---------- aiming ----------

    /// <summary>Rotation + confirm/cancel replace normal input while aiming.</summary>
    public void UpdateAiming(GameInput input, float dt)
    {
        float rot = 2.4f * dt;
        if (input.ToggleCutSide) CutRight = !CutRight;
        if (input.AimLeft) AimAngle -= rot;
        if (input.AimRight) AimAngle += rot;

        if (input.AimConfirm)
        {
            var dir = new Vector2(MathF.Cos(AimAngle), MathF.Sin(AimAngle));
            Planters.StartLineIn(_aimPlanter, AimCache, dir, CutRight);
            Aiming = false;
        }
        else if (input.AimCancel)
        {
            Aiming = false;
        }
    }

    /// <summary>C at a cache: enter aim mode if there's a cache and a planter to line in.</summary>
    private void TryStartAiming()
    {
        if (Planters == null) return;
        CacheEntity cache = null;
        float bestD = 70f;
        foreach (var c in _caches)
        {
            float d = Vector2.Distance(Pos, c.Pos);
            if (d < bestD) { bestD = d; cache = c; }
        }
        if (cache == null) return;

        var planter = Planters.FindLinePlanter(cache.Pos);
        if (planter == null) return;
        if (cache.Boxes <= 0 && planter.Bag <= 0) return; // no trees = no line

        Aiming = true;
        AimCache = cache;
        _aimPlanter = planter;
        var f = Facing;
        AimAngle = MathF.Atan2(f.Y, f.X);
    }

    // ---------- context actions ----------

    /// <summary>E = mount/dismount, Q = box action, F = crew, C = line-in, T = coach.</summary>
    public void HandleActions(GameInput input)
    {
        if (input.ToggleCutSide) CutRight = !CutRight;
        if (input.Mount) ToggleMount();
        if (input.BoxAction) DoBoxAction();
        if (input.Crew) Planters?.ToggleCrew(Pos, CutRight);
        if (input.LineIn) TryStartAiming();
        if (input.Coach && !Mounted && Planters != null)
        {
            var target = Planters.FindCoachTarget(Pos);
            if (target != null) Planters.Coach(target);
        }
    }

    private void ToggleMount()
    {
        if (Mounted)
        {
            _quad.Velocity = Vector2.Zero;   // the quad stays parked right here
            FootPos = _quad.Pos + new Vector2(0, 14); // step off beside it
            Mounted = false;
        }
        else if (Vector2.Distance(FootPos, _quad.Pos) < InteractRange)
        {
            Mounted = true;
        }
    }

    // ---------- walking ----------

    public void UpdateFootMovement(GameInput input, float dt)
    {
        Vector2 dir = input.WalkDir;
        Walking = dir != Vector2.Zero;
        if (!Walking) return;

        dir.Normalize();
        FootDir = MathF.Abs(dir.X) >= MathF.Abs(dir.Y)
            ? (dir.X >= 0 ? "E" : "W")
            : (dir.Y >= 0 ? "S" : "N");
        WalkAnim += dt;

        Vector2 delta = dir * Tweaks.FootSpeed * FootSpeedMult(FootPos) * dt;
        Vector2 tryX = FootPos + new Vector2(delta.X, 0);
        if (FootSpeedMult(tryX) > 0) FootPos.X = tryX.X;
        Vector2 tryY = FootPos + new Vector2(0, delta.Y);
        if (FootSpeedMult(tryY) > 0) FootPos.Y = tryY.Y;

        FootPos.X = MathHelper.Clamp(FootPos.X, 0, _map.Bounds.Width);
        FootPos.Y = MathHelper.Clamp(FootPos.Y, 0, _map.Bounds.Height);
    }

    /// <summary>On foot the bush and swamp are passable, just slow. Trucks still block.</summary>
    private float FootSpeedMult(Vector2 pos)
    {
        if (_map.Tiles == null) return 1f;
        var t = _map.Tiles.TerrainAtWorld(pos);
        return t.Name switch
        {
            "forest" => 0.35f,
            "swamp" => 0.5f,
            "obstacle" => 0f,
            "outside" => 0f,
            _ => MathF.Max(t.Speed, 0.5f)
        };
    }

    // ---------- quality reveals ----------

    /// <summary>On foot, planted spots near the player reveal their quality for a moment.</summary>
    public void UpdateReveals(float dt)
    {
        DecayReveals(dt);
        if (Mounted || Planters == null || _map.Tiles == null) return;

        int ts = _map.Tiles.TileSize, th = _map.Tiles.TileHeight;
        int ptx = (int)(FootPos.X / ts), pty = (int)(FootPos.Y / th);
        for (int ty = pty - 2; ty <= pty + 2; ty++)
            for (int tx = ptx - 2; tx <= ptx + 2; tx++)
            {
                int count = Planters.PlantedAtTile(tx, ty);
                for (int s = 0; s < count; s++)
                {
                    Vector2 pos = PlanterSystem.SpotPos(tx, ty, s, ts, th);
                    if (Vector2.DistanceSquared(pos, FootPos) < 48f * 48f)
                        Reveals[(tx, ty, s)] = 2.2f;
                }
            }
    }

    private void DecayReveals(float dt)
    {
        if (Reveals.Count == 0) return;
        var expired = new List<(int, int, int)>();
        foreach (var key in Reveals.Keys)
        {
            float t = Reveals[key] - dt;
            if (t <= 0) expired.Add(key);
            else Reveals[key] = t;
        }
        foreach (var k in expired) Reveals.Remove(k);
    }

    // ---------- boxes + caches ----------

    public (BoxAction action, Vector2 target) GetBoxAction()
    {
        Vector2 pos = Pos;

        foreach (var t in _map.TruckCenters)
            if (Vector2.Distance(pos, t) < InteractRange + 20)
            {
                if (Mounted && _quad.Boxes < QuadController.BoxCap) return (BoxAction.LoadFromTruck, t);
                if (!Mounted && !CarryingBox) return (BoxAction.LoadFromTruck, t);
                return (BoxAction.None, default);
            }

        foreach (var c in _caches)
            if (Vector2.Distance(pos, c.Pos) < InteractRange)
            {
                if (Mounted && _quad.Boxes > 0) return (BoxAction.AddToCache, c.Pos);
                if (!Mounted && CarryingBox) return (BoxAction.AddToCache, c.Pos);
                return (BoxAction.None, default);
            }

        if (!Mounted && Vector2.Distance(pos, _quad.Pos) < InteractRange)
        {
            if (CarryingBox && _quad.Boxes < QuadController.BoxCap) return (BoxAction.LoadAtvFromHands, _quad.Pos);
            if (!CarryingBox && _quad.Boxes > 0) return (BoxAction.TakeFromAtv, _quad.Pos);
        }

        if ((Mounted && _quad.Boxes > 0) || (!Mounted && CarryingBox))
            return (BoxAction.PlaceCache, pos + Facing * 40);

        return (BoxAction.None, default);
    }

    private void DoBoxAction()
    {
        var (action, target) = GetBoxAction();
        switch (action)
        {
            case BoxAction.LoadFromTruck:
                if (Mounted) _quad.Boxes++;
                else CarryingBox = true;
                break;
            case BoxAction.AddToCache:
                var cache = _caches.Find(c => c.Pos == target);
                if (cache == null) break;
                if (Mounted) { _quad.Boxes--; cache.Boxes++; }
                else { CarryingBox = false; cache.Boxes++; }
                break;
            case BoxAction.TakeFromAtv:
                _quad.Boxes--;
                CarryingBox = true;
                break;
            case BoxAction.LoadAtvFromHands:
                CarryingBox = false;
                _quad.Boxes++;
                break;
            case BoxAction.PlaceCache:
                if (!_map.IsPassable(target)) break; // no caches in trees/trucks
                if (Mounted) _quad.Boxes--;
                else CarryingBox = false;
                _caches.Add(new CacheEntity { Pos = target, Boxes = 1 });
                break;
        }
    }
}
