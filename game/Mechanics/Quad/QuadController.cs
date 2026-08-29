using Microsoft.Xna.Framework;
using System;

namespace Crewboss.Mechanics.Quad;

/// <summary>
/// The quad: gears, throttle/brake/steer, handbrake drift, terrain speed,
/// tile + debris collision, slope drag, suspension, and terrain tilt.
/// Owns its own position — when the crewboss dismounts, this IS the parked
/// quad. Reads intents from GameInput, never raw keys.
/// </summary>
public sealed class QuadController
{
    public Vector2 Pos;
    public Vector2 Heading = new(0, -1);
    public Vector2 Velocity;
    /// <summary>32-direction index into the generated atlas.</summary>
    public int DirIdx;
    public float Speed => Velocity.Length();

    // gearbox: index 0 = Reverse, 1..5 forward. Shifts take Tweaks.ShiftTime
    // with the clutch in (no throttle) until the new gear engages.
    public int Gear { get; private set; } = 1;
    public int PendingGear { get; private set; } = -1;
    public bool Shifting => PendingGear >= 0;
    private float _shiftTimer;
    /// <summary>0..1 through the current shift (0 = just pulled the clutch), 0 when not shifting.</summary>
    public float ShiftProgress => Shifting ? 1f - _shiftTimer / MathF.Max(0.01f, Tweaks.ShiftTime) : 0f;

    /// <summary>Boxes of seedlings on the racks.</summary>
    public int Boxes;
    public const int BoxCap = 8; // 6 on the rear rack (3x2) + 2 up front

    /// <summary>True while sliding sideways (brake-steer or handbrake) above a crawl.</summary>
    public bool Drifting { get; private set; }

    /// <summary>False while the starter is cranking (and while parked): no throttle, no shifting.</summary>
    public bool EngineOn = true;
    /// <summary>Whether the throttle was open this frame (for the engine note).</summary>
    public bool Throttle { get; private set; }

    // engine speed model: revs climb through each gear from idle to redline
    // as road speed climbs to that gear's top, then drop back on the upshift
    public const float IdleRpm = 1700f, RedlineRpm = 4600f;
    /// <summary>Smoothed engine RPM, driven by speed within the current gear.</summary>
    public float Rpm { get; private set; } = IdleRpm;

    // suspension: the chassis chases ground height on a spring — crest a rise
    // at speed and the quad floats (shadow shows the air), dips compress
    public float ChassisLift { get; private set; }
    private float _chassisVel;
    /// <summary>Smoothed sprite lean from ground slope (radians).</summary>
    public float Tilt { get; private set; }

    public void Reset(Vector2 spawn)
    {
        Pos = spawn;
        Velocity = Vector2.Zero;
        Heading = new Vector2(0, -1);
        DirIdx = 0;
        Gear = 1;
        PendingGear = -1;
        _shiftTimer = 0f;
        ChassisLift = 0f;
        _chassisVel = 0f;
        Tilt = 0f;
        Rpm = IdleRpm;
    }

    /// <summary>Height of the chassis above the ground under it (suspension/airtime).</summary>
    public float AirHeight(WorldMap map)
    {
        float surface = map.Lift(Pos) + MathF.Max(0f, WorldMap.BumpAt(Pos.X, Pos.Y) * map.RoughAt(Pos));
        return MathF.Max(0f, ChassisLift - surface);
    }

