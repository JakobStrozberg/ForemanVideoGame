using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace src.Screens;

public class MainMenuScreen : Screen
{
    private Texture2D _backgroundTexture;
    private Texture2D _titleTexture;
    private Texture2D _tapAnyTexture;
    
    // Main menu textures
    private Texture2D _mainMenuBackgroundTexture;
    private Texture2D _newGameButtonTexture;
    private Texture2D _loadGameButtonTexture;
    private Texture2D _supportButtonTexture;
    private Texture2D _customizeButtonTexture;
    
    // Map selection textures
    private Texture2D _map1ButtonTexture;
    private Texture2D _map2ButtonTexture;
    
    // Button positions and sizes
    private Rectangle _newGameButtonRect;
    private Rectangle _loadGameButtonRect;
    private Rectangle _supportButtonRect;
    private Rectangle _customizeButtonRect;
    
    // Map selection button positions
    private Rectangle _map1ButtonRect;
    private Rectangle _map2ButtonRect;
    
    // Screen state 
    private enum ScreenState { IntroScreen, MainMenu, MapSelection }
    private ScreenState _currentState;
    
    // Button animation properties
    private bool _isButtonAnimating;
    private float _buttonAnimationTimer;
    private const float BUTTON_ANIMATION_DURATION = 0.25f; // Animation duration in seconds
    private Rectangle _animatingButtonRect;
    
    // Mouse states for click detection
    private MouseState _currentMouseState;
    private MouseState _previousMouseState;
    
    // Screen dimensions (to position elements)
    private int _screenWidth;
    private int _screenHeight;
    
    // Animation properties for the title
    private float _titleAlpha;
    private float _titleScale;
    private float _animationTimer;
    private bool _animationComplete;
    private const float ANIMATION_DURATION = 2.0f; // Animation duration in seconds
    
    // Animation properties for the "tap any" message
    private float _tapAnyAlpha;
    private float _blinkTimer;
    private const float BLINK_RATE = 0.8f; // Blink rate in seconds
    
    public MainMenuScreen(Game1 game) : base(game)
    {
        _screenWidth = game.GraphicsDevice.Viewport.Width;
        _screenHeight = game.GraphicsDevice.Viewport.Height;
        
        // Initialize animation properties
        _titleAlpha = 0f;
        _titleScale = 0.5f;
        _animationTimer = 0f;
        _animationComplete = false;
        
        // Initialize tap any properties
        _tapAnyAlpha = 0f;
        _blinkTimer = 0f;
        
        // Initialize button animation properties
        _isButtonAnimating = false;
        _buttonAnimationTimer = 0f;
        
        // Initialize screen state
        _currentState = ScreenState.IntroScreen;
        
        // Initialize mouse states
        _currentMouseState = Mouse.GetState();
        _previousMouseState = _currentMouseState;
    }
    
