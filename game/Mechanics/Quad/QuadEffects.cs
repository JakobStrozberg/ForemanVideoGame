using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Crewboss.Mechanics.Quad;

/// <summary>
/// What the quad leaves behind: a tan dust plume at speed, grey gravel-smoke
/// on roads, and twin tire tracks. Spawned by distance travelled, not time,
/// so they read as one continuous trail at any frame rate.
/// </summary>
public sealed class QuadEffects
{
    private struct Dust { public Vector2 Pos, Vel; public float Age, Life, Size; public bool Smoke; }
    private struct TrackStamp { public Vector2 Pos; public float Ang; }

    private readonly List<Dust> _dust = new();
    private readonly List<TrackStamp> _tracks = new();
    private const int MaxTracks = 4000;

    private Texture2D _dustTexture, _trackTexture;
    private Vector2 _prevPos;
    private bool _havePrev;
    private float _dustSpawnDist, _smokeSpawnDist, _trackDist;
    private float _hashClock; // advances with travel; seeds the puff hash

    public void Load(GraphicsDevice gd)
    {
        // dust puff: soft radial tan cloud
        const int duSize = 24;
        _dustTexture = new Texture2D(gd, duSize, duSize);
        var duData = new Color[duSize * duSize];
        for (int y = 0; y < duSize; y++)
            for (int x = 0; x < duSize; x++)
            {
                float nx = (x - duSize / 2f) / (duSize / 2f);
                float ny = (y - duSize / 2f) / (duSize / 2f);
                float r2 = nx * nx + ny * ny;
                float a = r2 < 1f ? (1f - r2) * (1f - r2) : 0f;
                duData[y * duSize + x] = new Color(202, 182, 148) * a;
            }
        _dustTexture.SetData(duData);

        // tire-track stamp: two parallel tread bars, travel along local X.
        // 15x15 world px; successive stamps overlap into continuous twin lines.
        const int trW = 15, trH = 15;
        _trackTexture = new Texture2D(gd, trW, trH);
        var trData = new Color[trW * trH];
        var tread = new Color(46, 34, 22);
        for (int x = 0; x < trW; x++)
            for (int y = 0; y < trH; y++)
            {
                bool bar = y is >= 2 and <= 4 or >= 10 and <= 12;
                if (!bar) continue;
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                float a = (h & 0xFF) / 255f < 0.8f ? 1f : 0.4f;
                trData[y * trW + x] = tread * a;
            }
        _trackTexture.SetData(trData);
    }

    /// <summary>Spawn from the quad's travel this frame; call after the quad moved.</summary>
    public void Update(QuadController quad, WorldMap map, float dt)
    {
        Age(dt);
        if (!_havePrev) { _prevPos = quad.Pos; _havePrev = true; }

        float speed = quad.Speed;
        float travelled = Vector2.Distance(_prevPos, quad.Pos);
        _hashClock += dt * (6f + speed / 4.7f);

        // dust plume: only at real speed, spawned densely along the traveled
        // segment so it reads as one continuous cloud, not scattered puffs
        if (speed > Tweaks.DustMinSpeed)
        {
            float ramp = MathF.Min(1f, (speed - Tweaks.DustMinSpeed) / 43f);
            float spacing = MathHelper.Lerp(13f, 7f, ramp);
            _dustSpawnDist += travelled;
            int guard = 0;
            while (_dustSpawnDist > spacing && _dust.Count < 400 && guard++ < 16)
            {
                _dustSpawnDist -= spacing;
                float k = travelled > 0.01f ? _dustSpawnDist / travelled : 0f;
                Vector2 basePos = Vector2.Lerp(quad.Pos, _prevPos, k) - quad.Heading * 19f;
                uint h = Hash(977f, _dust.Count * 131 + guard * 37);
                float jx = ((h & 0xFF) / 255f - 0.5f) * 8f;
                float jy = (((h >> 8) & 0xFF) / 255f - 0.5f) * 6f;
                _dust.Add(new Dust
                {
                    Pos = basePos + new Vector2(jx, jy),
                    Vel = -quad.Heading * 9f + new Vector2(jx * 0.8f, -5f),
                    Life = 1.0f + ((h >> 16) & 0xFF) / 255f * 0.5f,
                    Size = 13f + ((h >> 20) & 0xFF) / 255f * 6f
                });
            }
        }
        else _dustSpawnDist = 0f;

        // road smoke: any movement on road/trail kicks up a gravel-exhaust
        // trail — no speed gate beyond a crawl, so puttering feels alive
        string terr = map.TerrainName(quad.Pos);
        if (speed > 8f && (terr == "road" || terr == "trail"))
        {
            _smokeSpawnDist += travelled;
            int guard = 0;
            while (_smokeSpawnDist > 15f && _dust.Count < 400 && guard++ < 8)
            {
                _smokeSpawnDist -= 15f;
                uint h = Hash(811f, _dust.Count * 97 + guard * 53);
                float jx = ((h & 0xFF) / 255f - 0.5f) * 6f;
                float jy = (((h >> 8) & 0xFF) / 255f - 0.5f) * 4f;
                _dust.Add(new Dust
                {
                    Pos = quad.Pos - quad.Heading * 17f + new Vector2(jx, jy),
                    Vel = -quad.Heading * 6f + new Vector2(jx * 0.6f, -11f), // drifts up
                    Life = 0.8f + ((h >> 16) & 0xFF) / 255f * 0.4f,
                    Size = 9f + ((h >> 20) & 0xFF) / 255f * 5f,
                    Smoke = true
                });
            }
        }
        else _smokeSpawnDist = 0f;

        // tire tracks: stamp a twin-tread mark every 13px of travel
        if (speed > 13f)
        {
            _trackDist += travelled;
            if (_trackDist > 13f)
            {
                _trackDist = 0f;
                _tracks.Add(new TrackStamp { Pos = quad.Pos, Ang = MathF.Atan2(quad.Velocity.Y, quad.Velocity.X) });
                if (_tracks.Count > MaxTracks) _tracks.RemoveAt(0);
            }
        }
        _prevPos = quad.Pos;
    }