    public void Update(GameInput input, WorldMap map, float dt)
    {
        UpdateGearbox(input, dt);

        // torque vs terrain: high-torque gears power through rough ground,
        // low-torque gears lose their acceleration in it
        float terrain = map.SpeedAt(Pos);
        float torque = Tweaks.GearTorque[Gear];
        float accelRate = Tweaks.GearAccel[Gear] * MathHelper.Lerp(terrain, 1f, torque * 0.8f);
        float maxSpeed = Tweaks.GearMax[Gear];

        bool throttle = input.Throttle && EngineOn;
        Throttle = throttle;
        bool braking = input.Brake;
        bool drift = input.Drift;
        float steer = input.Steer;

        float speed0 = Speed;
        Vector2 heading = Heading == Vector2.Zero ? new Vector2(0, -1) : Heading;
        bool reverse = Gear == 0;

        // tuned constants are per-frame factors at 60Hz; the game runs at the
        // display's refresh, so raise them to dt*60 for identical handling
        float PerFrame(float f) => MathF.Pow(f, dt * 60f);

        Drifting = (braking || drift) && steer != 0f && speed0 > 40f;

        if (braking)
        {
            // brake hard; steering while braking = drift: the nose whips
            // around while momentum comes along slowly
            Velocity *= PerFrame(0.94f);
            if (steer != 0f)
            {
                heading = Rotate(heading, steer * 4.5f * dt);
                if (speed0 > 26f)
                    Velocity = TurnToward(Velocity / MathF.Max(speed0, 0.01f), heading, 2.6f * dt) * Velocity.Length();
            }
        }
        else if (drift)
        {
            // handbrake drift: whippy nose, barely any grip — the quad slides
            // on its old momentum while the wheels point somewhere new
            if (steer != 0f)
                heading = Rotate(heading, steer * 5.2f * dt);

            // power slide: throttle still pushes along the nose, at partial bite
            if (throttle && !Shifting)
                Velocity += (reverse ? -heading : heading) * accelRate * 0.55f * dt;

            float spD = Velocity.Length();
            if (spD > 1f)
            {
                Vector2 travel = reverse ? -heading : heading;
                Velocity = Vector2.Lerp(Velocity, travel * spD, 1f - PerFrame(0.965f));
            }

            Velocity *= PerFrame(0.9875f); // tires scrubbing sideways bleed speed

            float spDm = Velocity.Length();
            if (spDm > maxSpeed)
                Velocity *= MathF.Max(maxSpeed / spDm, PerFrame(0.94f));
        }
        else
        {
            // steer: snappy hands, easing off some at speed
            if (steer != 0f)
            {
                float turnRate = MathHelper.Lerp(4.3f, 2.0f, MathF.Min(1f, speed0 / 214f));
                heading = Rotate(heading, steer * turnRate * dt);
            }

            // throttle (clutch is in while shifting); launch punch — extra
            // shove off the line, tapering out by half the gear's top
            if (throttle && !Shifting)
            {
                float launch = 1f + 0.7f * (1f - MathF.Min(1f, speed0 / (maxSpeed * 0.5f)));
                Velocity += (reverse ? -heading : heading) * accelRate * launch * dt;
            }

            // grip: momentum lines up behind the nose — firm at low speed,
            // loosening as you go fast so hard corners get a playful slide
            float sp2 = Velocity.Length();
            if (sp2 > 1f)
            {
                float gripBase = MathHelper.Lerp(0.84f, 0.91f, MathF.Min(1f, speed0 / 254f));
                Vector2 travel = reverse ? -heading : heading;
                Velocity = Vector2.Lerp(Velocity, travel * sp2, 1f - PerFrame(gripBase));
            }

            // drag: gentle coast off the gas; under throttle, rough ground
            // bogs down low-torque gears (G4/G5 in slash go nowhere)
            float drag = !throttle ? 0.995f : 0.995f - (1f - terrain) * (1f - torque) * 0.045f;
            Velocity *= PerFrame(drag);

            // over the gear's top speed: firm engine braking
            float sp = Velocity.Length();
            if (sp > maxSpeed)
                Velocity *= MathF.Max(maxSpeed / sp, PerFrame(0.94f));
        }

        Heading = heading;

        // move, scaled by terrain, with axis-separated collision against
        // impassable tiles (forest, trucks) and debris circles
        Vector2 delta = Velocity * dt * map.SpeedAt(Pos);
        Vector2 tryX = Pos + new Vector2(delta.X, 0);
        if (map.IsPassable(tryX) && !map.HitsDebris(tryX)) Pos.X = tryX.X; else Velocity.X = 0;
        Vector2 tryY = Pos + new Vector2(0, delta.Y);
        if (map.IsPassable(tryY) && !map.HitsDebris(tryY)) Pos.Y = tryY.Y; else Velocity.Y = 0;
        Pos.X = MathHelper.Clamp(Pos.X, 0, map.Bounds.Width);
        Pos.Y = MathHelper.Clamp(Pos.Y, 0, map.Bounds.Height);

        // the sprite always faces the nose — steering, drifting and reversing all read from it
        if (Velocity.LengthSquared() > 1f || steer != 0f)
            DirIdx = DirIndexOf(Heading);

        UpdateTerrainFollow(map, dt);
        UpdateRpm(throttle, dt);
    }