    public override void LoadContent()
    {
        try
        {
            // Load textures for intro screen
            _backgroundTexture = Game.Content.Load<Texture2D>("GameTextures/Menu-images/Menu3");
            _titleTexture = Game.Content.Load<Texture2D>("GameTextures/GameTitle");
            _tapAnyTexture = Game.Content.Load<Texture2D>("GameTextures/TapAny");
            
            // Load textures for main menu - using try/catch to debug issues
            try
            {
                _mainMenuBackgroundTexture = Game.Content.Load<Texture2D>("GameTextures/Menu-images/Menu2");
                Console.WriteLine("Successfully loaded Menu2");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error loading Menu2: {e.Message}");
                // Fallback to Menu3 if Menu2 can't be loaded
                _mainMenuBackgroundTexture = _backgroundTexture;
            }
            
            try
            {
                _newGameButtonTexture = Game.Content.Load<Texture2D>("GameTextures/Buttons/NameGame");
                _loadGameButtonTexture = Game.Content.Load<Texture2D>("GameTextures/Buttons/LoadGame");
                _supportButtonTexture = Game.Content.Load<Texture2D>("GameTextures/Buttons/Support");
                _customizeButtonTexture = Game.Content.Load<Texture2D>("GameTextures/Buttons/Customize");
                
                // Load map selection button textures using direct file paths
                LoadMapTextures();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error loading button textures: {e.Message}");
                // Create a placeholder texture for buttons if they can't be loaded
                _newGameButtonTexture = new Texture2D(Game.GraphicsDevice, 200, 50);
                _loadGameButtonTexture = new Texture2D(Game.GraphicsDevice, 200, 50);
                _supportButtonTexture = new Texture2D(Game.GraphicsDevice, 200, 50);
                _customizeButtonTexture = new Texture2D(Game.GraphicsDevice, 200, 50);
                
                // Create map button placeholders
                _map1ButtonTexture = new Texture2D(Game.GraphicsDevice, 200, 50);
                _map2ButtonTexture = new Texture2D(Game.GraphicsDevice, 200, 50);
                
                // Fill with solid colors for testing
                Color[] newGameData = new Color[200 * 50];
                Color[] loadGameData = new Color[200 * 50];
                Color[] supportData = new Color[200 * 50];
                Color[] customizeData = new Color[200 * 50];
                Color[] map1Data = new Color[200 * 50];
                Color[] map2Data = new Color[200 * 50];
                
                for (int i = 0; i < newGameData.Length; i++)
                {
                    newGameData[i] = Color.Red;
                    loadGameData[i] = Color.Green;
                    supportData[i] = Color.Blue;
                    customizeData[i] = Color.Yellow;
                    map1Data[i] = Color.Purple;
                    map2Data[i] = Color.Orange;
                }
                
                _newGameButtonTexture.SetData(newGameData);
                _loadGameButtonTexture.SetData(loadGameData);
                _supportButtonTexture.SetData(supportData);
                _customizeButtonTexture.SetData(customizeData);
                _map1ButtonTexture.SetData(map1Data);
                _map2ButtonTexture.SetData(map2Data);
            }
            
            // Calculate button positions and sizes
            CalculateButtonPositions();
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error in LoadContent: {e.Message}");
        }
    }
    
    private void CalculateButtonPositions()
    {
        // Calculate positions for buttons on the left side of the screen
        int buttonMargin = 20; // Space between buttons
        int leftMargin = 100;  // Distance from left edge
        
        // Button width/height ratio approximation - adjust as needed based on actual images
        float aspectRatio = 3f; // Typical button is wider than tall (3:1 ratio)
        
        int buttonWidth = _screenWidth / 4;
        int buttonHeight = (int)(buttonWidth / aspectRatio);
        
        // Position buttons along the left side with proper spacing
        int startY = _screenHeight / 5; // Start 1/5 down the screen (moved up from 1/4)
        
        _newGameButtonRect = new Rectangle(leftMargin, startY, buttonWidth, buttonHeight);
        _loadGameButtonRect = new Rectangle(leftMargin, startY + buttonHeight + buttonMargin, buttonWidth, buttonHeight);
        _supportButtonRect = new Rectangle(leftMargin, startY + (buttonHeight + buttonMargin) * 2, buttonWidth, buttonHeight);
        _customizeButtonRect = new Rectangle(leftMargin, startY + (buttonHeight + buttonMargin) * 3, buttonWidth, buttonHeight);
        
        // Position map selection buttons in the center of the screen
        int mapButtonWidth = buttonWidth;
        int mapButtonHeight = buttonHeight;
        int mapButtonSpacing = buttonMargin * 3;
        
        int mapButtonsX = (_screenWidth - (mapButtonWidth * 2 + mapButtonSpacing)) / 2;
        int mapButtonsY = _screenHeight / 3;
        
        _map1ButtonRect = new Rectangle(mapButtonsX, mapButtonsY, mapButtonWidth, mapButtonHeight);
        _map2ButtonRect = new Rectangle(mapButtonsX + mapButtonWidth + mapButtonSpacing, mapButtonsY, mapButtonWidth, mapButtonHeight);
    }
    
