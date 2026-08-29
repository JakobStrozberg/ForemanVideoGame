using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace Crewboss.Screens;

/// <summary>
/// One block, one day. This screen only wires the modules together and runs
/// the frame: input -> mechanics -> camera -> render. All behavior lives in
/// Mechanics/ (quad, player, planters, day) and Rendering/.
/// </summary>
public class GameplayScreen : Screen
{
    private readonly string _blockName;

    private readonly GameInput _input = new();
    private readonly Presenter _presenter;
    private readonly Camera _camera = new();
    private readonly DayClock _day = new();
    private readonly List<CacheEntity> _caches = new();

    private WorldMap _map;
    private GameArt _art;
    private QuadController _quad;
    private QuadEffects _effects;
    private readonly QuadAudio _audio = new();
    private PlayerController _player;
    private PlanterSystem _planters;
    private WorldRenderer _world;
    private Hud _hud;

    private bool _showHelp = true;
    private bool _debug; // F3 dev view: tiles, pieces, planter brains
    private float _preGameGrace = 0.35f; // the menu keypress that launched us must not skip the overview

    // pause menu: Resume / Restart Day / Quit To Menu
    private bool _paused;
    private int _pauseIndex;
    public static readonly string[] PauseItems = { "RESUME", "RESTART DAY", "QUIT TO MENU" };

    public GameplayScreen(CrewbossGame game, string blockName) : base(game)
    {
        _blockName = blockName;
        var vp = game.GraphicsDevice.Viewport;
        _presenter = new Presenter(vp.Width, vp.Height, Tweaks.CameraZoom);
        SyncView();
    }

    /// <summary>Skip the block-overview intro — straight into the day.</summary>
    public void SkipIntro() => _day.PreGame = false;

    private void SyncView()
    {
        _camera.ViewWidth = _presenter.ViewWidth;
        _camera.ViewHeight = _presenter.ViewHeight;
        _camera.Reset();
    }

    public override void LoadContent()
    {
        Tweaks.Load();
        try
        {
            var gd = Game.GraphicsDevice;
            _map = WorldMap.Load(gd, _blockName);
            _art = GameArt.Load(gd);

            _quad = new QuadController();
            _quad.Reset(_map.Spawn());
            _effects = new QuadEffects();
            _effects.Load(gd);
            _audio.Load();

            _player = new PlayerController(_quad, _map, _caches);
            if (_map.Tiles != null)
            {
                _planters = new PlanterSystem(_map.Tiles, _caches);
                _planters.SpawnCrew(_quad.Pos); // the crew waits by the trucks
                _player.Planters = _planters;
            }

            _world = new WorldRenderer(_art, _map, _camera, _quad, _player, _effects, _caches) { Planters = _planters };
            _hud = new Hud(_art);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error in GameplayScreen.LoadContent: {e.Message}");
        }
    }

    public override void Update(GameTime gameTime)
    {
        _input.Update();
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        if (_input.Restart) { _audio.Mute(); Restart(); return; }

        // Esc: pause during the day; in the overview or on the score screen it quits to the menu
        if (_input.Pause)
        {
            if (_map == null || _day.PreGame || _day.Over) { _audio.Mute(); QuitToMenu(); return; }
            _paused = !_paused;
            _pauseIndex = 0;
            if (_paused) _audio.Mute();
        }
        if (_paused)
        {
            if (_input.MenuUp) _pauseIndex = (_pauseIndex + PauseItems.Length - 1) % PauseItems.Length;
            if (_input.MenuDown) _pauseIndex = (_pauseIndex + 1) % PauseItems.Length;
            if (_input.MenuSelect)
            {
                switch (_pauseIndex)
                {
                    case 0: _paused = false; break;
                    case 1: _audio.Mute(); RestartDay(); break;
                    case 2: _audio.Mute(); QuitToMenu(); break;
                }
            }
            return;
        }

        // +/- = presentation zoom (world math stays 1:1)
        if (_input.ZoomIn) { _presenter.ApplyZoom(_presenter.Zoom * 1.15f); SyncView(); }
        if (_input.ZoomOut) { _presenter.ApplyZoom(_presenter.Zoom / 1.15f); SyncView(); }
        if (_input.ToggleHelp) _showHelp = !_showHelp;
        if (_input.ToggleDebug) _debug = !_debug;

        if (_map == null) return;

        // pre-game: block overview until any key
        if (_day.PreGame)
        {
            _preGameGrace -= dt;
            if (_preGameGrace <= 0f && _input.AnyKeyPressed) _day.PreGame = false;
            return;
        }

        // day over: score screen until R starts a new day
        if (_day.Over)
        {
            if (_input.Reset) { _day.Restart(); ResetPlayer(); }
            return;
        }
        if (_day.Update(dt)) { _audio.Mute(); RecordResult(); return; }

        if (_input.Reset) ResetPlayer();

        if (_player.Aiming)
        {
            _player.UpdateAiming(_input, dt);
        }
        else
        {
            _player.HandleActions(_input);
            _player.UpdateReveals(dt);
            if (_player.Mounted)
            {
                _quad.EngineOn = _audio.EngineReady;
                _quad.Update(_input, _map, dt);
                _effects.Update(_quad, _map, dt);
            }
            else
            {
                _player.UpdateFootMovement(_input, dt);
            }
        }

        _audio.Update(_quad, _player.Mounted, dt);
        _planters?.Update(dt, _player.Pos);

        Vector2 lead = _player.Mounted ? _quad.Velocity * 0.25f : Vector2.Zero;
        _camera.Update(_player.Pos, lead, _map.Bounds, dt);
    }

