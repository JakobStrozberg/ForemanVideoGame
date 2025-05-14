using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace src;

/// <summary>
/// Manages the overall game state and transitions
/// </summary>
public class GameManager
{
    private Game1 _game;
    
    // Current game state
    public enum GameState
    {
        MainMenu,
        Playing,
        Paused,
        GameOver
    }
    
    public GameState CurrentState { get; private set; } = GameState.MainMenu;
    
    public GameManager(Game1 game)
    {
        _game = game;
    }
    
    public void ChangeState(GameState newState)
    {
        CurrentState = newState;
        // Additional state transition logic can be added here
    }
    
    public void Update(GameTime gameTime)
    {
        // Update logic based on current state
        switch (CurrentState)
        {
            case GameState.MainMenu:
                UpdateMainMenu(gameTime);
                break;
            case GameState.Playing:
                UpdateGameplay(gameTime);
                break;
            case GameState.Paused:
                UpdatePaused(gameTime);
                break;
            case GameState.GameOver:
                UpdateGameOver(gameTime);
                break;
        }
    }
    
    public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        // Draw logic based on current state
        switch (CurrentState)
        {
            case GameState.MainMenu:
                DrawMainMenu(spriteBatch);
                break;
            case GameState.Playing:
                DrawGameplay(spriteBatch);
                break;
            case GameState.Paused:
                DrawPaused(spriteBatch);
                break;
            case GameState.GameOver:
                DrawGameOver(spriteBatch);
                break;
        }
    }
    
    private void UpdateMainMenu(GameTime gameTime)
    {
        // Menu update logic
    }
    
    private void UpdateGameplay(GameTime gameTime)
    {
        // Gameplay update logic
    }
    
    private void UpdatePaused(GameTime gameTime)
    {
        // Paused screen update logic
    }
    
    private void UpdateGameOver(GameTime gameTime)
    {
        // Game over screen update logic
    }
    
    private void DrawMainMenu(SpriteBatch spriteBatch)
    {
        // Menu drawing logic will be handled in Game1 for now
    }
    
    private void DrawGameplay(SpriteBatch spriteBatch)
    {
        // Gameplay drawing logic
    }
    
    private void DrawPaused(SpriteBatch spriteBatch)
    {
        // Paused screen drawing logic
    }
    
    private void DrawGameOver(SpriteBatch spriteBatch)
    {
        // Game over screen drawing logic
    }
} 