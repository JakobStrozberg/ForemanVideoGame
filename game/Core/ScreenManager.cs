using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Crewboss.Core;

public class ScreenManager
{
    private CrewbossGame _game;
    private Screen _currentScreen;
    private Dictionary<string, Screen> _screens;
    private readonly HashSet<Screen> _loaded = new();
    
    public ScreenManager(CrewbossGame game)
    {
        _game = game;
        _screens = new Dictionary<string, Screen>();
    }
    
    public void Initialize()
    {
        // Create and register all screens
        RegisterScreen("MainMenu", new MainMenuScreen(_game));
        
        // Set the initial screen to main menu
        ChangeScreen("MainMenu");
    }
    
    public void LoadContent()
    {
        // Load content for initial screen only (skip if ChangeScreen already did)
        if (_currentScreen != null && _loaded.Add(_currentScreen))
        {
            _currentScreen.LoadContent();
        }
    }
    
    public void RegisterScreen(string screenName, Screen screen)
    {
        if (_screens.ContainsKey(screenName))
        {
            // Replace existing screen
            _screens[screenName] = screen;
        }
        else
        {
            // Add new screen
            _screens.Add(screenName, screen);
        }
    }
    
    public void ChangeScreen(string screenName)
    {
        if (_screens.ContainsKey(screenName))
        {
            Screen newScreen = _screens[screenName];
            
            // Load content for the new screen if it hasn't been loaded yet
            if (newScreen != _currentScreen && _loaded.Add(newScreen))
            {
                try
                {
                    newScreen.LoadContent();
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading content for screen {screenName}: {e.Message}");
                }
            }
            
            _currentScreen = newScreen;
            System.Diagnostics.Debug.WriteLine($"Changed to screen: {screenName}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Screen {screenName} not found");
        }
    }
    
    public void Update(GameTime gameTime)
    {
        _currentScreen?.Update(gameTime);
    }
    
    public void PreDraw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        _currentScreen?.PreDraw(gameTime, spriteBatch);
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        _currentScreen?.Draw(gameTime, spriteBatch);
    }
} 