    private uint Hash(float clockMul, int salt)
    {
        uint h = (uint)(_hashClock * clockMul + salt);
        return (h ^ (h >> 13)) * 1274126177u;
    }

    private void Age(float dt)
    {
        for (int i = _dust.Count - 1; i >= 0; i--)
        {
            var d = _dust[i];
            d.Age += dt;
            d.Pos += d.Vel * dt;
            if (d.Age >= d.Life) _dust.RemoveAt(i);
            else _dust[i] = d;
        }
    }

    /// <summary>Faint wheel trails worn into the ground — draw under sprites.</summary>
    public void DrawTrails(SpriteBatch sb, Camera cam, WorldMap map)
    {
        if (_trackTexture == null || _tracks.Count == 0) return;
        float zoom = cam.Zoom;
        float viewW = cam.ViewWidth / zoom, viewH = cam.ViewHeight / zoom;
        var origin = new Vector2(_trackTexture.Width / 2f, _trackTexture.Height / 2f);
        for (int i = 0; i < _tracks.Count; i++)
        {
            var t = _tracks[i];
            if (t.Pos.X < cam.Position.X - 20 || t.Pos.X > cam.Position.X + viewW + 20 ||
                t.Pos.Y < cam.Position.Y - 20 || t.Pos.Y > cam.Position.Y + viewH + 20) continue;
            Vector2 s = cam.WorldToScreen(t.Pos, map.Lift(t.Pos));
            float fade = Math.Min(1f, (float)i / 400f + 0.25f); // oldest fade toward eviction
            sb.Draw(_trackTexture, s, null, Color.White * (0.22f * fade), t.Ang, origin, zoom, SpriteEffects.None, 0f);
        }
    }

    /// <summary>Dust and smoke puffs — draw over everything at ground level.</summary>
    public void DrawDust(SpriteBatch sb, Camera cam, WorldMap map)
    {
        if (_dustTexture == null) return;
        float zoom = cam.Zoom;
        foreach (var d in _dust)
        {
            float t = d.Age / d.Life;
            Vector2 s = cam.WorldToScreen(d.Pos, map.Lift(d.Pos));
            // road smoke: grey, rises, grows less; dust: tan cloud, billows out
            float grow = d.Smoke ? 1.0f : 1.7f;
            Color tint = d.Smoke ? new Color(118, 118, 124) : Color.White;
            float alpha = (1f - t) * (d.Smoke ? 0.5f : 0.42f);
            float scale = d.Size * (1f + t * grow) * zoom / 24f;
            sb.Draw(_dustTexture, s, null, tint * alpha, 0f, new Vector2(12, 12), scale, SpriteEffects.None, 0f);
        }
    }
}
