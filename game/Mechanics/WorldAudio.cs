using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Crewboss.Maps;
using Crewboss.Mechanics.Player;
using System;
using System.Collections.Generic;
using System.IO;

namespace Crewboss.Mechanics;

/// <summary>
/// The block itself: a wilderness bed (birds, bugs, breeze) that runs all day,
/// ducked a little under the quad, and the crewboss's boots — one step per
/// stride of walking, picked at random from a few takes with pitch jitter.
/// Silent if the wavs are missing.
/// </summary>
public sealed class WorldAudio : IDisposable
{
    private SoundEffect _ambience;
    private SoundEffectInstance _ambInst;
    private readonly List<SoundEffect> _steps = new();
    private readonly Random _rng = new();

    private float _ambVol;
    private int _lastCount;
    private int _lastStep = -1;

    public float AmbienceVolume = 0.55f;
    public float StepVolume = 0.5f;

    public void Load()
    {
        string dir = Path.Combine(Tweaks.ContentRoot(), "Sounds");
        _ambience = TryLoad(Path.Combine(dir, "ambience.wav"));
        if (_ambience != null) { _ambInst = _ambience.CreateInstance(); _ambInst.IsLooped = true; _ambInst.Volume = 0f; }
        for (int i = 1; i <= 8; i++)
        {
            var fx = TryLoad(Path.Combine(dir, $"step{i}.wav"));
            if (fx != null) _steps.Add(fx);
        }
    }

    private static SoundEffect TryLoad(string path)
    {
        try { return File.Exists(path) ? SoundEffect.FromFile(path) : null; }
        catch (Exception e) { Console.WriteLine($"WorldAudio: {path}: {e.Message}"); return null; }
    }

    /// <summary>Pause/menu: bed stops, footsteps forget their stride.</summary>
    public void Mute()
    {
        _ambInst?.Stop();
        _ambVol = 0f;
    }

    public void Update(PlayerController player, WorldMap map, bool engineRunning, float dt)
    {
        // wilderness bed: fade in on the first frame of the day, duck under the engine
        if (_ambInst != null)
        {
            if (_ambInst.State != SoundState.Playing) _ambInst.Play();
            float target = AmbienceVolume * (engineRunning ? 0.55f : 1f);
            _ambVol += (target - _ambVol) * (1f - MathF.Exp(-2f * dt));
            _ambInst.Volume = MathHelper.Clamp(_ambVol, 0f, 1f);
        }

        // boots: one step each time the walk cycle hits a contact frame
        if (player.Mounted || !player.Walking) { _lastCount = player.StepCount; return; }
        if (player.StepCount != _lastCount && _steps.Count > 0)
        {
            _lastCount = player.StepCount;
            PlayStep(map?.TerrainName(player.FootPos) ?? "");
        }
    }

    private void PlayStep(string terrain)
    {
        // never the same take twice in a row
        int i = _rng.Next(_steps.Count);
        if (_steps.Count > 1 && i == _lastStep) i = (i + 1) % _steps.Count;
        _lastStep = i;

        // the takes are gravel: full on road/trail, softer and lower on duff, slash and moss
        float vol = terrain switch { "road" => 1f, "trail" => 0.85f, "rock" => 0.8f, "swamp" => 0.35f, _ => 0.5f };
        float pitch = (terrain is "road" or "trail" or "rock" ? 0f : -0.25f) + ((float)_rng.NextDouble() - 0.5f) * 0.2f;
        _steps[i].Play(MathHelper.Clamp(vol * StepVolume, 0f, 1f), MathHelper.Clamp(pitch, -1f, 1f), 0f);
    }

    public void Dispose()
    {
        _ambInst?.Dispose(); _ambience?.Dispose();
        foreach (var s in _steps) s.Dispose();
    }
}
