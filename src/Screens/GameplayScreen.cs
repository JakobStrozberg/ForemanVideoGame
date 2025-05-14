using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;

namespace src.Screens;

public class GameplayScreen : Screen
{
    // Map properties
    private Texture2D _mapTexture;
    private Rectangle _mapBounds;
    private Vector2 _mapPosition;
    private float _mapScale = 1.0f;
    
    // ATV properties
    private Dictionary<string, Texture2D> _atvTextures;
    private Vector2 _atvPosition;
    private Vector2 _atvDirection;
    private Vector2 _atvVelocity = Vector2.Zero;
    private Vector2 _atvAcceleration = Vector2.Zero;
    private float _atvMaxSpeed = 350f; // Maximum speed in pixels per second - increased from 200f
    private float _atvAccelerationRate = 100f; // Acceleration in pixels per second^2 - increased from 50f
    private float _atvFriction = 0.97f; // Friction coefficient (1.0 = no friction)
    private float _atvSpeed = 100f; // pixels per second - reduced from 250f to make it much slower
    private string _currentAtvDirection = "N"; // Default facing north
    
    // Screen dimensions
    private int _screenWidth;
    private int _screenHeight;
    
    // Camera
    private Vector2 _cameraPosition;
    private float _cameraZoom = 2.5f; // Increased zoom factor from 1.5f to 2.5f - higher values = more zoomed in
    
    // Keyboard states
    private KeyboardState _currentKeyboardState;
    private KeyboardState _previousKeyboardState;
    
    // Map name
    private string _mapName;
    
    // New direction mapping
    private Dictionary<string, string> _directionMapping;
    
    public GameplayScreen(Game1 game, string mapName) : base(game)
    {
        _screenWidth = game.GraphicsDevice.Viewport.Width;
        _screenHeight = game.GraphicsDevice.Viewport.Height;
        _mapName = mapName;
        
        // Initialize ATV position to center of screen
        _atvPosition = new Vector2(_screenWidth / 2, _screenHeight / 2);
        _atvDirection = new Vector2(0, -1); // Default facing north
        _cameraPosition = new Vector2(0, 0);
        
        // Initialize ATV textures dictionary
        _atvTextures = new Dictionary<string, Texture2D>();
    }
    
    public override void LoadContent()
    {
        try
        {
            // Load map texture based on map name
            string mapPath = string.Empty;
            if (_mapName == "Map1")
            {
                // Using the path to Map2 folder as requested in the original user query
                mapPath = "/Users/jakobstrozberg/Desktop/CrewbossGame/src/Content/GameTextures/Maps/Map2/Map2";
                Console.WriteLine($"Using map path for Map1: {mapPath}");
            }
            else if (_mapName == "Map2")
            {
                // Using the path to Map1.jpeg as specified in the latest request
                // Note: Fixed to use lowercase "map1.jpeg" as seen in the directory listing
                mapPath = "/Users/jakobstrozberg/Desktop/CrewbossGame/src/Content/GameTextures/Maps/Map1/map1";
                Console.WriteLine($"Using map path for Map2 button: {mapPath}");
            }
            
            // Try loading the map via Content pipeline first
            try
            {
                _mapTexture = Game.Content.Load<Texture2D>($"GameTextures/Maps/{_mapName}/{_mapName}");
                Console.WriteLine($"Successfully loaded {_mapName} via content pipeline");
            }
            catch
            {
                // If that fails, try loading from direct file path with different extensions
                bool loaded = false;
                
                // Try JPEG first
                if (File.Exists(mapPath + ".jpeg"))
                {
                    using (FileStream fileStream = new FileStream(mapPath + ".jpeg", FileMode.Open))
                    {
                        _mapTexture = Texture2D.FromStream(Game.GraphicsDevice, fileStream);
                        loaded = true;
                        Console.WriteLine($"Loaded {_mapName} from direct file path (JPEG)");
                    }
                }
                // Then try JPG
                else if (File.Exists(mapPath + ".jpg"))
                {
                    using (FileStream fileStream = new FileStream(mapPath + ".jpg", FileMode.Open))
                    {
                        _mapTexture = Texture2D.FromStream(Game.GraphicsDevice, fileStream);
                        loaded = true;
                        Console.WriteLine($"Loaded {_mapName} from direct file path (JPG)");
                    }
                }
                // Finally try PNG
                else if (File.Exists(mapPath + ".png"))
                {
                    using (FileStream fileStream = new FileStream(mapPath + ".png", FileMode.Open))
                    {
                        _mapTexture = Texture2D.FromStream(Game.GraphicsDevice, fileStream);
                        loaded = true;
                        Console.WriteLine($"Loaded {_mapName} from direct file path (PNG)");
                    }
                }
                
                if (!loaded)
                {
                    // Create a placeholder texture if map can't be loaded
                    _mapTexture = new Texture2D(Game.GraphicsDevice, 1000, 1000);
                    Color[] mapData = new Color[1000 * 1000];
                    for (int i = 0; i < mapData.Length; i++)
                    {
                        mapData[i] = Color.Green; // Green placeholder map
                    }
                    _mapTexture.SetData(mapData);
                    Console.WriteLine("Created placeholder map texture");
                }
            }
            
            // Set map bounds
            _mapBounds = new Rectangle(0, 0, _mapTexture.Width, _mapTexture.Height);
            
            // Load ATV textures (all 16 directions)
            LoadAtvTextures();
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error in GameplayScreen.LoadContent: {e.Message}");
        }
    }
    
