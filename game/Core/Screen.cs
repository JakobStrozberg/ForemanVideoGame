using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Crewboss.Core;

/// <summary>
/// Base class for all game screens (menu, gameplay, etc.)
/// </summary>
public abstract class Screen
{
    protected CrewbossGame Game { get; }
    
    public Screen(CrewbossGame game)
    {
        Game = game;
    }
    
    public abstract void LoadContent();

    /// <summary>Called every time this screen becomes the current one.</summary>
    public virtual void OnShown() { }

    public abstract void Update(GameTime gameTime);

    /// <summary>
    /// Runs before the main sprite batch begins — for render-target passes.
    /// </summary>
    public virtual void PreDraw(GameTime gameTime, SpriteBatch spriteBatch) { }

    public abstract void Draw(GameTime gameTime, SpriteBatch spriteBatch);
} 