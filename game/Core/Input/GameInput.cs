using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Crewboss.Core;

/// <summary>
/// The one place raw devices become game intents. Mechanics read Throttle,
/// Steer, Mount... never Keys. Touch controls for the mobile heads plug in
/// here later by OR-ing into the same intents.
/// </summary>
public sealed class GameInput
{
    private KeyboardState _cur, _prev;
    private MouseState _mouse, _mousePrev;

    public void Update()
    {
        _prev = _cur;
        _cur = Keyboard.GetState();
        _mousePrev = _mouse;
        _mouse = Mouse.GetState();
    }

    private bool Down(Keys a, Keys b) => _cur.IsKeyDown(a) || _cur.IsKeyDown(b);
    private bool Down(Keys a, Keys b, Keys c) => _cur.IsKeyDown(a) || _cur.IsKeyDown(b) || _cur.IsKeyDown(c);
    private bool Pressed(Keys k) => _cur.IsKeyDown(k) && _prev.IsKeyUp(k);
    private bool Pressed(Keys a, Keys b) => Pressed(a) || Pressed(b);
    private bool Pressed(Keys a, Keys b, Keys c) => Pressed(a) || Pressed(b) || Pressed(c);

    // ---- driving (held) ----
    public bool Throttle => Down(Keys.Up, Keys.W);
    public bool Brake => Down(Keys.Down, Keys.S, Keys.Space);
    public bool Drift => Down(Keys.LeftShift, Keys.RightShift);
    public float Steer => (Down(Keys.Left, Keys.A) ? -1f : 0f) + (Down(Keys.Right, Keys.D) ? 1f : 0f);
    public bool GearUp => Pressed(Keys.X);
    public bool GearDown => Pressed(Keys.Z, Keys.LeftControl, Keys.RightControl);

    // ---- on foot (held) ----
    public Vector2 WalkDir
    {
        get
        {
            var d = Vector2.Zero;
            if (Down(Keys.Up, Keys.W)) d.Y -= 1;
            if (Down(Keys.Down, Keys.S)) d.Y += 1;
            if (Down(Keys.Left, Keys.A)) d.X -= 1;
            if (Down(Keys.Right, Keys.D)) d.X += 1;
            return d;
        }
    }

    // ---- context actions (edge) ----
    public bool Mount => Pressed(Keys.E);
    public bool EngineKey => Pressed(Keys.K);
    public bool BoxAction => Pressed(Keys.Q);
    public bool Crew => Pressed(Keys.F);
    public bool LineIn => Pressed(Keys.C);
    public bool Coach => Pressed(Keys.T);
    public bool DropFlag => Pressed(Keys.G);

    // ---- line-in aiming ----
    public bool AimLeft => Down(Keys.A, Keys.Left);
    public bool AimRight => Down(Keys.D, Keys.Right);
    public bool AimConfirm => Pressed(Keys.C, Keys.E);
    public bool AimCancel => Pressed(Keys.Q, Keys.F);

    // ---- menus ----
    public bool Pause => Pressed(Keys.Escape);
    public bool MenuUp => Pressed(Keys.Up, Keys.W);
    public bool MenuDown => Pressed(Keys.Down, Keys.S);
    public bool MenuSelect => Pressed(Keys.Enter, Keys.Space);
    public bool MenuLeft => Pressed(Keys.Left, Keys.A);
    public bool MenuRight => Pressed(Keys.Right, Keys.D);
    public Point MousePos => new(_mouse.X, _mouse.Y);
    public bool MouseClicked => _mouse.LeftButton == ButtonState.Pressed && _mousePrev.LeftButton == ButtonState.Released;

    // ---- meta ----
    public bool Reset => Pressed(Keys.R);
    public bool Restart => Pressed(Keys.F5);
    public bool ToggleHelp => Pressed(Keys.H);
    public bool ToggleCutSide => Pressed(Keys.Tab);
    public bool ToggleDebug => Pressed(Keys.F3);
    public bool ZoomIn => Pressed(Keys.OemPlus, Keys.Add);
    public bool ZoomOut => Pressed(Keys.OemMinus, Keys.Subtract);
    public bool AnyKeyPressed => _cur.GetPressedKeys().Length > 0 && _prev.GetPressedKeys().Length == 0;
}
