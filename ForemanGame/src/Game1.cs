using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using src.Screens;

namespace src;

public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    
    // Game components
    private GameManager _gameManager;
    private ScreenManager _screenManager;
    
    // Expose ScreenManager property to make it accessible from screens
    public ScreenManager ScreenManager => _screenManager;
    
    // Screen dimensions
    private int _screenWidth = 1600;
    private int _screenHeight = 900;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        // Run unlocked, paced by vsync: one update per displayed frame at the
        // monitor's native refresh (120Hz Macs included) instead of the fixed
        // 60Hz step doubling/skipping frames — even pacing, no judder.
        IsFixedTimeStep = false;
        _graphics.SynchronizeWithVerticalRetrace = true;
        
        // Set screen dimensions
        _graphics.PreferredBackBufferWidth = _screenWidth;
        _graphics.PreferredBackBufferHeight = _screenHeight;
    }

    protected override void Initialize()
    {
        // Apply graphics changes
        _graphics.ApplyChanges();
        
        // Initialize game components
        _gameManager = new GameManager(this);
        _screenManager = new ScreenManager(this);

        // F5 relaunch: jump straight back into the map, skipping the menu
        string autoMap = System.Environment.GetEnvironmentVariable("CREWBOSS_AUTOSTART");
        if (!string.IsNullOrEmpty(autoMap))
        {
            _screenManager.RegisterScreen("MainMenu", new MainMenuScreen(this));
            var gameplay = new GameplayScreen(this, autoMap);
            gameplay.SkipIntro();
            _screenManager.RegisterScreen("Gameplay", gameplay);
            _screenManager.ChangeScreen("Gameplay");
        }
        else
        {
            _screenManager.Initialize();
        }
        
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        
        // Load screen content
        _screenManager.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        // Update current screen
        _screenManager.Update(gameTime);
        
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        // Render-target passes (e.g. gameplay world buffer) before the main batch
        _screenManager.PreDraw(gameTime, _spriteBatch);

        GraphicsDevice.Clear(Color.Black);

        _spriteBatch.Begin();

        // Draw current screen
        _screenManager.Draw(gameTime, _spriteBatch);

        _spriteBatch.End();

        base.Draw(gameTime);
    }
}
