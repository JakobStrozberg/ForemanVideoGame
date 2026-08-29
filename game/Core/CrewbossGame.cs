using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Crewboss.Core;

public class CrewbossGame : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    
    private ScreenManager _screenManager;
    
    // Expose ScreenManager property to make it accessible from screens
    public ScreenManager ScreenManager => _screenManager;
    
    // Screen dimensions
    private int _screenWidth = 1600;
    private int _screenHeight = 900;

    public CrewbossGame()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        // Run unlocked, paced by vsync: one update per displayed frame at the
        // monitor's native refresh (120Hz Macs included) instead of the fixed
        // 60Hz step doubling/skipping frames — even pacing, no judder.
        IsFixedTimeStep = false;
        _graphics.SynchronizeWithVerticalRetrace = true;
        
        // Window shape. CREWBOSS_WINDOW=phone previews a landscape iPhone
        // aspect (19.5:9) so mobile framing can be checked on the Mac anytime.
        if (System.Environment.GetEnvironmentVariable("CREWBOSS_WINDOW") == "phone")
        {
            _screenWidth = 1950;
            _screenHeight = 900;
        }
        _graphics.PreferredBackBufferWidth = _screenWidth;
        _graphics.PreferredBackBufferHeight = _screenHeight;
    }

    protected override void Initialize()
    {
        // Apply graphics changes
        _graphics.ApplyChanges();
        
        // Initialize game components
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