    private void LoadMapTextures()
    {
        try
        {
            // Try to load via content pipeline first
            try
            {
                _map1ButtonTexture = Game.Content.Load<Texture2D>("GameTextures/Buttons/Map1");
                _map2ButtonTexture = Game.Content.Load<Texture2D>("GameTextures/Buttons/Map2");
                Console.WriteLine("Successfully loaded map buttons via content pipeline");
            }
            catch
            {
                // If that fails, try loading from direct file paths
                using (System.IO.FileStream fileStream = new System.IO.FileStream(
                    "/Users/jakobstrozberg/Desktop/CrewbossGame/src/Content/GameTextures/Buttons/Map1.png", 
                    System.IO.FileMode.Open))
                {
                    _map1ButtonTexture = Texture2D.FromStream(Game.GraphicsDevice, fileStream);
                    Console.WriteLine("Loaded Map1 from direct file path");
                }
                
                using (System.IO.FileStream fileStream = new System.IO.FileStream(
                    "/Users/jakobstrozberg/Desktop/CrewbossGame/src/Content/GameTextures/Buttons/Map2.png", 
                    System.IO.FileMode.Open))
                {
                    _map2ButtonTexture = Texture2D.FromStream(Game.GraphicsDevice, fileStream);
                    Console.WriteLine("Loaded Map2 from direct file path");
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error loading map textures from direct file paths: {e.Message}");
            
            // If all loading attempts fail, create colored placeholders
            _map1ButtonTexture = new Texture2D(Game.GraphicsDevice, 200, 50);
            _map2ButtonTexture = new Texture2D(Game.GraphicsDevice, 200, 50);
            
            Color[] map1Data = new Color[200 * 50];
            Color[] map2Data = new Color[200 * 50];
            
            for (int i = 0; i < map1Data.Length; i++)
            {
                map1Data[i] = Color.Purple;
                map2Data[i] = Color.Orange;
            }
            
            _map1ButtonTexture.SetData(map1Data);
            _map2ButtonTexture.SetData(map2Data);
        }
    }
    
    public override void Update(GameTime gameTime)
    {
        // Update mouse state
        _previousMouseState = _currentMouseState;
        _currentMouseState = Mouse.GetState();
        
        switch (_currentState)
        {
            case ScreenState.IntroScreen:
                UpdateIntroScreen(gameTime);
                break;
                
            case ScreenState.MainMenu:
                UpdateMainMenu(gameTime);
                break;
                
            case ScreenState.MapSelection:
                UpdateMapSelection(gameTime);
                break;
        }
    }
    
    private void UpdateIntroScreen(GameTime gameTime)
    {
        // Update title animation
        if (!_animationComplete)
        {
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _animationTimer += deltaTime;
            
            // Calculate animation progress (0.0 to 1.0)
            float progress = Math.Min(_animationTimer / ANIMATION_DURATION, 1.0f);
            
            // Update animation properties using easing function for smoother animation
            _titleAlpha = EaseInOut(progress);
            _titleScale = 0.5f + (0.5f * EaseInOut(progress));
            
            // Check if animation is complete
            if (progress >= 1.0f)
            {
                _animationComplete = true;
                _titleAlpha = 1.0f;
                _titleScale = 1.0f;
            }
        }
        
        // Update tap any text blinking (only when title animation is complete)
        if (_animationComplete)
        {
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _blinkTimer += deltaTime;
            
            // Create a pulsing/blinking effect
            _tapAnyAlpha = 0.5f + (float)(Math.Sin(_blinkTimer * Math.PI / BLINK_RATE) * 0.5f);
            
            // Check for mouse click (or any input) to transition to the main menu
            if (IsMouseClicked() || IsKeyPressed(Keys.Enter) || IsKeyPressed(Keys.Space))
            {
                _currentState = ScreenState.MainMenu;
            }
        }
    }
    
    private void UpdateMainMenu(GameTime gameTime)
    {
        // Update button animation if active
        if (_isButtonAnimating)
        {
            _buttonAnimationTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
            
            // Check if the animation has completed
            if (_buttonAnimationTimer >= BUTTON_ANIMATION_DURATION)
            {
                _isButtonAnimating = false;
                _buttonAnimationTimer = 0f;
            }
        }
        
        // Check for button clicks only if not currently animating
        if (!_isButtonAnimating && IsMouseClicked())
        {
            Point mousePosition = _currentMouseState.Position;
            
            if (_newGameButtonRect.Contains(mousePosition))
            {
                // Handle New Game button click - start animation
                _isButtonAnimating = true;
                _animatingButtonRect = _newGameButtonRect;
                _buttonAnimationTimer = 0f;
                
                // Navigate to map selection screen after animation duration
                Console.WriteLine("New Game clicked - showing map selection");
                _currentState = ScreenState.MapSelection;
            }
            else if (_loadGameButtonRect.Contains(mousePosition))
            {
                // Handle Load Game button click
                _isButtonAnimating = true;
                _animatingButtonRect = _loadGameButtonRect;
                _buttonAnimationTimer = 0f;
                Console.WriteLine("Load Game clicked");
                // Later: Show saved games screen
            }
            else if (_supportButtonRect.Contains(mousePosition))
            {
                // Handle Support button click
                _isButtonAnimating = true;
                _animatingButtonRect = _supportButtonRect;
                _buttonAnimationTimer = 0f;
                Console.WriteLine("Support clicked");
                // Later: Show support options
            }
            else if (_customizeButtonRect.Contains(mousePosition))
            {
                // Handle Customize button click
                _isButtonAnimating = true;
                _animatingButtonRect = _customizeButtonRect;
                _buttonAnimationTimer = 0f;
                Console.WriteLine("Customize clicked");
                // Later: Show customization screen
            }
        }
    }
    
    private void UpdateMapSelection(GameTime gameTime)
    {
        // Update button animation if active
        if (_isButtonAnimating)
        {
            _buttonAnimationTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
            
            // Check if the animation has completed
            if (_buttonAnimationTimer >= BUTTON_ANIMATION_DURATION)
            {
                _isButtonAnimating = false;
                _buttonAnimationTimer = 0f;
            }
        }
        
        // Check for button clicks only if not currently animating
        if (!_isButtonAnimating && IsMouseClicked())
        {
            Point mousePosition = _currentMouseState.Position;
            
            if (_map1ButtonRect.Contains(mousePosition))
            {
                // Handle Map 1 button click
                _isButtonAnimating = true;
                _animatingButtonRect = _map1ButtonRect;
                _buttonAnimationTimer = 0f;
                Console.WriteLine("Map 1 selected");
                
                // Create the gameplay screen with Map1
                GameplayScreen gameplayScreen = new GameplayScreen(Game, "Map1");
                // Register the screen with the screen manager
                ScreenManager screenManager = GetScreenManager();
                if (screenManager != null)
                {
                    screenManager.RegisterScreen("Gameplay", gameplayScreen);
                    screenManager.ChangeScreen("Gameplay");
                }
            }
            else if (_map2ButtonRect.Contains(mousePosition))
            {
                // Handle Map 2 button click
                _isButtonAnimating = true;
                _animatingButtonRect = _map2ButtonRect;
                _buttonAnimationTimer = 0f;
                Console.WriteLine("Map 2 selected");
                
                // Create the gameplay screen with Map1 when Map2 button is clicked
                GameplayScreen gameplayScreen = new GameplayScreen(Game, "Map2");
                // Register the screen with the screen manager
                ScreenManager screenManager = GetScreenManager();
                if (screenManager != null)
                {
                    screenManager.RegisterScreen("Gameplay", gameplayScreen);
                    screenManager.ChangeScreen("Gameplay");
                }
            }
        }
        
        // Check for back button (Escape key)
        if (IsKeyPressed(Keys.Escape))
        {
            _currentState = ScreenState.MainMenu;
        }
    }
    
    private bool IsMouseClicked()
    {
        return _currentMouseState.LeftButton == ButtonState.Released && 
               _previousMouseState.LeftButton == ButtonState.Pressed;
    }
    
    private bool IsKeyPressed(Keys key)
    {
        KeyboardState keyState = Keyboard.GetState();
        return keyState.IsKeyDown(key);
    }
    
    // Simple ease in-out function for smoother animation
    private float EaseInOut(float t)
    {
        return t < 0.5f ? 2 * t * t : 1 - (float)Math.Pow(-2 * t + 2, 2) / 2;
    }
    
    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        switch (_currentState)
        {
            case ScreenState.IntroScreen:
                DrawIntroScreen(spriteBatch);
                break;
                
            case ScreenState.MainMenu:
                DrawMainMenu(spriteBatch);
                break;
                
            case ScreenState.MapSelection:
                DrawMapSelection(spriteBatch);
                break;
        }
    }
    
    private void DrawIntroScreen(SpriteBatch spriteBatch)
    {
        // Draw the background to fill the screen
        spriteBatch.Draw(_backgroundTexture, new Rectangle(0, 0, _screenWidth, _screenHeight), Color.White);
        
        // Draw the game title centered at the top with animation effects
        if (_titleTexture != null)
        {
            // Calculate the center position for the title
            Vector2 titleOrigin = new Vector2(_titleTexture.Width / 2, _titleTexture.Height / 2);
            Vector2 titleCenter = new Vector2(_screenWidth / 2, _titleTexture.Height / 2 + 30);
            
            // Draw with animation effects (scale, transparency)
            spriteBatch.Draw(
                _titleTexture,
                titleCenter,
                null,
                Color.White * _titleAlpha,
                0f,
                titleOrigin,
                _titleScale,
                SpriteEffects.None,
                0f
            );
        }
        
        // Draw the "Tap Any" image at the bottom of the screen
        if (_tapAnyTexture != null && _animationComplete)
        {
            // Calculate the center position for the tap any image
            Vector2 tapAnyOrigin = new Vector2(_tapAnyTexture.Width / 2, _tapAnyTexture.Height / 2);
            Vector2 tapAnyPosition = new Vector2(_screenWidth / 2, _screenHeight - (_tapAnyTexture.Height / 4));
            
            // Draw with blinking effect
            spriteBatch.Draw(
                _tapAnyTexture,
                tapAnyPosition,
                null,
                Color.White * _tapAnyAlpha,
                0f,
                tapAnyOrigin,
                0.5f,
                SpriteEffects.None,
                0f
            );
        }
    }
    
    private void DrawMainMenu(SpriteBatch spriteBatch)
    {
        // Draw the main menu background - maintain aspect ratio and crop instead of stretching
        if (_mainMenuBackgroundTexture != null)
        {
            // Calculate the source rectangle to maintain aspect ratio
            Rectangle sourceRect;
            float screenAspect = (float)_screenWidth / _screenHeight;
            float textureAspect = (float)_mainMenuBackgroundTexture.Width / _mainMenuBackgroundTexture.Height;

            if (screenAspect > textureAspect)
            {
                // Screen is wider than texture - crop top and bottom
                int sourceHeight = (int)(_mainMenuBackgroundTexture.Width / screenAspect);
                int yOffset = (_mainMenuBackgroundTexture.Height - sourceHeight) / 2;
                sourceRect = new Rectangle(0, yOffset, _mainMenuBackgroundTexture.Width, sourceHeight);
            }
            else
            {
                // Screen is taller than texture - crop sides
                int sourceWidth = (int)(_mainMenuBackgroundTexture.Height * screenAspect);
                int xOffset = (_mainMenuBackgroundTexture.Width - sourceWidth) / 2;
                sourceRect = new Rectangle(xOffset, 0, sourceWidth, _mainMenuBackgroundTexture.Height);
            }

            // Draw the texture preserving aspect ratio
            spriteBatch.Draw(_mainMenuBackgroundTexture, new Rectangle(0, 0, _screenWidth, _screenHeight), sourceRect, Color.White);
        }
        
        // Draw the buttons - with darkening effect if they are being animated
        Color newGameColor = (_isButtonAnimating && _animatingButtonRect == _newGameButtonRect) ? Color.Gray : Color.White;
        Color loadGameColor = (_isButtonAnimating && _animatingButtonRect == _loadGameButtonRect) ? Color.Gray : Color.White;
        Color supportColor = (_isButtonAnimating && _animatingButtonRect == _supportButtonRect) ? Color.Gray : Color.White;
        Color customizeColor = (_isButtonAnimating && _animatingButtonRect == _customizeButtonRect) ? Color.Gray : Color.White;
        
        spriteBatch.Draw(_newGameButtonTexture, _newGameButtonRect, newGameColor);
        spriteBatch.Draw(_loadGameButtonTexture, _loadGameButtonRect, loadGameColor);
        spriteBatch.Draw(_supportButtonTexture, _supportButtonRect, supportColor);
        spriteBatch.Draw(_customizeButtonTexture, _customizeButtonRect, customizeColor);
    }
    
    private void DrawMapSelection(SpriteBatch spriteBatch)
    {
        // Draw the same background as main menu
        if (_mainMenuBackgroundTexture != null)
        {
            // Calculate the source rectangle to maintain aspect ratio
            Rectangle sourceRect;
            float screenAspect = (float)_screenWidth / _screenHeight;
            float textureAspect = (float)_mainMenuBackgroundTexture.Width / _mainMenuBackgroundTexture.Height;

            if (screenAspect > textureAspect)
            {
                // Screen is wider than texture - crop top and bottom
                int sourceHeight = (int)(_mainMenuBackgroundTexture.Width / screenAspect);
                int yOffset = (_mainMenuBackgroundTexture.Height - sourceHeight) / 2;
                sourceRect = new Rectangle(0, yOffset, _mainMenuBackgroundTexture.Width, sourceHeight);
            }
            else
            {
                // Screen is taller than texture - crop sides
                int sourceWidth = (int)(_mainMenuBackgroundTexture.Height * screenAspect);
                int xOffset = (_mainMenuBackgroundTexture.Width - sourceWidth) / 2;
                sourceRect = new Rectangle(xOffset, 0, sourceWidth, _mainMenuBackgroundTexture.Height);
            }

            // Draw the texture preserving aspect ratio
            spriteBatch.Draw(_mainMenuBackgroundTexture, new Rectangle(0, 0, _screenWidth, _screenHeight), sourceRect, Color.White);
        }
        
        // Draw title text "Select Map"
        string titleText = "SELECT MAP";
        Vector2 titlePosition = new Vector2(_screenWidth / 2, _screenHeight / 6);
        
        // Draw the map selection buttons - with darkening effect if they are being animated
        Color map1Color = (_isButtonAnimating && _animatingButtonRect == _map1ButtonRect) ? Color.Gray : Color.White;
        Color map2Color = (_isButtonAnimating && _animatingButtonRect == _map2ButtonRect) ? Color.Gray : Color.White;
        
        spriteBatch.Draw(_map1ButtonTexture, _map1ButtonRect, map1Color);
        spriteBatch.Draw(_map2ButtonTexture, _map2ButtonRect, map2Color);
    }
    
    // Helper method to get the screen manager from Game1
    private ScreenManager GetScreenManager()
    {
        // Access the screen manager directly using the property in Game1
        return (Game as Game1)?.ScreenManager;
    }
} 