    /// <summary>A fresh screen for the same block: new day, new crew, empty caches.</summary>
    private void RestartDay()
    {
        Tweaks.Load();
        var fresh = new GameplayScreen(Game, _blockName);
        fresh.SkipIntro();
        Game.ScreenManager.RegisterScreen("Gameplay", fresh);
        Game.ScreenManager.ChangeScreen("Gameplay");
    }

    private void QuitToMenu() => Game.ScreenManager.ChangeScreen("MainMenu");

    /// <summary>Day over: keep the best star result for this block.</summary>
    private void RecordResult()
    {
        if (_planters == null) return;
        int stars = DayClock.Stars(_planters.TreesPlanted, _planters.Faults, (int)_planters.IdleSeconds);
        Progress.Load().Record(_blockName, stars);
    }

    private void ResetPlayer()
    {
        _quad.Reset(_map.Spawn());
        _player.Reset();
        Console.WriteLine("Player reset to spawn");
    }

    /// <summary>
    /// F5: relaunch the WHOLE process via `dotnet run` on the desktop head,
    /// which recompiles first — so CODE changes land too, not just assets.
    /// Falls back to a fresh in-process screen when there's no repo around.
    /// </summary>
    private void Restart()
    {
        string repo = Tweaks.RepoRoot();
        string projectDir = repo == null ? null : Path.Combine(repo, "platforms", "Desktop");
        if (projectDir != null && File.Exists(Path.Combine(projectDir, "Crewboss.Desktop.csproj")))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "run",
                    WorkingDirectory = projectDir,
                    UseShellExecute = false
                };
                psi.EnvironmentVariables["CREWBOSS_AUTOSTART"] = _blockName; // straight back into this block
                System.Diagnostics.Process.Start(psi);
                Game.Exit();
                return;
            }
            catch { /* dotnet not on PATH — fall back below */ }
        }
        Tweaks.Load();
        var fresh = new GameplayScreen(Game, _blockName);
        fresh.SkipIntro();
        Game.ScreenManager.RegisterScreen("Gameplay", fresh);
        Game.ScreenManager.ChangeScreen("Gameplay");
    }

    public override void PreDraw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        var gd = Game.GraphicsDevice;

        gd.SetRenderTarget(_presenter.Scene(gd));
        gd.Clear(Color.Black);
        spriteBatch.Begin(SpriteSortMode.Deferred, null, SamplerState.PointClamp);
        if (_map != null)
        {
            if (_day.PreGame) _hud.DrawPreGame(spriteBatch, _presenter.ViewWidth, _presenter.ViewHeight, _map.Texture, Blocks.Get(_blockName).Title);
            else
            {
                _world.Draw(spriteBatch);
                if (_debug) _world.DrawDebug(spriteBatch);
            }
        }
        spriteBatch.End();

        // screen-anchored overlays: separate target, presented unshifted
        gd.SetRenderTarget(_presenter.Ui(gd));
        gd.Clear(Color.Transparent);
        spriteBatch.Begin(SpriteSortMode.Deferred, null, SamplerState.PointClamp);
        if (_map != null && !_day.PreGame)
            _hud.Draw(spriteBatch, _presenter.ViewWidth, _presenter.ViewHeight, _day, _quad, _player, _planters, _showHelp);
        if (_paused)
            _hud.DrawPause(spriteBatch, _presenter.ViewWidth, _presenter.ViewHeight, PauseItems, _pauseIndex);
        spriteBatch.End();
        gd.SetRenderTarget(null);
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        // one smooth upscale, shifted by the camera's sub-pixel remainder
        _presenter.Present(spriteBatch, _day.PreGame ? Vector2.Zero : _camera.Frac, drawUi: !_day.PreGame);
    }
}
