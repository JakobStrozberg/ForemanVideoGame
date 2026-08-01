using Microsoft.Xna.Framework.Audio;
using System;
using System.IO;

namespace src;

/// <summary>
/// Runtime audio: RPM-pitched engine loop while mounted, skid while drifting,
/// wind/bird ambience, and one-shots for shifts, planting, boxes and UI.
/// All WAVs come from tools/ArtTool `sounds`. Null-safe if files are missing.
/// </summary>
public class AudioSystem
{
    private SoundEffect _shift, _plant, _thud, _blip;
    private SoundEffectInstance _engine, _skid, _ambience;
    private float _enginePitch = -0.6f, _engineVol, _skidVol;

    public static AudioSystem Load(string dir)
    {
        var a = new AudioSystem();
        try
        {
            SoundEffect L(string name)
            {
                string p = Path.Combine(dir, name);
                if (!File.Exists(p)) return null;
                using var fs = File.OpenRead(p);
                return SoundEffect.FromStream(fs);
            }

            var engine = L("EngineLoop.wav");
            if (engine != null)
            {
                a._engine = engine.CreateInstance();
                a._engine.IsLooped = true;
                a._engine.Volume = 0f;
            }
            var skid = L("Skid.wav");
            if (skid != null)
            {
                a._skid = skid.CreateInstance();
                a._skid.IsLooped = true;
                a._skid.Volume = 0f;
            }
            var amb = L("Ambience.wav");
            if (amb != null)
            {
                a._ambience = amb.CreateInstance();
                a._ambience.IsLooped = true;
                a._ambience.Volume = 0.20f;
                a._ambience.Play();
            }
            a._shift = L("GearShift.wav");
            a._plant = L("Plant.wav");
            a._thud = L("BoxThud.wav");
            a._blip = L("Blip.wav");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Audio unavailable: {e.Message}");
        }
        return a;
    }

    /// <summary>
    /// Engine follows RPM: pitch climbs through the gear's speed range, sags
    /// while the clutch is in mid-shift. Skid fades with drifting.
    /// </summary>
    public void Update(float dt, bool mounted, float speed, float gearMax, bool shifting, bool drifting)
    {
        if (_engine != null)
        {
            float targetVol, targetPitch;
            if (mounted)
            {
                float rpm = Math.Clamp(speed / MathF.Max(gearMax, 1f), 0f, 1.15f);
                targetPitch = shifting ? -0.45f : -0.60f + rpm * 1.05f;
                targetVol = shifting ? 0.30f : 0.42f + rpm * 0.18f;
            }
            else
            {
                targetPitch = -0.6f;
                targetVol = 0f;
            }

            float k = MathF.Min(1f, dt * 7f);
            _enginePitch += (targetPitch - _enginePitch) * k;
            _engineVol += (targetVol - _engineVol) * k;
            _engine.Pitch = Math.Clamp(_enginePitch, -1f, 1f);
            _engine.Volume = Math.Clamp(_engineVol, 0f, 1f);

            if (mounted && _engine.State != SoundState.Playing) _engine.Play();
            else if (!mounted && _engineVol < 0.02f && _engine.State == SoundState.Playing) _engine.Stop();
        }

        if (_skid != null)
        {
            float target = drifting ? 0.5f : 0f;
            _skidVol += (target - _skidVol) * MathF.Min(1f, dt * 10f);
            _skid.Volume = Math.Clamp(_skidVol, 0f, 1f);
            if (_skidVol > 0.03f && _skid.State != SoundState.Playing) _skid.Play();
            else if (_skidVol <= 0.03f && _skid.State == SoundState.Playing) _skid.Pause();
        }
    }

    public void Shift() => _shift?.Play(0.55f, 0f, 0f);
    public void Plant() => _plant?.Play(0.30f, 0f, 0f);
    public void Thud() => _thud?.Play(0.55f, 0f, 0f);
    public void Blip() => _blip?.Play(0.40f, 0f, 0f);
}
