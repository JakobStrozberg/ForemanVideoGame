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
        _screenManager.Initialize();
        
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
        GraphicsDevice.Clear(Color.Black);

        _spriteBatch.Begin();
        
        // Draw current screen
        _screenManager.Draw(gameTime, _spriteBatch);
        
        _spriteBatch.End();

        base.Draw(gameTime);
    }
}
