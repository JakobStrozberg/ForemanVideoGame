using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using System;
using System.IO;

namespace Crewboss.Mechanics.Quad;

/// <summary>
/// Engine sound for the quad. Three loops recorded at known engine speeds
/// (idle ~1730 rpm, low cruise ~2900, working ~3890) are each pitch-shifted
/// to the quad's live RPM and crossfaded so whichever was recorded nearest
/// the target carries the note. Plus a start one-shot (the quad can't move
/// until the engine catches) and a shut-off. Silent if the wavs are missing.
/// </summary>
public sealed class QuadAudio : IDisposable
{
    private sealed class Layer
    {
        public SoundEffect Fx; public SoundEffectInstance Inst; public float NativeRpm; public float Vol;
    }

    private SoundEffect _start, _off, _shift;
    private bool _wasShifting;
    private readonly Layer _idle = new() { NativeRpm = 1730f };
    private readonly Layer _lo = new() { NativeRpm = 2900f };
    private readonly Layer _hi = new() { NativeRpm = 3890f };
    private Layer[] Layers => new[] { _idle, _lo, _hi };

    /// <summary>Seconds into quad_start.wav where the engine catches — cranking before, running after.</summary>
    private const float CatchTime = 1.35f;

    private bool _running;     // key on (mounted)
    private float _sinceStart; // seconds since the key turned
    private float _gain;       // smoothed master fade (0 while cranking / after key off)
    private float _wobble;

    public float MasterVolume = 0.8f;

    /// <summary>Engine caught and running: the quad may move.</summary>
    public bool EngineReady => _running && _sinceStart >= CatchTime;

    public void Load()
    {
        string dir = Path.Combine(Tweaks.ContentRoot(), "Sounds");
        _start = TryLoad(Path.Combine(dir, "quad_start.wav"));
        _off = TryLoad(Path.Combine(dir, "quad_off.wav"));
        _shift = TryLoad(Path.Combine(dir, "quad_shift.wav"));
        LoadLayer(_idle, Path.Combine(dir, "quad_idle.wav"));
        LoadLayer(_lo, Path.Combine(dir, "quad_drive_lo.wav"));
        LoadLayer(_hi, Path.Combine(dir, "quad_drive.wav"));
    }

    private static void LoadLayer(Layer l, string path)
    {
        l.Fx = TryLoad(path);
        if (l.Fx == null) return;
        l.Inst = l.Fx.CreateInstance();
        l.Inst.IsLooped = true;
        l.Inst.Volume = 0f;
    }

    private static SoundEffect TryLoad(string path)
    {
        try { return File.Exists(path) ? SoundEffect.FromFile(path) : null; }
        catch (Exception e) { Console.WriteLine($"QuadAudio: {path}: {e.Message}"); return null; }
    }

    /// <summary>Turn the key: starter cranks, engine catches at CatchTime, loops fade up under it.</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        _sinceStart = 0f;
        _gain = 0f;
        _start?.Play(MasterVolume, 0f, 0f);
        foreach (var l in Layers) l.Inst?.Play();
    }

    /// <summary>Key off: loops fade, shut-off one-shot plays.</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _off?.Play(MasterVolume * 0.9f, 0f, 0f);
    }

    /// <summary>Pause/menu: silence everything without the shut-off sound.</summary>
    public void Mute()
    {
        _running = false;
        _sinceStart = 0f;
        _gain = 0f;
        foreach (var l in Layers) { l.Inst?.Stop(); l.Vol = 0f; }
    }

    public void Update(QuadController quad, bool mounted, float dt)
    {
        if (mounted && !_running) Start();
        else if (!mounted && _running) Stop();

        if (_running) _sinceStart += dt;

        // clutch in: the foot-shift clunk, right as the revs chop
        if (quad.Shifting && !_wasShifting && EngineReady)
            _shift?.Play(MasterVolume * 0.9f, 0f, 0f);
        _wasShifting = quad.Shifting;

        // master: silent while cranking, up once the engine catches, down after key off
        float gainTarget = EngineReady ? 1f : 0f;
        _gain += (gainTarget - _gain) * (1f - MathF.Exp(-(EngineReady ? 5f : 8f) * dt));

        // which loop carries the note: equal-power crossfade in log-rpm space
        float rpm = MathF.Max(QuadController.IdleRpm, quad.Rpm);
        float L = MathF.Log(rpm), a = MathF.Log(_idle.NativeRpm), b = MathF.Log(_lo.NativeRpm), c = MathF.Log(_hi.NativeRpm);
        float wIdle = 0f, wLo = 0f, wHi = 0f;
        if (L <= a) wIdle = 1f;
        else if (L < b) { float t = (L - a) / (b - a); wIdle = 1f - t; wLo = t; }
        else if (L < c) { float t = (L - b) / (c - b); wLo = 1f - t; wHi = t; }
        else wHi = 1f;

        // louder under throttle and as the revs climb
        float loudness = 0.75f + 0.25f * (rpm - QuadController.IdleRpm) / (QuadController.RedlineRpm - QuadController.IdleRpm);
        if (quad.Throttle) loudness += 0.1f;
        // gear change: the throttle chops shut — a hard dip right as the clutch
        // comes in, then the note builds back as the new gear engages
        if (quad.Shifting)
        {
            float p = quad.ShiftProgress;
            float dip = p < 0.15f ? 0.55f : MathHelper.Lerp(0.55f, 1f, (p - 0.15f) / 0.85f);
            loudness *= dip;
        }

        _wobble += dt;
        float wob = 0.006f * MathF.Sin(_wobble * 1.7f) + 0.004f * MathF.Sin(_wobble * 0.61f);

        Apply(_idle, MathF.Sqrt(wIdle), rpm, loudness, wob, dt);
        Apply(_lo, MathF.Sqrt(wLo), rpm, loudness, wob, dt);
        Apply(_hi, MathF.Sqrt(wHi), rpm, loudness, wob, dt);

        if (!_running && _gain < 0.01f)
            foreach (var l in Layers)
                if (l.Inst?.State == SoundState.Playing) l.Inst.Stop();
    }

    private void Apply(Layer l, float weight, float rpm, float loudness, float wob, float dt)
    {
        if (l.Inst == null) return;
        l.Vol += (weight - l.Vol) * (1f - MathF.Exp(-6f * dt));
        l.Inst.Volume = MathHelper.Clamp(l.Vol * loudness * _gain * MasterVolume, 0f, 1f);
        // MonoGame pitch: -1..1 = one octave down..up
        float pitch = MathF.Log2(rpm / l.NativeRpm) + wob;
        l.Inst.Pitch = MathHelper.Clamp(pitch, -0.5f, 0.5f);
    }

    public void Dispose()
    {
        foreach (var l in Layers) { l.Inst?.Dispose(); l.Fx?.Dispose(); }
        _start?.Dispose(); _off?.Dispose(); _shift?.Dispose();
    }
}
