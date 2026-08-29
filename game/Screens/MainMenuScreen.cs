using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Text.Json;

namespace Crewboss.Screens;

/// <summary>
/// Title + block select. One card per block: name, size, best stars, lock
/// state. Keyboard (left/right, enter) and mouse (hover, click). Pixel-font
/// UI drawn from solid rectangles — no menu art to maintain.
/// </summary>
public class MainMenuScreen : Screen
{
    private readonly GameInput _input = new();
    private Presenter _presenter;
    private GameArt _art;
    private Texture2D _title;
    private Progress _progress;
    private int _selected;
    private float _time;
    private bool _settingsFlash;
    private float _settingsFlashTimer;

    // per-block size read from the generated tile data
    private readonly string[] _sizes = new string[Blocks.All.Length];
    private readonly Rectangle[] _cards = new Rectangle[Blocks.All.Length];
    private Rectangle _settingsRect;

    public MainMenuScreen(CrewbossGame game) : base(game) { }

    public override void OnShown() { if (_art != null) Refresh(); }

    public override void LoadContent()
    {
        var vp = Game.GraphicsDevice.Viewport;
        _presenter = new Presenter(vp.Width, vp.Height, 1.5f);
        _art = GameArt.Load(Game.GraphicsDevice);
        try { _title = Game.Content.Load<Texture2D>("GameTextures/GameTitle"); } catch { _title = null; }
        Refresh();
    }