    private void LoadAtvTextures()
    {
        try
        {
            // Updated direction names to match the actual filenames in the directory
            string[] directions = { "N", "NNE", "NE", "NEE", "E", "SEE", "SE", "SSE", "S", "SSW", "SW", "SWW", "W", "NWW", "NW", "NNW" };
            
            foreach (string direction in directions)
            {
                try
                {
                    // Try content pipeline first
                    string textureName = $"GameTextures/TransParentQuadPositions/Quad_{direction}";
                    _atvTextures[direction] = Game.Content.Load<Texture2D>(textureName);
                }
                catch
                {
                    // If that fails, try direct file path
                    string filePath = $"/Users/jakobstrozberg/Desktop/CrewbossGame/src/Content/GameTextures/TransParentQuadPositions/Quad_{direction}.png";
                    try
                    {
                        using (FileStream fileStream = new FileStream(filePath, FileMode.Open))
                        {
                            _atvTextures[direction] = Texture2D.FromStream(Game.GraphicsDevice, fileStream);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to load ATV texture {direction}: {e.Message}");
                        // Create placeholder texture
                        _atvTextures[direction] = CreatePlaceholderAtvTexture(direction);
                    }
                }
            }
            
            // Create mapping from 16-point compass directions to our file naming convention
            _directionMapping = new Dictionary<string, string>
            {
                { "N", "N" },
                { "NNE", "NNE" },
                { "NE", "NE" },
                { "ENE", "NEE" },
                { "E", "E" },
                { "ESE", "SEE" },
                { "SE", "SE" },
                { "SSE", "SSE" },
                { "S", "S" },
                { "SSW", "SSW" },
                { "SW", "SW" },
                { "WSW", "SWW" },
                { "W", "W" },
                { "WNW", "NWW" },
                { "NW", "NW" },
                { "NNW", "NNW" }
            };
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error loading ATV textures: {e.Message}");
        }
    }
    
    private Texture2D CreatePlaceholderAtvTexture(string direction)
    {
        // Create a simple colored triangle pointing in the appropriate direction
        Texture2D texture = new Texture2D(Game.GraphicsDevice, 50, 50);
        Color[] colorData = new Color[50 * 50];
        
        // Fill with transparent first
        for (int i = 0; i < colorData.Length; i++)
        {
            colorData[i] = Color.Transparent;
        }
        
        // Draw a simple colored triangle
        for (int y = 0; y < 50; y++)
        {
            for (int x = 0; x < 50; x++)
            {
                // Simple triangle pointing up for North, etc.
                if (direction.Contains("N") && y < 25 && x > y && x < 50 - y)
                {
                    colorData[y * 50 + x] = Color.Red;
                }
                else if (direction.Contains("S") && y >= 25 && x > 50 - y && x < y)
                {
                    colorData[y * 50 + x] = Color.Red;
                }
                else if (direction.Contains("E") && x >= 25 && y > 50 - x && y < x)
                {
                    colorData[y * 50 + x] = Color.Red;
                }
                else if (direction.Contains("W") && x < 25 && y > x && y < 50 - x)
                {
                    colorData[y * 50 + x] = Color.Red;
                }
            }
        }
        
        texture.SetData(colorData);
        return texture;
    }
    
    public override void Update(GameTime gameTime)
    {
        // Update keyboard state
        _previousKeyboardState = _currentKeyboardState;
        _currentKeyboardState = Keyboard.GetState();
        
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        
        // Check for reset key (R)
        if (_currentKeyboardState.IsKeyDown(Keys.R) && _previousKeyboardState.IsKeyUp(Keys.R))
        {
            ResetAtvPosition();
        }
        
        // Handle ATV movement
        HandleAtvMovement(deltaTime);
        
        // Update camera to follow ATV
        UpdateCamera();
    }
    
    private void HandleAtvMovement(float deltaTime)
    {
        // Reset acceleration each frame
        _atvAcceleration = Vector2.Zero;
        
        // Calculate acceleration vector based on arrow key input
        if (_currentKeyboardState.IsKeyDown(Keys.Up) || _currentKeyboardState.IsKeyDown(Keys.W))
            _atvAcceleration.Y -= _atvAccelerationRate;
        if (_currentKeyboardState.IsKeyDown(Keys.Down) || _currentKeyboardState.IsKeyDown(Keys.S))
            _atvAcceleration.Y += _atvAccelerationRate;
        if (_currentKeyboardState.IsKeyDown(Keys.Left) || _currentKeyboardState.IsKeyDown(Keys.A))
            _atvAcceleration.X -= _atvAccelerationRate;
        if (_currentKeyboardState.IsKeyDown(Keys.Right) || _currentKeyboardState.IsKeyDown(Keys.D))
            _atvAcceleration.X += _atvAccelerationRate;
        
        // Apply acceleration to velocity
        _atvVelocity += _atvAcceleration * deltaTime;
        
        // Apply friction to gradually slow down when not accelerating
        _atvVelocity *= _atvFriction;
        
        // Limit to maximum speed
        if (_atvVelocity.LengthSquared() > _atvMaxSpeed * _atvMaxSpeed)
        {
            _atvVelocity.Normalize();
            _atvVelocity *= _atvMaxSpeed;
        }
        
        // Update position based on velocity
        _atvPosition += _atvVelocity * deltaTime;
        
        // Clamp position to map bounds
        _atvPosition.X = MathHelper.Clamp(_atvPosition.X, 0, _mapBounds.Width);
        _atvPosition.Y = MathHelper.Clamp(_atvPosition.Y, 0, _mapBounds.Height);
        
        // Update ATV direction texture only if we have some significant velocity
        if (_atvVelocity.LengthSquared() > 1.0f)
        {
            _atvDirection = Vector2.Normalize(_atvVelocity);
            UpdateAtvDirectionTexture();
        }
        
        // Debug output showing current speed
        if ((_currentKeyboardState.IsKeyDown(Keys.D) && _previousKeyboardState.IsKeyUp(Keys.D)) ||
            (_currentKeyboardState.IsKeyDown(Keys.LeftShift) && _previousKeyboardState.IsKeyUp(Keys.LeftShift)))
        {
            float currentSpeed = _atvVelocity.Length();
            Console.WriteLine($"Current speed: {currentSpeed:F1} pixels/sec");
        }
    }
    
    private void UpdateAtvDirectionTexture()
    {
        // Calculate angle in radians (0 is to the right, increases clockwise)
        float angle = MathF.Atan2(_atvDirection.Y, _atvDirection.X);
        
        // Convert to degrees and adjust so 0 is up, increases clockwise
        float degrees = MathHelper.ToDegrees(angle);
        degrees = (degrees + 90) % 360;
        if (degrees < 0) degrees += 360;
        
        // Map degrees to 16-point compass direction
        // Each direction covers 22.5 degrees (360 / 16)
        int direction = (int)Math.Round(degrees / 22.5) % 16;
        
        // Map direction index to direction string
        string[] compassDirections = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
        string compassDirection = compassDirections[direction];
        
        // Map compass direction to file naming convention
        if (_directionMapping != null && _directionMapping.ContainsKey(compassDirection))
        {
            _currentAtvDirection = _directionMapping[compassDirection];
        }
        else
        {
            // Fallback to compass direction if mapping fails
            _currentAtvDirection = compassDirection;
        }
    }
    
    private void UpdateCamera()
    {
        // Center camera on ATV, offset by half screen dimensions
        // Adjusted to account for zoom factor
        _cameraPosition.X = _atvPosition.X - (_screenWidth / (2 * _cameraZoom));
        _cameraPosition.Y = _atvPosition.Y - (_screenHeight / (2 * _cameraZoom));
        
        // Clamp camera to map bounds
        float maxX = _mapBounds.Width - (_screenWidth / _cameraZoom);
        float maxY = _mapBounds.Height - (_screenHeight / _cameraZoom);
        _cameraPosition.X = MathHelper.Clamp(_cameraPosition.X, 0, maxX);
        _cameraPosition.Y = MathHelper.Clamp(_cameraPosition.Y, 0, maxY);
    }
    
    private void ResetAtvPosition()
    {
        // Reset to center of the screen or a specific position on the map
        _atvPosition = new Vector2(_mapBounds.Width / 2, _mapBounds.Height / 2);
        _atvVelocity = Vector2.Zero;
        _atvAcceleration = Vector2.Zero;
        _atvDirection = new Vector2(0, -1); // Default facing north
        _currentAtvDirection = "N";
        Console.WriteLine("ATV position reset");
    }
    
    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        // Draw map
        Rectangle mapRect = new Rectangle(
            0, 0, 
            _screenWidth, _screenHeight);
        
        Rectangle mapSourceRect = new Rectangle(
            (int)_cameraPosition.X, (int)_cameraPosition.Y, 
            (int)(_screenWidth / _cameraZoom), (int)(_screenHeight / _cameraZoom));
        
        spriteBatch.Draw(_mapTexture, mapRect, mapSourceRect, Color.White);
        
        // Draw ATV - centered on screen when camera is following
        if (_atvTextures.ContainsKey(_currentAtvDirection))
        {
            Texture2D atvTexture = _atvTextures[_currentAtvDirection];
            
            // Adjusted ATV drawing position to account for zoom
            Vector2 atvDrawPosition = new Vector2(
                (_atvPosition.X - _cameraPosition.X) * _cameraZoom,
                (_atvPosition.Y - _cameraPosition.Y) * _cameraZoom);
            
            // Center the ATV texture on its position
            Vector2 atvOrigin = new Vector2(atvTexture.Width / 2, atvTexture.Height / 2);
            
            spriteBatch.Draw(
                atvTexture,
                atvDrawPosition,
                null,
                Color.White,
                0f,
                atvOrigin,
                0.15f, // Increased scale slightly from 0.1f to 0.15f
                SpriteEffects.None,
                0f);
        }
        
        // Draw speedometer
        DrawSpeedometer(spriteBatch);
    }
    
    private void DrawSpeedometer(SpriteBatch spriteBatch)
    {
        // Calculate current speed as percentage of max speed
        float currentSpeed = _atvVelocity.Length();
        float speedPercent = currentSpeed / _atvMaxSpeed;
        
        // Draw background bar
        Rectangle speedometerBg = new Rectangle(
            10, 10,  // Position (top left)
            150, 20  // Size
        );
        
        // Draw filled bar based on speed
        Rectangle speedometerFill = new Rectangle(
            10, 10,  // Position (top left)
            (int)(150 * speedPercent), 20  // Size - width scaled by speed percentage
        );
        
        // Draw the speedometer
        spriteBatch.Draw(GetOrCreateTexture("speedometerBg", Color.DarkGray), speedometerBg, Color.White);
        spriteBatch.Draw(GetOrCreateTexture("speedometerFill", Color.Green), speedometerFill, Color.White);
    }
    
    private Texture2D GetOrCreateTexture(string textureName, Color color)
    {
        // A simple helper to create solid color textures for UI elements
        Texture2D texture = new Texture2D(Game.GraphicsDevice, 1, 1);
        texture.SetData(new[] { color });
        return texture;
    }
} 