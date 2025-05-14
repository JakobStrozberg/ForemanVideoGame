using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace src.Screens;

/// <summary>
/// Base class for all game screens (menu, gameplay, etc.)
/// </summary>
public abstract class Screen
{
    protected Game1 Game { get; }
    
    public Screen(Game1 game)
    {
        Game = game;
    }
    
    public abstract void LoadContent();
    
    public abstract void Update(GameTime gameTime);
    
    public abstract void Draw(GameTime gameTime, SpriteBatch spriteBatch);
} 