    /// <summary>Re-read progress and block sizes — called on every return to the menu.</summary>
    private void Refresh()
    {
        _progress = Progress.Load();
        for (int i = 0; i < Blocks.All.Length; i++)
        {
            string tiles = Path.Combine(Tweaks.ContentRoot(), "Maps", Blocks.All[i].Id, $"{Blocks.All[i].Id}.tiles.json");
            _sizes[i] = "";
            try
            {
                if (File.Exists(tiles))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(tiles));
                    var r = doc.RootElement;
                    _sizes[i] = $"{r.GetProperty("width").GetInt32()} X {r.GetProperty("height").GetInt32()} TILES";
                }
            }
            catch { /* size is decoration */ }
        }
        // select the furthest unlocked block by default
        _selected = 0;
        for (int i = 0; i < Blocks.All.Length; i++) if (_progress.IsUnlocked(i)) _selected = i;
    }

    public override void Update(GameTime gameTime)
    {
        _input.Update();
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _time += dt;
        if (_settingsFlashTimer > 0) { _settingsFlashTimer -= dt; if (_settingsFlashTimer <= 0) _settingsFlash = false; }

        int n = Blocks.All.Length;
        if (_input.MenuLeft) _selected = (_selected + n - 1) % n;
        if (_input.MenuRight) _selected = (_selected + 1) % n;

        // mouse: hover selects, click plays
        Point m = _input.MousePos;
        var mv = new Point((int)(m.X / _presenter.Zoom), (int)(m.Y / _presenter.Zoom));
        for (int i = 0; i < n; i++)
            if (_cards[i].Contains(mv)) { _selected = i; if (_input.MouseClicked) Play(i); }
        if (_settingsRect.Contains(mv) && _input.MouseClicked) FlashSettings();

        if (_input.MenuSelect) Play(_selected);
        if (_input.Pause) Game.Exit();
    }

    private void FlashSettings() { _settingsFlash = true; _settingsFlashTimer = 1.5f; }

    private void Play(int i)
    {
        if (!_progress.IsUnlocked(i)) return;
        var screen = new GameplayScreen(Game, Blocks.All[i].Id);
        Game.ScreenManager.RegisterScreen("Gameplay", screen);
        Game.ScreenManager.ChangeScreen("Gameplay");
        Refresh(); // so returning shows fresh stars
    }

    public override void PreDraw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        var gd = Game.GraphicsDevice;
        int w = _presenter.ViewWidth, h = _presenter.ViewHeight;
        gd.SetRenderTarget(_presenter.Scene(gd));
        gd.Clear(new Color(18, 26, 20));
        spriteBatch.Begin(SpriteSortMode.Deferred, null, SamplerState.PointClamp);

        var font = _art.Font;
        var gold = new Color(255, 222, 92);
        var dim = new Color(120, 128, 116);

        // title
        if (_title != null)
        {
            float s = Math.Min(1f, (w * 0.5f) / _title.Width);
            int tw = (int)(_title.Width * s), th = (int)(_title.Height * s);
            spriteBatch.Draw(_title, new Rectangle((w - tw) / 2, 28, tw, th), Color.White);
        }
        else font?.DrawCentered(spriteBatch, "CREWBOSS", w / 2f, 40, 9f, Color.White);
        font?.DrawCentered(spriteBatch, "PICK A BLOCK", w / 2f, h * 0.30f, 3f, dim);

        // cards
        int n = Blocks.All.Length;
        int cardW = Math.Min(300, (w - 60) / n - 16), cardH = 170;
        int totalW = n * cardW + (n - 1) * 16;
        int x0 = (w - totalW) / 2, y0 = (int)(h * 0.38f);
        for (int i = 0; i < n; i++)
        {
            var b = Blocks.All[i];
            bool unlocked = _progress.IsUnlocked(i);
            bool sel = i == _selected;
            var r = new Rectangle(x0 + i * (cardW + 16), y0 + (sel ? -6 : 0), cardW, cardH);
            _cards[i] = r;

            spriteBatch.Draw(_art.Solid("cardBg", new Color(30, 42, 34)), r, Color.White);
            var edge = _art.Solid("cardEdge", sel ? gold : new Color(60, 78, 64));
            spriteBatch.Draw(edge, new Rectangle(r.X, r.Y, r.Width, 3), Color.White);
            spriteBatch.Draw(edge, new Rectangle(r.X, r.Bottom - 3, r.Width, 3), Color.White);
            spriteBatch.Draw(edge, new Rectangle(r.X, r.Y, 3, r.Height), Color.White);
            spriteBatch.Draw(edge, new Rectangle(r.Right - 3, r.Y, 3, r.Height), Color.White);
            if (font == null) continue;

            float cx = r.X + r.Width / 2f;
            font.DrawCentered(spriteBatch, $"BLOCK {i + 1}", cx, r.Y + 16, 2.2f, dim);
            font.DrawCentered(spriteBatch, b.Title, cx, r.Y + 40, 3.6f, unlocked ? Color.White : dim);
            font.DrawCentered(spriteBatch, _sizes[i], cx, r.Y + 78, 2f, dim);
            if (unlocked)
            {
                int stars = _progress.Stars(b.Id);
                font.DrawCentered(spriteBatch, stars > 0 ? $"BEST: {stars}/3 STARS" : "NOT YET PLANTED", cx, r.Y + 106, 2.2f,
                    stars > 0 ? gold : dim);
                if (sel) font.DrawCentered(spriteBatch, "ENTER: PLANT IT", cx, r.Y + 140, 2.2f, gold);
            }
            else
            {
                font.DrawCentered(spriteBatch, "LOCKED", cx, r.Y + 106, 2.6f, new Color(190, 80, 60));
                font.DrawCentered(spriteBatch, $"1 STAR ON BLOCK {i}", cx, r.Y + 134, 2f, dim);
            }
        }

        // blurb for the selected block + footer
        font?.DrawCentered(spriteBatch, Blocks.All[_selected].Blurb, w / 2f, y0 + cardH + 30, 2.4f, Color.White);
        _settingsRect = new Rectangle(w / 2 - 70, h - 64, 140, 22);
        font?.DrawCentered(spriteBatch, _settingsFlash ? "SETTINGS: COMING SOON" : "SETTINGS", w / 2f, h - 60, 2.4f, dim);
        font?.DrawCentered(spriteBatch, "LEFT/RIGHT . ENTER . ESC QUITS", w / 2f, h - 30, 2f, dim);

        spriteBatch.End();
        gd.SetRenderTarget(null);
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        _presenter.Present(spriteBatch, Vector2.Zero, drawUi: false);
    }
}