    private void UpdateRpm(bool throttle, float dt)
    {
        float target;
        if (!EngineOn || Shifting)
            target = IdleRpm; // starter cranking, or clutch in between gears
        else
        {
            float frac = MathHelper.Clamp(Speed / MathF.Max(1f, Tweaks.GearMax[Gear]), 0f, 1f);
            target = MathHelper.Lerp(IdleRpm, RedlineRpm, frac);
            if (throttle) target += 150f; // a little extra note under load
            if (Drifting) target += 250f; // wheels spinning up in the slide
        }
        // revs rise faster than they fall; the clutch cut on a shift is abrupt
        float rate = Shifting ? 9f : target > Rpm ? 5f : 3.5f;
        Rpm += (target - Rpm) * (1f - MathF.Exp(-rate * dt));
    }

    private void UpdateGearbox(GameInput input, float dt)
    {
        if (!EngineOn) return;
        if (PendingGear >= 0)
        {
            _shiftTimer -= dt;
            if (_shiftTimer <= 0) { Gear = PendingGear; PendingGear = -1; }
        }
        else if (input.GearUp)
        {
            if (Gear < 5) { PendingGear = Gear + 1; _shiftTimer = Tweaks.ShiftTime; }
        }
        else if (input.GearDown)
        {
            if (Gear > 0) { PendingGear = Gear - 1; _shiftTimer = Tweaks.ShiftTime; } // below 1st sits Reverse
        }
    }

    /// <summary>Slope drag, suspension spring over relief + micro-bumps, and terrain tilt.</summary>
    private void UpdateTerrainFollow(WorldMap map, float dt)
    {
        float speed = Speed;
        float groundLift = map.Lift(Pos);
        if (speed > 1f)
        {
            Vector2 vn = Velocity / speed;
            float ahead = map.Lift(Pos.X + vn.X * 26f, Pos.Y + vn.Y * 26f);
            float slope = (ahead - groundLift) / 26f; // positive = climbing
            Velocity *= 1f - Math.Clamp(slope, -0.15f, 0.30f) * 4f * dt;
        }

        float rough = map.RoughAt(Pos);
        float bump = WorldMap.BumpAt(Pos.X, Pos.Y) * rough;

        // lively spring (soft damping): crests and bumps overshoot a touch,
        // so the chassis visibly works the terrain
        float springTarget = groundLift + MathF.Max(0f, bump);
        _chassisVel += ((springTarget - ChassisLift) * 75f - _chassisVel * 5.5f) * dt;
        ChassisLift += _chassisVel * dt;

        // tilt: smoothed so it reads as suspension articulation, not snapping
        float tiltTarget = map.TiltAt(Pos);
        Tilt += (tiltTarget - Tilt) * (1f - MathF.Exp(-9f * dt));
    }

    /// <summary>32-direction atlas index for a heading (0 = north, clockwise).</summary>
    public static int DirIndexOf(Vector2 heading)
    {
        float degrees = MathHelper.ToDegrees(MathF.Atan2(heading.Y, heading.X));
        degrees = (degrees + 90) % 360;
        if (degrees < 0) degrees += 360;
        return (int)Math.Round(degrees / 11.25) % 32;
    }

    /// <summary>Rotate a unit vector toward a target direction, clamped to maxRadians.</summary>
    private static Vector2 TurnToward(Vector2 cur, Vector2 target, float maxRadians)
    {
        float a = MathF.Atan2(cur.Y, cur.X);
        float d = MathHelper.WrapAngle(MathF.Atan2(target.Y, target.X) - a);
        a += MathHelper.Clamp(d, -maxRadians, maxRadians);
        return new Vector2(MathF.Cos(a), MathF.Sin(a));
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float a = MathF.Atan2(v.Y, v.X) + radians;
        return new Vector2(MathF.Cos(a), MathF.Sin(a));
    }
}
