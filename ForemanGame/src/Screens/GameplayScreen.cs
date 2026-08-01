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
    
    // ATV properties. DRIVING THE QUAD SHOULD BE FUN: quick off the line,
    // gears 1-5 for top speed, rough ground bounces you around.
    private Dictionary<string, Texture2D> _atvTextures;
    private Vector2 _atvPosition;
    private Vector2 _atvDirection;
    private Vector2 _atvVelocity = Vector2.Zero;
    private Vector2 _atvAcceleration = Vector2.Zero;
    private float _atvFriction = 0.97f; // Friction coefficient (1.0 = no friction)
    private string _currentAtvDirection = "N"; // 16-dir name (legacy texture fallback)
    private int _atvDirIdx = 0;                // 32-dir index into the generated atlas
    private int _parkedAtvDirIdx = 8;

    // gearbox: Shift = up, Ctrl = down. Shifting takes real time — clutch in,
    // no throttle until the new gear engages.
    private int _gear = 1;
    private int _pendingGear = -1;
    private float _shiftTimer;
    // The gear ladder (index 0 = Reverse):
    //   R  = G1 but backwards
    //   G1 = crawler: really slow, full torque — moves anywhere, even slop
    //   G2 = a bit faster, still strong
    //   G3 = the midrange workhorse — best all-round on the block
    //   G4 = fastest you'll normally go; low torque, wants decent ground
    //   G5 = highway gear — only pays off on the road to the block
    // Torque = how well the gear powers through rough terrain: it protects
    // acceleration AND keeps drag low on bad ground. Low-torque gears bog down.
    private static readonly float[] GearMax = { 95, 95, 150, 205, 280, 380 };
    private static readonly float[] GearAccel = { 320, 320, 250, 240, 190, 165 };
    private static readonly float[] GearTorque = { 1f, 1f, 0.60f, 0.52f, 0.35f, 0.20f };

    // advances with travel; seeds the dust-puff hash
    private float _bounceTime;

    // dust plume in the quad's wake — only at real speed, dense enough to read
    // as one continuous cloud
    private struct Dust { public Vector2 Pos, Vel; public float Age, Life, Size; public bool Smoke; }
    private readonly List<Dust> _dust = new();
    private Texture2D _dustTexture;
    private float _dustSpawnDist;
    private float _smokeSpawnDist; // road smoke: separate accumulator, lower threshold
    private float _quadTilt;       // smoothed sprite lean from ground slope (radians)

    /// <summary>Position-keyed micro-bump height (world px, unscaled by terrain roughness).</summary>
    private static float BumpAt(float x, float y) =>
        (MathF.Sin(x * 0.10f + y * 0.06f) * 0.6f
       + MathF.Sin(x * 0.031f + y * 0.113f) * 0.4f) * 2.4f;

    /// <summary>Terrain roughness multiplier for micro-bumps at a position.</summary>
    private float BumpRoughAt(Vector2 pos) =>
        _tileMap == null ? 0.6f : _tileMap.TerrainAtWorld(pos).Name switch
        {
            "rock" => 1.4f,
            "slash" => 1.0f,
            "swamp" => 0.8f,
            "cream" => 0.5f,
            "trail" => 0.3f,
            "road" => 0.15f,
            _ => 0.6f
        };
    private Vector2 _prevQuadPos;

    // tire tracks: two parallel tread marks stamped along the path, oriented
    // with travel. FIFO-capped so the oldest tracks fade away.
    private struct TrackStamp { public Vector2 Pos; public float Ang; }
    private readonly List<TrackStamp> _tracks = new();
    private Texture2D _trackTexture;
    private float _trackDist;
    private const int MAX_TRACKS = 4000;
    
    // Screen dimensions
    private int _screenWidth;
    private int _screenHeight;
    
    // Camera
    private Vector2 _cameraPosition;
    private float _cameraZoom = Tweaks.CameraZoom;

    // Anti-nausea rig: the scene renders 1:1 into an offscreen target sized
    // screen/zoom (integer camera, no fractional world scaling), then one
    // linear upscale presents it — a single filter instead of thousands of
    // per-sprite sub-pixel resamplings (the full-screen shimmer).
    private int _outWidth, _outHeight;
    private RenderTarget2D _sceneTarget;
    // Screen-anchored overlays (HUD, band shade) render here and present
    // WITHOUT the sub-pixel shift — pinned UI never swims with the camera
    private RenderTarget2D _uiTarget;
    private Vector2 _camSmooth;
    // Sub-pixel camera remainder, applied as an offset at the present upscale:
    // the world renders on an integer grid, but the frame as a whole glides —
    // no more whole-pixel stepping (the "drive jitter").
    private Vector2 _camFrac;
    private bool _camInit;
    private float _presentZoom = Tweaks.CameraZoom;
    private bool _showHelp = true;

    /// <summary>Controls card, bottom-left. H toggles. Font charset is
    /// 0-9 A-Z : ! - . / — keep the text inside it.</summary>
    private void DrawControls(SpriteBatch spriteBatch)
    {
        if (!_showHelp || _font == null) return;
        string[] lines =
        {
            "DRIVE: WASD/ARROWS",
            "GAS: UP  BRAKE: DOWN/SPACE",
            "DRIFT: HOLD SHIFT",
            "GEARS: X/Z",
            "E: MOUNT  Q: BOXES",
            "F: CREW  C: LINE-IN",
            "T: COACH  R: RESET",
            "ZOOM: PLUS/MINUS",
            "F5: RESTART  H: HIDE",
        };
        const float fs = 1.6f;
        int lineH = (int)(7 * fs);
        int w = 0;
        foreach (var l in lines) w = Math.Max(w, (int)BitmapFont.Measure(l, fs));
        int h = lines.Length * lineH + 12;
        int x = 8, y = _screenHeight - h - 8;
        spriteBatch.Draw(GetOrCreateTexture("helpBg", new Color(0, 0, 0)),
            new Rectangle(x - 4, y - 4, w + 16, h + 8), Color.White * 0.55f);
        for (int i = 0; i < lines.Length; i++)
            _font.Draw(spriteBatch, lines[i], new Vector2(x, y + i * lineH), fs,
                new Color(230, 230, 210));
    }

    /// <summary>Skip the block-overview intro — straight into the day.</summary>
    public void SkipIntro() => _preGame = false;

    /// <summary>
    /// Runtime zoom: change the presentation scale and rebuild everything
    /// sized off the logical resolution. World math stays 1:1 — zoom only
    /// changes how much world fits on screen.
    /// </summary>
    private void ApplyZoom(float z)
    {
        _presentZoom = MathHelper.Clamp(z, 0.8f, 2.5f);
        // +1 overscan: the present pass shifts the frame by up to one logical
        // pixel (sub-pixel camera), so render one extra column/row
        _screenWidth = (int)MathF.Ceiling(_outWidth / _presentZoom) + 1;
        _screenHeight = (int)MathF.Ceiling(_outHeight / _presentZoom) + 1;
        _sceneTarget?.Dispose();
        _sceneTarget = null;
        _uiTarget?.Dispose();
        _uiTarget = null;
        _camInit = false;
    }
    
    // Keyboard states
    private KeyboardState _currentKeyboardState;
    private KeyboardState _previousKeyboardState;
    
    // Map name
    private string _mapName;

    // Terrain data layer (generated alongside map art by tools/ArtTool)
    private TileMap _tileMap;

    // HUD baseline: slim strip along the top of the screen
    private const int HudBase = 38;

    // Trees draw at runtime (not baked into the map): y-sorted against the ATV
    private TreeLayer _treeLayer;

    // Debris obstacles (logs, stumps): rendered like trees, but the QUAD collides
    // with them — people just climb over. Collision = circles per object.
    private TreeLayer _debrisLayer;
    private TreeLayer _vegLayer; // grass + bushes: no collision, just life
    private readonly List<(Vector2 c, float r)> _debrisCircles = new();

    // suspension: the chassis chases ground elevation on a spring — crest a rise
    // at speed and the quad floats (shadow shows the air), dips compress
    private float _chassisLift;
    private float _chassisVel;

    // Avatar: mounted on the quad or on foot. The quad stays parked where you
    // leave it. On foot you're slower but can push through bush and swamp.
    private bool _mounted = true;
    private Vector2 _parkedAtvPos;
    private string _parkedAtvDir = "S";
    private string _footDir = "S";
    private float _walkAnim;
    private bool _walking;
    private const float INTERACT_RANGE = 55f;

    // Boxes + caches. Caches are player-placed at runtime (Q drops a box).
    private bool _carryingBox;
    private int _atvBoxes;
    private const int ATV_BOX_CAP = 8; // 6 on the rear rack (3x2) + 2 up front
    private readonly List<CacheEntity> _caches = new();
    private List<Vector2> _truckCenters = new();

    private enum BoxAction { None, LoadFromTruck, AddToCache, TakeFromAtv, LoadAtvFromHands, PlaceCache }

    // Planter crew (F = pick up / drop crew members)
    private PlanterSystem _planters;

    // Line-in aiming: C at a cache -> arrow from the cache, A/D rotates,
    // C confirms, Q cancels. The chosen planter marches the bearing solo.
    private bool _aiming;
    private float _aimAngle;
    private CacheEntity _aimCache;
    private Planter _aimPlanter;

    // Generated sprites (foreman, planters, seedlings, prompt badges, cache tent)
    private Texture2D _foremanAtlas, _planterAtlas, _seedlingAtlas;
    private Texture2D _badgeE, _badgeQ, _badgeC, _badgeAlert, _badgeDone, _cacheTexture;
    private Rectangle[] _foremanFrames, _planterFrames, _seedlingFrames;

    // Generated quad atlas: 16 directions x (parked, with rider)
    private Texture2D _quadAtlas;
    private Rectangle[] _quadFrames;
    private static readonly string[] QuadDirOrder =
        { "N", "NNE", "NE", "NEE", "E", "SEE", "SE", "SSE", "S", "SSW", "SW", "SWW", "W", "NWW", "NW", "NNW" };
    private const int WALK_FRAMES = 4;      // contact-pass-contact-pass
    private const int PLANTER_FRAMES = 13;  // 3 dirs x 4 walk frames + planting pose


    // Audio
    private AudioSystem _audio;
    private bool _drifting;
    private int _lastTreeCount;
    private float _plantSfxCooldown;

    // Text + day loop
    private BitmapFont _font;
    private Texture2D _badgeT;
    private const float DAY_SECONDS = 480f; // one block day
    private float _dayRemaining = DAY_SECONDS;
    private bool _dayOver;
    private bool _preGame = true;

    // walk-the-line quality reveals: (tileX, tileY, spot) -> seconds left to show
    private readonly Dictionary<(int, int, int), float> _reveals = new();

    // Cached solid-color textures for UI elements
    private readonly Dictionary<string, Texture2D> _uiTextures = new();

    // New direction mapping
    private Dictionary<string, string> _directionMapping;
    
    public GameplayScreen(Game1 game, string mapName) : base(game)
    {
        _outWidth = game.GraphicsDevice.Viewport.Width;
        _outHeight = game.GraphicsDevice.Viewport.Height;
        // logical resolution: world renders 1:1 here, presented at _presentZoom
        // (+1 overscan for the sub-pixel present shift)
        _screenWidth = (int)MathF.Ceiling(_outWidth / _presentZoom) + 1;
        _screenHeight = (int)MathF.Ceiling(_outHeight / _presentZoom) + 1;
        _cameraZoom = 1f;
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
        Tweaks.Load();
        try
        {
            // Generated blocks (map art + tile data built by tools/ArtTool) take
            // priority; legacy hand-made map art is the fallback.
            string texRoot = Path.Combine(Tweaks.ContentRoot(), "GameTextures");
            string generatedDir = Path.Combine(texRoot, "Maps", "Generated");
            string blockName = _mapName == "Map1" ? "Block1" : "Block2";
            string blockPng = Path.Combine(generatedDir, $"{blockName}.png");
            string tilesJson = Path.Combine(generatedDir, $"{blockName}.tiles.json");

            if (File.Exists(blockPng))
            {
                using (FileStream fileStream = new FileStream(blockPng, FileMode.Open, FileAccess.Read))
                {
                    _mapTexture = Texture2D.FromStream(Game.GraphicsDevice, fileStream);
                }
                Console.WriteLine($"Loaded generated block {blockName}");

                if (File.Exists(tilesJson))
                {
                    _tileMap = TileMap.Load(tilesJson);
                    _atvPosition = _tileMap.FindSpawn();
                    Console.WriteLine($"Loaded tile data {_tileMap.Width}x{_tileMap.Height} @ {_tileMap.TileSize}px, spawn {_atvPosition}");
                }
            }
            else
            {
                // Legacy maps via content pipeline, then direct file, then placeholder
                try
                {
                    _mapTexture = Game.Content.Load<Texture2D>($"GameTextures/Maps/{_mapName}/{_mapName}");
                    Console.WriteLine($"Loaded {_mapName} via content pipeline");
                }
                catch
                {
                    string mapDir = Path.Combine(texRoot, "Maps", _mapName);
                    string loadedFrom = null;
                    if (Directory.Exists(mapDir))
                    {
                        foreach (string file in Directory.EnumerateFiles(mapDir))
                        {
                            string name = Path.GetFileNameWithoutExtension(file);
                            string ext = Path.GetExtension(file).ToLowerInvariant();
                            if (!name.Equals(_mapName, StringComparison.OrdinalIgnoreCase)) continue;
                            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;
                            using (FileStream fileStream = new FileStream(file, FileMode.Open, FileAccess.Read))
                            {
                                _mapTexture = Texture2D.FromStream(Game.GraphicsDevice, fileStream);
                            }
                            loadedFrom = file;
                            break;
                        }
                    }

                    if (loadedFrom != null)
                    {
                        Console.WriteLine($"Loaded {_mapName} from {loadedFrom}");
                    }
                    else
                    {
                        _mapTexture = new Texture2D(Game.GraphicsDevice, 1000, 1000);
                        Color[] mapData = new Color[1000 * 1000];
                        for (int i = 0; i < mapData.Length; i++)
                        {
                            mapData[i] = Color.Green;
                        }
                        _mapTexture.SetData(mapData);
                        Console.WriteLine("Created placeholder map texture");
                    }
                }
            }

            // Set map bounds: logical world size (the PNG carries extra transparent
            // top padding for terrain relief, so texture height != world height)
            _mapBounds = _tileMap != null
                ? new Rectangle(0, 0, _tileMap.Width * _tileMap.TileSize, _tileMap.Height * _tileMap.TileHeight)
                : new Rectangle(0, 0, _mapTexture.Width, _mapTexture.Height);

            // Runtime tree layer (atlas + per-block positions)
            _treeLayer = TreeLayer.Load(Game.GraphicsDevice,
                Path.Combine(texRoot, "Generated", "TreeAtlas.png"),
                Path.Combine(texRoot, "Generated", "TreeAtlas.json"),
                Path.Combine(generatedDir, $"{blockName}.trees.json"));
            Console.WriteLine(_treeLayer != null ? "Loaded tree layer" : "No tree layer found");

            // Debris obstacles (same file shape as the tree layer)
            _debrisLayer = TreeLayer.Load(Game.GraphicsDevice,
                Path.Combine(texRoot, "Generated", "DebrisAtlas.png"),
                Path.Combine(texRoot, "Generated", "DebrisAtlas.json"),
                Path.Combine(generatedDir, $"{blockName}.debris.json"));
            if (_debrisLayer != null)
            {
                foreach (var (dx, dy, dv) in _debrisLayer.InRange(float.MinValue, float.MaxValue))
                {
                    var p = new Vector2(dx, dy);
                    if (dv < 6)
                    {
                        // log: three circles along its angle (y squashed like the sprite)
                        float ang = dv * 30f * MathF.PI / 180f;
                        var off = new Vector2(MathF.Cos(ang), MathF.Sin(ang) * 0.7f) * 13f;
                        _debrisCircles.Add((p - off, 7f));
                        _debrisCircles.Add((p, 7f));
                        _debrisCircles.Add((p + off, 7f));
                    }
                    else
                    {
                        _debrisCircles.Add((p, 6f));
                    }
                }
                Console.WriteLine($"Loaded debris: {_debrisCircles.Count} collision circles");
            }

            // Vegetation (grass tufts + bushes)
            _vegLayer = TreeLayer.Load(Game.GraphicsDevice,
                Path.Combine(texRoot, "Generated", "VegAtlas.png"),
                Path.Combine(texRoot, "Generated", "VegAtlas.json"),
                Path.Combine(generatedDir, $"{blockName}.veg.json"));

            // Foreman, planters, seedlings, prompt badges, cache sprite
            string genDir2 = Path.Combine(texRoot, "Generated");
            _foremanAtlas = TryLoadTexture(Path.Combine(genDir2, "ForemanAtlas.png"));
            _foremanFrames = LoadAtlasRects(Path.Combine(genDir2, "ForemanAtlas.json"));
            _planterAtlas = TryLoadTexture(Path.Combine(genDir2, "PlanterAtlas.png"));
            _planterFrames = LoadAtlasRects(Path.Combine(genDir2, "PlanterAtlas.json"));
            _seedlingAtlas = TryLoadTexture(Path.Combine(genDir2, "SeedlingAtlas.png"));
            _seedlingFrames = LoadAtlasRects(Path.Combine(genDir2, "SeedlingAtlas.json"));
            _badgeE = TryLoadTexture(Path.Combine(genDir2, "BadgeE.png"));
            _badgeQ = TryLoadTexture(Path.Combine(genDir2, "BadgeQ.png"));
            _badgeC = TryLoadTexture(Path.Combine(genDir2, "BadgeC.png"));
            _badgeT = TryLoadTexture(Path.Combine(genDir2, "BadgeT.png"));
            _font = BitmapFont.Load(Game.GraphicsDevice, Path.Combine(genDir2, "FontAtlas.png"));
            _audio = AudioSystem.Load(Path.Combine(Tweaks.ContentRoot(), "Audio"));
            _badgeAlert = TryLoadTexture(Path.Combine(genDir2, "BadgeAlert.png"));
            _badgeDone = TryLoadTexture(Path.Combine(genDir2, "BadgeDone.png"));
            _cacheTexture = TryLoadTexture(Path.Combine(genDir2, "Cache.png"));
            _quadAtlas = TryLoadTexture(Path.Combine(genDir2, "QuadAtlas.png"));
            _quadFrames = LoadAtlasRects(Path.Combine(genDir2, "QuadAtlas.json"));

            // Truck interaction points come from the terrain grid (O tiles)
            _truckCenters = _tileMap?.FindTileCenters('O') ?? new List<Vector2>();

            // Dust puff: soft radial tan cloud
            const int duSize = 24;
            _dustTexture = new Texture2D(Game.GraphicsDevice, duSize, duSize);
            var duData = new Color[duSize * duSize];
            for (int y = 0; y < duSize; y++)
                for (int x = 0; x < duSize; x++)
                {
                    float nx = (x - duSize / 2f) / (duSize / 2f);
                    float ny = (y - duSize / 2f) / (duSize / 2f);
                    float r2 = nx * nx + ny * ny;
                    float a = r2 < 1f ? (1f - r2) * (1f - r2) : 0f;
                    duData[y * duSize + x] = new Color(202, 182, 148) * a;
                }
            _dustTexture.SetData(duData);

            // tire-track stamp: two parallel tread bars, travel along local X.
            // 15x15 world px; successive stamps overlap into continuous twin lines.
            const int trW = 15, trH = 15;
            _trackTexture = new Texture2D(Game.GraphicsDevice, trW, trH);
            var trData = new Color[trW * trH];
            var tread = new Color(46, 34, 22);
            for (int x = 0; x < trW; x++)
                for (int y = 0; y < trH; y++)
                {
                    bool bar = y is >= 2 and <= 4 or >= 10 and <= 12;
                    if (!bar) continue;
                    // ragged tread edges
                    uint h = (uint)(x * 374761393 + y * 668265263);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    float a = (h & 0xFF) / 255f < 0.8f ? 1f : 0.4f;
                    trData[y * trW + x] = tread * a;
                }
            _trackTexture.SetData(trData);

            // Planter crew waits by the trucks
            if (_tileMap != null)
            {
                _planters = new PlanterSystem(_tileMap, _caches);
                _planters.SpawnCrew(_atvPosition);
            }

            // Load ATV textures (all 16 directions)
            LoadAtvTextures();
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error in GameplayScreen.LoadContent: {e.Message}");
        }
    }
    
    private Texture2D TryLoadTexture(string path)
    {
        if (!File.Exists(path)) return null;
        using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
        {
            return Texture2D.FromStream(Game.GraphicsDevice, fileStream);
        }
    }

    private Rectangle[] LoadAtlasRects(string jsonPath)
    {
        if (!File.Exists(jsonPath)) return null;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
        var list = new List<Rectangle>();
        foreach (var s in doc.RootElement.GetProperty("sprites").EnumerateArray())
            list.Add(new Rectangle(
                s.GetProperty("x").GetInt32(), s.GetProperty("y").GetInt32(),
                s.GetProperty("w").GetInt32(), s.GetProperty("h").GetInt32()));
        return list.ToArray();
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
                    // If that fails, try the file relative to the app's content directory
                    string filePath = Path.Combine(AppContext.BaseDirectory, "Content", "GameTextures",
                        "TransParentQuadPositions", $"Quad_{direction}.png");
                    try
                    {
                        using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
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
        
        // F5 = the nuclear option: relaunch the WHOLE process via `dotnet run`,
        // which recompiles first — so CODE changes land too, not just assets.
        // Falls back to the in-process fresh-screen restart if the spawn fails
        // (e.g. running from a published build with no project around).
        if (Pressed(Keys.F5))
        {
            string projectDir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Tweaks.ContentRoot(), ".."));
            bool spawned = false;
            if (System.IO.File.Exists(System.IO.Path.Combine(projectDir, "src.csproj")))
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = "run",
                        WorkingDirectory = projectDir,
                        UseShellExecute = false
                    };
                    // the fresh process jumps straight back into this map
                    psi.EnvironmentVariables["CREWBOSS_AUTOSTART"] = _mapName;
                    System.Diagnostics.Process.Start(psi);
                    spawned = true;
                }
                catch { /* dotnet not on PATH — fall back below */ }
            }
            if (spawned)
            {
                Game.Exit();
                return;
            }
            Tweaks.Load();
            var fresh = new GameplayScreen((Game1)Game, _mapName);
            fresh._preGame = false; // straight back into the day
            Game.ScreenManager.RegisterScreen("Gameplay", fresh);
            Game.ScreenManager.ChangeScreen("Gameplay");
            return;
        }

        // +/- = zoom in/out (presentation scale; world math stays 1:1)
        if (Pressed(Keys.OemPlus) || Pressed(Keys.Add)) ApplyZoom(_presentZoom * 1.15f);
        if (Pressed(Keys.OemMinus) || Pressed(Keys.Subtract)) ApplyZoom(_presentZoom / 1.15f);

        // H = toggle the on-screen controls card
        if (Pressed(Keys.H)) _showHelp = !_showHelp;

        // Pre-game: block overview until any key
        if (_preGame)
        {
            if (_currentKeyboardState.GetPressedKeys().Length > 0 &&
                _previousKeyboardState.GetPressedKeys().Length == 0)
                _preGame = false;
            return;
        }

        // Day over: score screen until R restarts the clock
        if (_dayOver)
        {
            if (Pressed(Keys.R))
            {
                _dayRemaining = DAY_SECONDS;
                _dayOver = false;
                ResetAtvPosition();
            }
            return;
        }

        _dayRemaining -= deltaTime;
        if (_dayRemaining <= 0)
        {
            _dayRemaining = 0;
            _dayOver = true;
            return;
        }

        // Check for reset key (R)
        if (Pressed(Keys.R))
        {
            ResetAtvPosition();
        }

        // Aiming a line-in: rotation + confirm/cancel replace normal input
        if (_aiming)
        {
            float rot = 2.4f * deltaTime;
            if (_currentKeyboardState.IsKeyDown(Keys.A) || _currentKeyboardState.IsKeyDown(Keys.Left))
                _aimAngle -= rot;
            if (_currentKeyboardState.IsKeyDown(Keys.D) || _currentKeyboardState.IsKeyDown(Keys.Right))
                _aimAngle += rot;

            if (Pressed(Keys.C) || Pressed(Keys.E))
            {
                var dir = new Vector2(MathF.Cos(_aimAngle), MathF.Sin(_aimAngle));
                _planters.StartLineIn(_aimPlanter, _aimCache, dir);
                _aiming = false;
                _audio?.Blip();
            }
            else if (Pressed(Keys.Q) || Pressed(Keys.F))
            {
                _aiming = false;
            }

            _planters?.Update(deltaTime, _atvPosition);
            UpdateCamera(deltaTime);
            return;
        }

        // Context actions: E = mount/dismount, Q = box action, F = crew,
        // C = aim line-in at a cache, T = coach (on foot)
        if (Pressed(Keys.E)) ToggleMount();
        if (Pressed(Keys.Q)) DoBoxAction();
        if (Pressed(Keys.F)) _planters?.ToggleCrew(_atvPosition);
        if (Pressed(Keys.C)) TryStartAiming();
        if (Pressed(Keys.T) && !_mounted && _planters != null)
        {
            var target = _planters.FindCoachTarget(_atvPosition);
            if (target != null)
            {
                _planters.Coach(target);
                _audio?.Blip();
            }
        }

        // Walking a planted line on foot reveals tree quality nearby
        if (!_mounted) UpdateQualityReveals(deltaTime);
        else DecayReveals(deltaTime);

        // Movement: quad physics when mounted, direct walk when on foot
        if (_mounted) HandleAtvMovement(deltaTime);
        else HandleFootMovement(deltaTime);

        // Planter crew AI
        _planters?.Update(deltaTime, _atvPosition);

        // dust settles
        UpdateDust(deltaTime);

        // audio: engine RPM, skid, soft shovel thunks when trees go in nearby
        if (!_mounted) _drifting = false;
        _audio?.Update(deltaTime, _mounted, _atvVelocity.Length(), Tweaks.GearMax[_gear],
            _pendingGear >= 0, _drifting);
        _plantSfxCooldown -= deltaTime;
        if (_planters != null && _planters.TreesPlanted > _lastTreeCount)
        {
            if (_plantSfxCooldown <= 0)
            {
                _audio?.Plant();
                _plantSfxCooldown = 0.14f;
            }
            _lastTreeCount = _planters.TreesPlanted;
        }

        // Update camera to follow the player
        UpdateCamera((float)gameTime.ElapsedGameTime.TotalSeconds);
    }
    
    private bool Pressed(Keys key) =>
        _currentKeyboardState.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);

    /// <summary>Rotate a unit vector toward a target direction, clamped to maxRadians.</summary>
    private static Vector2 TurnToward(Vector2 cur, Vector2 target, float maxRadians)
    {
        float a = MathF.Atan2(cur.Y, cur.X);
        float d = MathHelper.WrapAngle(MathF.Atan2(target.Y, target.X) - a);
        a += MathHelper.Clamp(d, -maxRadians, maxRadians);
        return new Vector2(MathF.Cos(a), MathF.Sin(a));
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float a = MathF.Atan2(v.Y, v.X) + radians;
        return new Vector2(MathF.Cos(a), MathF.Sin(a));
    }

    /// <summary>On foot: planted spots near the player reveal their quality for a moment.</summary>
    private void UpdateQualityReveals(float dt)
    {
        DecayReveals(dt);
        if (_planters == null || _tileMap == null) return;

        int ts = _tileMap.TileSize, th = _tileMap.TileHeight;
        int ptx = (int)(_atvPosition.X / ts), pty = (int)(_atvPosition.Y / th);
        for (int ty = pty - 2; ty <= pty + 2; ty++)
            for (int tx = ptx - 2; tx <= ptx + 2; tx++)
            {
                int count = _planters.PlantedAtTile(tx, ty);
                for (int s = 0; s < count; s++)
                {
                    Vector2 pos = PlanterSystem.SpotPos(tx, ty, s, ts, th);
                    if (Vector2.DistanceSquared(pos, _atvPosition) < 48f * 48f)
                        _reveals[(tx, ty, s)] = 2.2f;
                }
            }
    }

    private void DecayReveals(float dt)
    {
        if (_reveals.Count == 0) return;
        var expired = new List<(int, int, int)>();
        foreach (var key in _reveals.Keys)
        {
            float t = _reveals[key] - dt;
            if (t <= 0) expired.Add(key);
            else _reveals[key] = t;
        }
        foreach (var k in expired) _reveals.Remove(k);
    }

    /// <summary>C at a cache: enter aim mode if there's a cache and a planter to line in.</summary>
    private void TryStartAiming()
    {
        if (_planters == null) return;
        CacheEntity cache = null;
        float bestD = 70f;
        foreach (var c in _caches)
        {
            float d = Vector2.Distance(_atvPosition, c.Pos);
            if (d < bestD) { bestD = d; cache = c; }
        }
        if (cache == null) return;

        var planter = _planters.FindLinePlanter(cache.Pos);
        if (planter == null) return;
        if (cache.Boxes <= 0 && planter.Bag <= 0) return; // no trees = no line

        _aiming = true;
        _aimCache = cache;
        _aimPlanter = planter;
        var f = FacingVector();
        _aimAngle = MathF.Atan2(f.Y, f.X);
    }

    private void ToggleMount()
    {
        if (_mounted)
        {
            _parkedAtvPos = _atvPosition;
            _parkedAtvDir = _currentAtvDirection;
            _parkedAtvDirIdx = _atvDirIdx;
            _atvVelocity = Vector2.Zero;
            _mounted = false;
            _atvPosition += new Vector2(0, 14); // step off beside the quad
            _audio?.Blip();
        }
        else if (Vector2.Distance(_atvPosition, _parkedAtvPos) < INTERACT_RANGE)
        {
            _atvPosition = _parkedAtvPos;
            _currentAtvDirection = _parkedAtvDir;
            _atvDirIdx = _parkedAtvDirIdx;
            _mounted = true;
            _audio?.Blip();
        }
    }

    private void HandleFootMovement(float deltaTime)
    {
        Vector2 dir = Vector2.Zero;
        if (_currentKeyboardState.IsKeyDown(Keys.Up) || _currentKeyboardState.IsKeyDown(Keys.W)) dir.Y -= 1;
        if (_currentKeyboardState.IsKeyDown(Keys.Down) || _currentKeyboardState.IsKeyDown(Keys.S)) dir.Y += 1;
        if (_currentKeyboardState.IsKeyDown(Keys.Left) || _currentKeyboardState.IsKeyDown(Keys.A)) dir.X -= 1;
        if (_currentKeyboardState.IsKeyDown(Keys.Right) || _currentKeyboardState.IsKeyDown(Keys.D)) dir.X += 1;

        _walking = dir != Vector2.Zero;
        if (!_walking) return;

        dir.Normalize();
        _footDir = MathF.Abs(dir.X) >= MathF.Abs(dir.Y)
            ? (dir.X >= 0 ? "E" : "W")
            : (dir.Y >= 0 ? "S" : "N");
        _walkAnim += deltaTime;

        Vector2 delta = dir * Tweaks.FootSpeed * FootSpeedMult(_atvPosition) * deltaTime;
        Vector2 tryX = _atvPosition + new Vector2(delta.X, 0);
        if (FootSpeedMult(tryX) > 0) _atvPosition.X = tryX.X;
        Vector2 tryY = _atvPosition + new Vector2(0, delta.Y);
        if (FootSpeedMult(tryY) > 0) _atvPosition.Y = tryY.Y;

        _atvPosition.X = MathHelper.Clamp(_atvPosition.X, 0, _mapBounds.Width);
        _atvPosition.Y = MathHelper.Clamp(_atvPosition.Y, 0, _mapBounds.Height);
    }

    /// <summary>Quad-only: logs and stumps block the machine (people climb over).</summary>
    private bool HitsDebris(Vector2 pos)
    {
        const float quadR = 10f;
        foreach (var (c, r) in _debrisCircles)
        {
            float rr = r + quadR;
            float dx = pos.X - c.X, dy = pos.Y - c.Y;
            if (dx * dx + dy * dy < rr * rr) return true;
        }
        return false;
    }

    /// <summary>On foot the bush and swamp are passable, just slow. Trucks still block.</summary>
    private float FootSpeedMult(Vector2 pos)
    {
        if (_tileMap == null) return 1f;
        var t = _tileMap.TerrainAtWorld(pos);
        return t.Name switch
        {
            "forest" => 0.35f,
            "swamp" => 0.5f,
            "obstacle" => 0f,
            "outside" => 0f,
            _ => MathF.Max(t.Speed, 0.5f)
        };
    }

    private Vector2 FacingVector() => _mounted
        ? (_atvDirection == Vector2.Zero ? new Vector2(0, 1) : _atvDirection)
        : _footDir switch
        {
            "N" => new Vector2(0, -1),
            "S" => new Vector2(0, 1),
            "E" => new Vector2(1, 0),
            _ => new Vector2(-1, 0)
        };

    /// <summary>What Q would do right now, and where. Priority: truck, cache, parked quad, open ground.</summary>
    private (BoxAction action, Vector2 target) GetBoxAction()
    {
        Vector2 pos = _atvPosition;

        foreach (var t in _truckCenters)
            if (Vector2.Distance(pos, t) < INTERACT_RANGE + 20)
            {
                if (_mounted && _atvBoxes < ATV_BOX_CAP) return (BoxAction.LoadFromTruck, t);
                if (!_mounted && !_carryingBox) return (BoxAction.LoadFromTruck, t);
                return (BoxAction.None, default);
            }

        foreach (var c in _caches)
            if (Vector2.Distance(pos, c.Pos) < INTERACT_RANGE)
            {
                if (_mounted && _atvBoxes > 0) return (BoxAction.AddToCache, c.Pos);
                if (!_mounted && _carryingBox) return (BoxAction.AddToCache, c.Pos);
                return (BoxAction.None, default);
            }

        if (!_mounted && Vector2.Distance(pos, _parkedAtvPos) < INTERACT_RANGE)
        {
            if (_carryingBox && _atvBoxes < ATV_BOX_CAP) return (BoxAction.LoadAtvFromHands, _parkedAtvPos);
            if (!_carryingBox && _atvBoxes > 0) return (BoxAction.TakeFromAtv, _parkedAtvPos);
        }

        if ((_mounted && _atvBoxes > 0) || (!_mounted && _carryingBox))
            return (BoxAction.PlaceCache, pos + FacingVector() * 40);

        return (BoxAction.None, default);
    }

    private void DoBoxAction()
    {
        var (action, target) = GetBoxAction();
        switch (action)
        {
            case BoxAction.LoadFromTruck:
                if (_mounted) _atvBoxes++;
                else _carryingBox = true;
                break;
            case BoxAction.AddToCache:
                var cache = _caches.Find(c => c.Pos == target);
                if (cache == null) break;
                if (_mounted) { _atvBoxes--; cache.Boxes++; }
                else { _carryingBox = false; cache.Boxes++; }
                break;
            case BoxAction.TakeFromAtv:
                _atvBoxes--;
                _carryingBox = true;
                break;
            case BoxAction.LoadAtvFromHands:
                _carryingBox = false;
                _atvBoxes++;
                break;
            case BoxAction.PlaceCache:
                if (_tileMap != null && !_tileMap.IsPassable(target)) break; // no caches in trees/trucks
                if (_mounted) _atvBoxes--;
                else _carryingBox = false;
                _caches.Add(new CacheEntity { Pos = target, Boxes = 1 });
                break;
        }
        if (action != BoxAction.None) _audio?.Thud();
    }

    private void HandleAtvMovement(float deltaTime)
    {
        // gear shifts take Tweaks.ShiftTime: clutch in (no throttle), then it engages
        if (_pendingGear >= 0)
        {
            _shiftTimer -= deltaTime;
            if (_shiftTimer <= 0)
            {
                _gear = _pendingGear;
                _pendingGear = -1;
                _audio?.Shift();
            }
        }
        else if (Pressed(Keys.X))
        {
            if (_gear < 5) { _pendingGear = _gear + 1; _shiftTimer = Tweaks.ShiftTime; }
        }
        else if (Pressed(Keys.Z) || Pressed(Keys.LeftControl) || Pressed(Keys.RightControl))
        {
            if (_gear > 0) { _pendingGear = _gear - 1; _shiftTimer = Tweaks.ShiftTime; } // below 1st sits Reverse
        }
        bool shifting = _pendingGear >= 0;

        // torque vs terrain: high-torque gears power through rough ground,
        // low-torque gears lose their acceleration in it
        float terrain = _tileMap?.SpeedAt(_atvPosition) ?? 1f;
        float torque = Tweaks.GearTorque[_gear];
        float accelRate = Tweaks.GearAccel[_gear] * MathHelper.Lerp(terrain, 1f, torque * 0.8f);
        float maxSpeed = Tweaks.GearMax[_gear];

        // Racing controls: UP = gas, DOWN = brake (and drift), LEFT/RIGHT = steer.
        // Gas only ever pushes along the nose; Reverse gear pushes backward off it.
        bool throttle = _currentKeyboardState.IsKeyDown(Keys.Up) || _currentKeyboardState.IsKeyDown(Keys.W);
        bool braking = _currentKeyboardState.IsKeyDown(Keys.Down) || _currentKeyboardState.IsKeyDown(Keys.S)
                    || _currentKeyboardState.IsKeyDown(Keys.Space);
        float steer = 0f;
        if (_currentKeyboardState.IsKeyDown(Keys.Left) || _currentKeyboardState.IsKeyDown(Keys.A)) steer -= 1f;
        if (_currentKeyboardState.IsKeyDown(Keys.Right) || _currentKeyboardState.IsKeyDown(Keys.D)) steer += 1f;

        float speed0 = _atvVelocity.Length();
        Vector2 heading = _atvDirection == Vector2.Zero ? new Vector2(0, -1) : _atvDirection;
        bool reverse = _gear == 0;

        // The tuned constants below are per-frame factors at 60Hz; the game now
        // runs at the display's refresh, so raise them to dt*60 to keep the
        // same handling at any frame rate
        float PerFrame(float f) => MathF.Pow(f, deltaTime * 60f);

        // Hold SHIFT = handbrake drift: the nose whips around while momentum
        // keeps sliding on the old line; throttle stays live for power slides
        bool drift = _currentKeyboardState.IsKeyDown(Keys.LeftShift)
                  || _currentKeyboardState.IsKeyDown(Keys.RightShift);

        _drifting = (braking || drift) && steer != 0f && speed0 > 20f;

        if (braking)
        {
            // brake hard; steering while braking = drift: the nose whips around
            // while momentum comes along slowly
            _atvVelocity *= PerFrame(0.94f);
            if (steer != 0f)
            {
                heading = Rotate(heading, steer * 4.5f * deltaTime);
                if (speed0 > 13f)
                    _atvVelocity = TurnToward(_atvVelocity / MathF.Max(speed0, 0.01f), heading,
                        2.6f * deltaTime) * _atvVelocity.Length();
            }
        }
        else if (drift)
        {
            // handbrake drift: whippy nose, barely any grip — the quad slides
            // on its old momentum while the wheels point somewhere new
            if (steer != 0f)
                heading = Rotate(heading, steer * 5.2f * deltaTime);

            // power slide: throttle still pushes along the nose, at partial bite
            if (throttle && !shifting)
                _atvVelocity += (reverse ? -heading : heading) * accelRate * 0.55f * deltaTime;

            float spD = _atvVelocity.Length();
            if (spD > 1f)
            {
                Vector2 travel = reverse ? -heading : heading;
                _atvVelocity = Vector2.Lerp(_atvVelocity, travel * spD, 1f - PerFrame(0.965f));
            }

            // tires scrubbing sideways bleed speed
            _atvVelocity *= PerFrame(0.9875f);

            float spDm = _atvVelocity.Length();
            if (spDm > maxSpeed)
                _atvVelocity *= MathF.Max(maxSpeed / spDm, PerFrame(0.94f));
        }
        else
        {
            // steer: snappy hands, easing off some at speed
            if (steer != 0f)
            {
                float turnRate = MathHelper.Lerp(4.3f, 2.0f, MathF.Min(1f, speed0 / 107f));
                heading = Rotate(heading, steer * turnRate * deltaTime);
            }

            // throttle (clutch is in while shifting gears); launch punch —
            // extra shove off the line, tapering out by half the gear's top
            if (throttle && !shifting)
            {
                float launch = 1f + 0.7f * (1f - MathF.Min(1f, speed0 / (maxSpeed * 0.5f)));
                _atvVelocity += (reverse ? -heading : heading) * accelRate * launch * deltaTime;
            }

            // grip: momentum lines up behind the nose — firm at low speed,
            // loosening as you go fast so hard corners get a playful slide
            float sp2 = _atvVelocity.Length();
            if (sp2 > 1f)
            {
                float gripBase = MathHelper.Lerp(0.84f, 0.91f, MathF.Min(1f, speed0 / 127f));
                Vector2 travel = reverse ? -heading : heading;
                _atvVelocity = Vector2.Lerp(_atvVelocity, travel * sp2, 1f - PerFrame(gripBase));
            }

            // drag: gentle coast off the gas; under throttle, rough ground bogs
            // down low-torque gears (G4/G5 in slash go nowhere — drop a gear)
            float drag = !throttle
                ? 0.995f
                : 0.995f - (1f - terrain) * (1f - torque) * 0.045f;
            _atvVelocity *= PerFrame(drag);

            // over the gear's top speed: firm engine braking (a soft floor here
            // let every gear creep to highway speed)
            float sp = _atvVelocity.Length();
            if (sp > maxSpeed)
                _atvVelocity *= MathF.Max(maxSpeed / sp, PerFrame(0.94f));
        }

        _atvDirection = heading;
        
        // Update position based on velocity, scaled by terrain (road fast, slash slow),
        // with axis-separated collision against impassable tiles (forest, obstacles)
        Vector2 delta = _atvVelocity * deltaTime;
        if (_tileMap != null)
        {
            delta *= _tileMap.SpeedAt(_atvPosition);

            Vector2 tryX = _atvPosition + new Vector2(delta.X, 0);
            if (_tileMap.IsPassable(tryX) && !HitsDebris(tryX)) _atvPosition.X = tryX.X;
            else _atvVelocity.X = 0;

            Vector2 tryY = _atvPosition + new Vector2(0, delta.Y);
            if (_tileMap.IsPassable(tryY) && !HitsDebris(tryY)) _atvPosition.Y = tryY.Y;
            else _atvVelocity.Y = 0;
        }
        else
        {
            _atvPosition += delta;
        }

        // Clamp position to map bounds
        _atvPosition.X = MathHelper.Clamp(_atvPosition.X, 0, _mapBounds.Width);
        _atvPosition.Y = MathHelper.Clamp(_atvPosition.Y, 0, _mapBounds.Height);
        
        // The sprite always faces the nose (heading) — steering, drifting and
        // reversing all read correctly from it
        if (_atvVelocity.LengthSquared() > 1f || steer != 0f)
            UpdateAtvDirectionTexture();
        
        // Terrain follow: slope drag (uphill bogs, downhill pushes) + suspension —
        // the chassis chases ground height on a spring, so crests float and dips slam
        float speed = _atvVelocity.Length();
        float groundLift = Lift(_atvPosition.X, _atvPosition.Y);
        if (speed > 1f)
        {
            Vector2 vn = _atvVelocity / speed;
            float ahead = Lift(_atvPosition.X + vn.X * 26f, _atvPosition.Y + vn.Y * 26f);
            float slope = (ahead - groundLift) / 26f; // positive = climbing
            _atvVelocity *= 1f - Math.Clamp(slope, -0.15f, 0.30f) * 4f * deltaTime;
        }
        // Micro-relief: position-keyed bumps too fine for the tile heightfield.
        // Keyed to WORLD POSITION (not time), so they're fixed ground features —
        // the same spot always bumps the same way, a parked quad never moves,
        // and riding over them at speed makes the ground read 3D underfoot.
        float bumpRough = BumpRoughAt(_atvPosition);
        float bump = BumpAt(_atvPosition.X, _atvPosition.Y) * bumpRough;

        // livelier spring (softer damping): crests and bumps overshoot a touch,
        // so the chassis visibly works the terrain
        float springTarget = groundLift + MathF.Max(0f, bump);
        _chassisVel += ((springTarget - _chassisLift) * 75f - _chassisVel * 5.5f) * deltaTime;
        _chassisLift += _chassisVel * deltaTime;

        // TILT: ground slope in screen-x under the quad (tile relief + micro
        // bumps) leans the sprite — uphill side up, matching the hillshade.
        // Smoothed so it reads as suspension articulation, not snapping.
        float hR = Lift(_atvPosition.X + 11f, _atvPosition.Y) + BumpAt(_atvPosition.X + 11f, _atvPosition.Y) * bumpRough;
        float hL = Lift(_atvPosition.X - 11f, _atvPosition.Y) + BumpAt(_atvPosition.X - 11f, _atvPosition.Y) * bumpRough;
        float tiltTarget = MathHelper.Clamp(-MathF.Atan2(hR - hL, 22f) * 1.1f, -0.35f, 0.35f);
        _quadTilt += (tiltTarget - _quadTilt) * (1f - MathF.Exp(-9f * deltaTime));

        // (rough-ground rattle removed: the constant sprite shake read as
        // jitter. _bounceTime still advances — it seeds the dust hash.)
        _bounceTime += deltaTime * (6f + speed / 4.7f);

        // dust plume: only at real speed, spawned densely along the traveled
        // segment so it reads as one continuous cloud, not scattered puffs
        if (speed > Tweaks.DustMinSpeed)
        {
            float ramp = MathF.Min(1f, (speed - Tweaks.DustMinSpeed) / 43f);
            float spacing = MathHelper.Lerp(13f, 7f, ramp);
            float travelled = Vector2.Distance(_prevQuadPos, _atvPosition);
            _dustSpawnDist += travelled;
            int guard = 0;
            while (_dustSpawnDist > spacing && _dust.Count < 400 && guard++ < 16)
            {
                _dustSpawnDist -= spacing;
                float k = travelled > 0.01f ? _dustSpawnDist / travelled : 0f;
                Vector2 basePos = Vector2.Lerp(_atvPosition, _prevQuadPos, k) - _atvDirection * 19f;

                uint h = (uint)(_bounceTime * 977f + _dust.Count * 131 + guard * 37);
                h = (h ^ (h >> 13)) * 1274126177u;
                float jx = ((h & 0xFF) / 255f - 0.5f) * 8f;
                float jy = (((h >> 8) & 0xFF) / 255f - 0.5f) * 6f;
                _dust.Add(new Dust
                {
                    Pos = basePos + new Vector2(jx, jy),
                    Vel = -_atvDirection * 9f + new Vector2(jx * 0.8f, -5f),
                    Age = 0f,
                    Life = 1.0f + ((h >> 16) & 0xFF) / 255f * 0.5f,
                    Size = 13f + ((h >> 20) & 0xFF) / 255f * 6f
                });
            }
        }
        else
        {
            _dustSpawnDist = 0f;
        }

        // Road smoke: any movement on road/trail kicks up a gravel-exhaust
        // trail behind the quad — no speed gate beyond a crawl, so even
        // puttering down the FSR feels alive
        string terrName = _tileMap?.TerrainAtWorld(_atvPosition).Name ?? "";
        if (speed > 8f && (terrName == "road" || terrName == "trail"))
        {
            float travelled = Vector2.Distance(_prevQuadPos, _atvPosition);
            _smokeSpawnDist += travelled;
            int guard = 0;
            while (_smokeSpawnDist > 15f && _dust.Count < 400 && guard++ < 8)
            {
                _smokeSpawnDist -= 15f;
                uint h = (uint)(_bounceTime * 811f + _dust.Count * 97 + guard * 53);
                h = (h ^ (h >> 13)) * 1274126177u;
                float jx = ((h & 0xFF) / 255f - 0.5f) * 6f;
                float jy = (((h >> 8) & 0xFF) / 255f - 0.5f) * 4f;
                _dust.Add(new Dust
                {
                    Pos = _atvPosition - _atvDirection * 17f + new Vector2(jx, jy),
                    Vel = -_atvDirection * 6f + new Vector2(jx * 0.6f, -11f), // drifts up
                    Age = 0f,
                    Life = 0.8f + ((h >> 16) & 0xFF) / 255f * 0.4f,
                    Size = 9f + ((h >> 20) & 0xFF) / 255f * 5f,
                    Smoke = true
                });
            }
        }
        else
        {
            _smokeSpawnDist = 0f;
        }

        // tire tracks: stamp a twin-tread mark every 13px of travel
        if (speed > 13f && _trackTexture != null)
        {
            _trackDist += Vector2.Distance(_prevQuadPos, _atvPosition);
            if (_trackDist > 13f)
            {
                _trackDist = 0f;
                _tracks.Add(new TrackStamp
                {
                    Pos = _atvPosition,
                    Ang = MathF.Atan2(_atvVelocity.Y, _atvVelocity.X)
                });
                if (_tracks.Count > MAX_TRACKS) _tracks.RemoveAt(0);
            }
        }
        _prevQuadPos = _atvPosition;
    }

    private void UpdateDust(float dt)
    {
        for (int i = _dust.Count - 1; i >= 0; i--)
        {
            var d = _dust[i];
            d.Age += dt;
            d.Pos += d.Vel * dt;
            if (d.Age >= d.Life) _dust.RemoveAt(i);
            else _dust[i] = d;
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
        
        // 32-direction index for the generated atlas (11.25 degrees per step)
        _atvDirIdx = (int)Math.Round(degrees / 11.25) % 32;

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
    
    private void UpdateCamera(float deltaTime)
    {
        // Look-ahead: lead the camera along travel so the scene flows steadily
        // instead of accelerating with every steer — predictable flow is what
        // the inner ear wants. The exponential follow eases the lead in/out.
        Vector2 lead = _mounted ? _atvVelocity * 0.25f : Vector2.Zero;

        // Center the ATV (plus lead) on screen
        var target = new Vector2(
            _atvPosition.X + lead.X - (_screenWidth / (2 * _cameraZoom)),
            _atvPosition.Y + lead.Y - (_screenHeight / (2 * _cameraZoom)));

        // Smooth exponential follow. No deadzone: it made camera velocity
        // discontinuous (hold, then catch up) which read as jitter while
        // steering — the sub-pixel present now absorbs micro-corrections
        // smoothly, so the whole frame glides instead.
        if (!_camInit) { _camSmooth = target; _camInit = true; }
        float k = 1f - MathF.Exp(-12f * MathF.Max(deltaTime, 1e-4f));
        _camSmooth += (target - _camSmooth) * k;

        float maxX = _mapBounds.Width - (_screenWidth / _cameraZoom);
        float maxY = _mapBounds.Height - (_screenHeight / _cameraZoom);
        _camSmooth.X = MathHelper.Clamp(_camSmooth.X, 0, maxX);
        _camSmooth.Y = MathHelper.Clamp(_camSmooth.Y, 0, maxY);

        // integer camera: world pixels land 1:1 on target pixels — no crawl.
        // The dropped fraction is NOT lost: it offsets the present upscale, so
        // scroll speed stays continuous instead of stepping whole pixels.
        _cameraPosition.X = MathF.Floor(_camSmooth.X);
        _cameraPosition.Y = MathF.Floor(_camSmooth.Y);
        _camFrac = _camSmooth - _cameraPosition;
    }
    
    private void ResetAtvPosition()
    {
        // Reset to the road spawn if tile data exists, otherwise map center
        _mounted = true;
        _gear = 1; // always start in first
        _pendingGear = -1;
        _atvPosition = _tileMap?.FindSpawn() ?? new Vector2(_mapBounds.Width / 2, _mapBounds.Height / 2);
        _atvVelocity = Vector2.Zero;
        _atvAcceleration = Vector2.Zero;
        _atvDirection = new Vector2(0, -1); // Default facing north
        _currentAtvDirection = "N";
        Console.WriteLine("ATV position reset");
    }
    
    public override void PreDraw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        _sceneTarget ??= new RenderTarget2D(Game.GraphicsDevice, _screenWidth, _screenHeight);
        _uiTarget ??= new RenderTarget2D(Game.GraphicsDevice, _screenWidth, _screenHeight);
        Game.GraphicsDevice.SetRenderTarget(_sceneTarget);
        Game.GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(SpriteSortMode.Deferred, null, SamplerState.PointClamp);
        if (_preGame) DrawPreGame(spriteBatch);
        else DrawScene(spriteBatch);
        spriteBatch.End();

        // screen-anchored overlays: separate target, presented unshifted
        Game.GraphicsDevice.SetRenderTarget(_uiTarget);
        Game.GraphicsDevice.Clear(Color.Transparent);
        spriteBatch.Begin(SpriteSortMode.Deferred, null, SamplerState.PointClamp);
        if (!_preGame) DrawUi(spriteBatch);
        spriteBatch.End();
        Game.GraphicsDevice.SetRenderTarget(null);
    }

    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        // ONE smooth upscale of the finished logical frame (outer batch is
        // LinearClamp) — the only place any fractional scaling happens.
        // Shifted by the camera's sub-pixel remainder so scrolling glides
        // between the integer-camera frames (the +1 overscan covers the edge).
        if (_sceneTarget != null)
        {
            Vector2 shift = _preGame ? Vector2.Zero : _camFrac * _presentZoom;
            spriteBatch.Draw(_sceneTarget, -shift, null, Color.White, 0f,
                Vector2.Zero, _presentZoom, SpriteEffects.None, 0f);
        }
        // UI on top, pinned: no sub-pixel shift
        if (_uiTarget != null && !_preGame)
            spriteBatch.Draw(_uiTarget, Vector2.Zero, null, Color.White, 0f,
                Vector2.Zero, _presentZoom, SpriteEffects.None, 0f);
    }

    private void DrawScene(SpriteBatch spriteBatch)
    {
        // Terrain relief: the PNG carries maxElev rows of top padding (baked
        // hill displacement overhang). The blit maps world row y to PNG row
        // y + maxElev.
        int maxElev = _tileMap?.MaxElev ?? 0;
        Rectangle mapRect = new Rectangle(0, 0, _screenWidth, _screenHeight);
        Rectangle mapSourceRect = new Rectangle(
            (int)_cameraPosition.X, (int)_cameraPosition.Y + maxElev,
            (int)(_screenWidth / _cameraZoom), (int)(_screenHeight / _cameraZoom));
        spriteBatch.Draw(_mapTexture, mapRect, mapSourceRect, Color.White);

        // Faint wheel trails worn into the ground
        DrawTrails(spriteBatch);

        // Planted seedlings sit flat on the ground, under all standing sprites
        DrawSeedlings(spriteBatch);

        // Trees, caches, parked quad, planters, player — y-sorted far-to-near
        DrawWorldSprites(spriteBatch);

        // Dust hangs over everything at ground level
        DrawDust(spriteBatch);

        // World-anchored overlays travel with the camera shift
        DrawPrompts(spriteBatch);
        if (_aiming) DrawAimArrow(spriteBatch);
    }
    /// <summary>
    /// Screen-anchored HUD. Rendered into _uiTarget and presented WITHOUT the
    /// sub-pixel camera shift so text never swims with the camera.
    /// </summary>
    private void DrawUi(SpriteBatch spriteBatch)
    {
        DrawSpeedometer(spriteBatch);
        DrawBoxHud(spriteBatch);
        DrawDayHud(spriteBatch);
        DrawControls(spriteBatch);

        if (_dayOver) DrawScoreScreen(spriteBatch);
    }

    private void DrawPreGame(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(GetOrCreateTexture("pregameBg", new Color(14, 20, 16)),
            new Rectangle(0, 0, _screenWidth, _screenHeight), Color.White);

        if (_mapTexture != null)
        {
            // fit the whole block on screen
            float s = Math.Min((_screenWidth - 200f) / _mapTexture.Width,
                (_screenHeight - 160f) / _mapTexture.Height);
            int w = (int)(_mapTexture.Width * s), h = (int)(_mapTexture.Height * s);
            spriteBatch.Draw(_mapTexture,
                new Rectangle((_screenWidth - w) / 2, 90, w, h), Color.White);
        }

        if (_font != null)
        {
            _font.DrawCentered(spriteBatch, "BLOCK 1", _screenWidth / 2f, 30, 6f, Color.White);
            _font.DrawCentered(spriteBatch, "PRESS ANY KEY TO START THE DAY",
                _screenWidth / 2f, _screenHeight - 44, 3f, new Color(255, 222, 92));
        }
    }

    private void DrawDayHud(SpriteBatch spriteBatch)
    {
        if (_font == null) return;
        // HUD lives in the shaded vista band — bottom row, like a menu bar
        int t = (int)MathF.Ceiling(_dayRemaining);
        string clock = $"{t / 60}:{t % 60:00}";
        _font.Draw(spriteBatch, clock, new Vector2(_screenWidth - 90, HudBase - 32), 4f, Color.White);

        // gear indicator while riding; gray with the target while the shift is in
        if (_mounted)
        {
            string gearText = _pendingGear >= 0
                ? (_pendingGear == 0 ? "R.." : $"G{_pendingGear}..")
                : (_gear == 0 ? "R" : $"G{_gear}");
            Color gearColor = _pendingGear >= 0 ? Color.Gray
                : _gear == 0 ? new Color(230, 120, 80)
                : _atvVelocity.Length() > Tweaks.GearMax[_gear] * 0.92f ? new Color(255, 222, 92) : Color.White;
            _font.Draw(spriteBatch, gearText, new Vector2(170, HudBase - 30), 4f, gearColor);
        }
        if (_planters != null)
            _font.Draw(spriteBatch, _planters.TreesPlanted.ToString(),
                new Vector2(_screenWidth - 150, HudBase - 28), 3f, new Color(140, 220, 130));
    }

    private void DrawScoreScreen(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(GetOrCreateTexture("scoreDim", Color.Black),
            new Rectangle(0, 0, _screenWidth, _screenHeight), Color.White * 0.72f);
        if (_font == null || _planters == null) return;

        int trees = _planters.TreesPlanted;
        int faults = _planters.Faults;
        int idle = (int)_planters.IdleSeconds;

        int stars = 3;
        if (faults > trees * 0.10f) stars--;
        if (idle > 180) stars--;
        if (trees < 200) stars = Math.Min(stars, 1);

        float cx = _screenWidth / 2f;
        _font.DrawCentered(spriteBatch, "DAY OVER", cx, 170, 8f, Color.White);
        _font.DrawCentered(spriteBatch, $"TREES PLANTED: {trees}", cx, 280, 4f, new Color(140, 220, 130));
        _font.DrawCentered(spriteBatch, $"FAULTS: {faults}", cx, 330, 4f,
            faults > trees * 0.1f ? new Color(214, 60, 48) : Color.White);
        _font.DrawCentered(spriteBatch, $"IDLE TIME: {idle / 60}:{idle % 60:00}", cx, 380, 4f,
            idle > 180 ? new Color(214, 60, 48) : Color.White);
        _font.DrawCentered(spriteBatch, $"STARS: {stars}/3", cx, 450, 5f, new Color(255, 222, 92));
        _font.DrawCentered(spriteBatch, "PRESS R FOR A NEW DAY", cx, 540, 3f, Color.Gray);
    }

    /// <summary>The line-in aim arrow, drawn from the cache along the chosen bearing.</summary>
    private void DrawAimArrow(SpriteBatch spriteBatch)
    {
        Texture2D tex = GetOrCreateTexture("aimArrow", new Color(255, 222, 92));
        Vector2 start = WorldToScreen(_aimCache.Pos);
        float len = 130f * _cameraZoom;
        var dir = new Vector2(MathF.Cos(_aimAngle), MathF.Sin(_aimAngle));

        spriteBatch.Draw(tex, start, null, Color.White * 0.9f, _aimAngle,
            new Vector2(0, 0.5f), new Vector2(len, 3f), SpriteEffects.None, 0f);

        // arrowhead: two strokes angled back from the tip
        Vector2 tip = start + dir * len;
        foreach (float da in stackalloc[] { 2.6f, -2.6f })
            spriteBatch.Draw(tex, tip, null, Color.White * 0.9f, _aimAngle + da,
                new Vector2(0, 0.5f), new Vector2(26f, 3f), SpriteEffects.None, 0f);
    }

    private struct WorldEntity
    {
        public float SortY;
        public Texture2D Tex;
        public Rectangle Src;
        public float X, BaseY, Scale;
        public SpriteEffects Fx;
        public bool Shadow; // ground vehicles cover their own shadow — leave false
        public float LiftExtra; // height above the ground (suspension/airtime)
        public float Rot; // sprite lean (radians) — terrain tilt
    }

    /// <summary>
    /// All world sprites y-sorted: trees merged with entities (caches, quad, player).
    /// </summary>
    private void DrawWorldSprites(SpriteBatch spriteBatch)
    {
        float zoom = _cameraZoom;
        float viewWorldH = _screenHeight / zoom;
        float maxTreeWorld = _treeLayer?.MaxSpriteHeight ?? 80;
        // elevation lift shifts sprites up by as much as MaxElev — range windows
        // must include it, or high-ground objects pop at the screen edges
        float liftPad = _tileMap?.MaxElev ?? 0;

        var entities = new List<WorldEntity>();

        if (!_mounted)
        {
            // parked quad leans statically with whatever slope it sits on
            float pr = BumpRoughAt(_parkedAtvPos);
            float phR = Lift(_parkedAtvPos.X + 11f, _parkedAtvPos.Y) + BumpAt(_parkedAtvPos.X + 11f, _parkedAtvPos.Y) * pr;
            float phL = Lift(_parkedAtvPos.X - 11f, _parkedAtvPos.Y) + BumpAt(_parkedAtvPos.X - 11f, _parkedAtvPos.Y) * pr;
            float parkedTilt = MathHelper.Clamp(-MathF.Atan2(phR - phL, 22f) * 1.1f, -0.35f, 0.35f);
            AddQuadEntity(entities, _parkedAtvPos, _parkedAtvDir, _parkedAtvDirIdx, rider: false, zoom, bounce: 0f, tilt: parkedTilt);
        }

        if (_cacheTexture != null)
            foreach (var c in _caches)
                entities.Add(new WorldEntity
                {
                    SortY = c.Pos.Y,
                    Tex = _cacheTexture,
                    Src = new Rectangle(0, 0, _cacheTexture.Width, _cacheTexture.Height),
                    X = c.Pos.X,
                    BaseY = c.Pos.Y,
                    Scale = zoom,
                    Shadow = true
                });

        // vegetation — pure set dressing
        if (_vegLayer != null)
        {
            foreach (var (vx, vy, vv) in _vegLayer.InRange(
                _cameraPosition.Y - 30 - liftPad,
                _cameraPosition.Y + viewWorldH + 30 + liftPad))
            {
                var src = _vegLayer.Sprites[vv % _vegLayer.Sprites.Length];
                entities.Add(new WorldEntity
                {
                    SortY = vy,
                    Tex = _vegLayer.Atlas,
                    Src = src,
                    X = vx,
                    BaseY = vy,
                    Scale = zoom,
                    Shadow = vv >= 3 // bushes ground themselves; grass stays light
                });
            }
        }

        // debris obstacles (logs, stumps) — quad steers around these
        if (_debrisLayer != null)
        {
            foreach (var (dx, dy, dv) in _debrisLayer.InRange(
                _cameraPosition.Y - maxTreeWorld - liftPad,
                _cameraPosition.Y + viewWorldH + 60 + liftPad))
            {
                var src = _debrisLayer.Sprites[dv % _debrisLayer.Sprites.Length];
                entities.Add(new WorldEntity
                {
                    SortY = dy,
                    Tex = _debrisLayer.Atlas,
                    Src = src,
                    X = dx,
                    BaseY = dy,
                    Scale = zoom,
                    Shadow = true
                });
            }
        }

        // planter crew
        if (_planters != null && _planterAtlas != null && _planterFrames != null)
        {
            foreach (var p in _planters.Planters)
            {
                Rectangle src;
                SpriteEffects fx = SpriteEffects.None;
                if (p.State == PlanterState.Planting)
                {
                    src = _planterFrames[p.Variant * PLANTER_FRAMES + PLANTER_FRAMES - 1];
                }
                else
                {
                    int dirIdx = p.Dir switch { "S" => 0, "N" => 1, _ => 2 };
                    int frame = p.Walking ? (int)(p.WalkAnim * 8) % WALK_FRAMES : 1;
                    src = _planterFrames[p.Variant * PLANTER_FRAMES + dirIdx * WALK_FRAMES + frame];
                    if (p.Dir == "W") fx = SpriteEffects.FlipHorizontally;
                }
                entities.Add(new WorldEntity
                {
                    SortY = p.Pos.Y + 11,
                    Tex = _planterAtlas,
                    Src = src,
                    X = p.Pos.X,
                    BaseY = p.Pos.Y + 11,
                    Scale = zoom,
                    Fx = fx,
                    Shadow = true
                });
            }
        }

        // player: quad with rider when mounted, foreman when on foot
        if (_mounted)
        {
            AddQuadEntity(entities, _atvPosition, _currentAtvDirection, _atvDirIdx, rider: true, zoom,
                MathF.Max(0f, _chassisLift - Lift(_atvPosition.X, _atvPosition.Y)), _quadTilt);
        }
        else if (_foremanAtlas != null && _foremanFrames != null)
        {
            int dirIdx = _footDir switch { "S" => 0, "N" => 1, _ => 2 };
            int frame = _walking ? (int)(_walkAnim * 8) % WALK_FRAMES : 1;
            var src = _foremanFrames[dirIdx * WALK_FRAMES + frame];
            entities.Add(new WorldEntity
            {
                SortY = _atvPosition.Y + 11,
                Tex = _foremanAtlas,
                Src = src,
                X = _atvPosition.X,
                BaseY = _atvPosition.Y + 11,
                Scale = zoom,
                Fx = _footDir == "W" ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
                Shadow = true
            });
        }

        entities.Sort((a, b) => a.SortY.CompareTo(b.SortY));

        // merge entities with the y-sorted tree stream, respecting the pass filter
        int e = 0;
        void DrawEntitiesUpTo(float y)
        {
            while (e < entities.Count && entities[e].SortY <= y)
            {
                var en = entities[e++];
                DrawWorldSprite(spriteBatch, en.Tex, en.Src, en.X, en.BaseY, en.Scale, en.Fx, en.Shadow, en.LiftExtra, en.Rot);
            }
        }

        if (_treeLayer != null)
        {
            foreach (var (tx, ty, tv) in _treeLayer.InRange(
                _cameraPosition.Y - maxTreeWorld * Tweaks.TreeScale - liftPad,
                _cameraPosition.Y + viewWorldH + maxTreeWorld * Tweaks.TreeScale + liftPad))
            {
                DrawEntitiesUpTo(ty);
                var src = _treeLayer.Sprites[tv % _treeLayer.Sprites.Length];
                DrawWorldSprite(spriteBatch, _treeLayer.Atlas, src, tx, ty,
                    zoom * Tweaks.TreeScale, SpriteEffects.None, shadow: true);
            }
        }
        DrawEntitiesUpTo(float.MaxValue);
    }

    /// <summary>Tire tracks: rotated twin-tread stamps along everywhere the quad has driven.</summary>
    private void DrawTrails(SpriteBatch spriteBatch)
    {
        if (_trackTexture == null || _tracks.Count == 0) return;
        float zoom = _cameraZoom;
        float viewW = _screenWidth / zoom, viewH = _screenHeight / zoom;
        var origin = new Vector2(_trackTexture.Width / 2f, _trackTexture.Height / 2f);

        for (int i = 0; i < _tracks.Count; i++)
        {
            var t = _tracks[i];
            if (t.Pos.X < _cameraPosition.X - 20 || t.Pos.X > _cameraPosition.X + viewW + 20 ||
                t.Pos.Y < _cameraPosition.Y - 20 || t.Pos.Y > _cameraPosition.Y + viewH + 20) continue;
            Vector2 s = WorldToScreen(t.Pos);
            // oldest tracks fade out as they approach eviction
            float fade = Math.Min(1f, (float)i / 400f + 0.25f);
            spriteBatch.Draw(_trackTexture, s, null, Color.White * (0.22f * fade), t.Ang,
                origin, zoom, SpriteEffects.None, 0f);
        }
    }

    /// <summary>Dust puffs drifting in the quad's wake, growing and fading.</summary>
    private void DrawDust(SpriteBatch spriteBatch)
    {
        if (_dustTexture == null) return;
        float zoom = _cameraZoom;
        foreach (var d in _dust)
        {
            float t = d.Age / d.Life;
            Vector2 s = WorldToScreen(d.Pos);
            // road smoke: grey, rises, grows less; dust: tan cloud, billows out
            float grow = d.Smoke ? 1.0f : 1.7f;
            Color tint = d.Smoke ? new Color(118, 118, 124) : Color.White;
            float alpha = (1f - t) * (d.Smoke ? 0.5f : 0.42f);
            float scale = d.Size * (1f + t * grow) * zoom / 24f;
            spriteBatch.Draw(_dustTexture, s, null, tint * alpha, 0f,
                new Vector2(12, 12), scale, SpriteEffects.None, 0f);
        }
    }

    /// <summary>Seedlings in the visible tile range, drawn flat on the ground.</summary>
    private void DrawSeedlings(SpriteBatch spriteBatch)
    {
        if (_planters == null || _seedlingAtlas == null || _seedlingFrames == null || _tileMap == null) return;

        int ts = _tileMap.TileSize, th = _tileMap.TileHeight;
        float zoom = _cameraZoom;
        int playHeight = _screenHeight;
        int tx0 = Math.Max(0, (int)(_cameraPosition.X / ts) - 1);
        int ty0 = Math.Max(0, (int)(_cameraPosition.Y / th) - 1);
        int tx1 = Math.Min(_tileMap.Width - 1, (int)((_cameraPosition.X + _screenWidth / zoom) / ts) + 1);
        int ty1 = Math.Min(_tileMap.Height - 1, (int)((_cameraPosition.Y + playHeight / zoom + _tileMap.MaxElev) / th) + 1);

        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
            {
                int count = _planters.PlantedAtTile(tx, ty);
                for (int s = 0; s < count; s++)
                {
                    Vector2 pos = PlanterSystem.SpotPos(tx, ty, s, ts, th);
                    var src = _seedlingFrames[(tx * 31 + ty * 17 + s) % _seedlingFrames.Length];
                    Vector2 screen = WorldToScreen(pos);
                    spriteBatch.Draw(_seedlingAtlas, screen, src, Color.White, 0f,
                        new Vector2(src.Width / 2f, src.Height), zoom * 0.7f, SpriteEffects.None, 0f);

                    // quality reveal: green = good tree, red = fault
                    if (_reveals.TryGetValue((tx, ty, s), out float ttl))
                    {
                        bool bad = _planters.IsFault(tx, ty, s);
                        Texture2D mark = GetOrCreateTexture(bad ? "revealBad" : "revealGood",
                            bad ? new Color(214, 60, 48) : new Color(84, 200, 90));
                        float alpha = Math.Min(1f, ttl / 0.6f);
                        spriteBatch.Draw(mark,
                            new Rectangle((int)screen.X - 3, (int)(screen.Y - src.Height * zoom * 0.7f) - 10, 7, 7),
                            Color.White * alpha);
                    }
                }
            }
    }

    /// <summary>
    /// Quad entity from the generated 16-direction atlas (parked or with rider);
    /// falls back to the legacy per-direction textures if the atlas is missing.
    /// </summary>
    private void AddQuadEntity(List<WorldEntity> entities, Vector2 pos, string dir, int dirIdx,
        bool rider, float zoom, float bounce, float tilt = 0f)
    {
        if (_quadAtlas != null && _quadFrames != null && _quadFrames.Length >= 32 * 2 * 9)
        {
            // v2 atlas: (boxes * 32 + dir) * 2 + rider — boxes ride the racks visibly
            int boxes = Math.Clamp(_atvBoxes, 0, 8);
            var src = _quadFrames[(boxes * 32 + Math.Clamp(dirIdx, 0, 31)) * 2 + (rider ? 1 : 0)];
            entities.Add(new WorldEntity
            {
                SortY = pos.Y + 12,
                Tex = _quadAtlas,
                Src = src,
                X = pos.X,
                BaseY = pos.Y + 12,
                Scale = zoom,
                LiftExtra = bounce,
                Rot = tilt,
                Shadow = true
            });
        }
        else if (_quadAtlas != null && _quadFrames != null && _quadFrames.Length >= 32)
        {
            // legacy 16-dir atlas
            int d16 = Array.IndexOf(QuadDirOrder, dir);
            if (d16 < 0) d16 = 8;
            var src = _quadFrames[d16 * 2 + (rider ? 1 : 0)];
            entities.Add(new WorldEntity
            {
                SortY = pos.Y + 12,
                Tex = _quadAtlas,
                Src = src,
                X = pos.X,
                BaseY = pos.Y + 12 - bounce,
                Scale = zoom,
                Rot = tilt,
                Shadow = true
            });
        }
        else if (_atvTextures.ContainsKey(dir))
        {
            Texture2D quad = _atvTextures[dir];
            float wh = quad.Height * 0.15f / zoom;
            entities.Add(new WorldEntity
            {
                SortY = pos.Y + wh / 2f,
                Tex = quad,
                Src = new Rectangle(0, 0, quad.Width, quad.Height),
                X = pos.X,
                BaseY = pos.Y + wh / 2f - bounce,
                Scale = 0.15f
            });
        }
    }

    /// <summary>Prompt badges over interaction targets, plus box pips over caches.</summary>
    private void DrawPrompts(SpriteBatch spriteBatch)
    {
        if (_badgeE != null && !_mounted &&
            Vector2.Distance(_atvPosition, _parkedAtvPos) < INTERACT_RANGE)
            DrawBadge(spriteBatch, _badgeE, _parkedAtvPos + new Vector2(0, -34));

        // planter state badges: idle "!" (fix it) and done "✓" (come move them)
        if (_planters != null)
            foreach (var p in _planters.Planters)
            {
                if (p.State == PlanterState.Idle && _badgeAlert != null)
                    DrawBadge(spriteBatch, _badgeAlert, p.Pos + new Vector2(0, -30));
                else if (p.State == PlanterState.Done && _badgeDone != null)
                    DrawBadge(spriteBatch, _badgeDone, p.Pos + new Vector2(0, -30));
            }

        // T badge: on foot next to a working planter = coach them
        if (!_mounted && _badgeT != null && _planters != null)
        {
            var coachee = _planters.FindCoachTarget(_atvPosition);
            if (coachee != null && coachee.CoachTimer <= 0)
                DrawBadge(spriteBatch, _badgeT, coachee.Pos + new Vector2(22, -30));
        }

        var (action, target) = GetBoxAction();
        if (action != BoxAction.None && _badgeQ != null)
            DrawBadge(spriteBatch, _badgeQ, target + new Vector2(0, action == BoxAction.PlaceCache ? -10 : -48));

        // C badge over a cache when a line-in is possible from it
        if (!_aiming && _badgeC != null && _planters != null)
            foreach (var c in _caches)
                if (Vector2.Distance(_atvPosition, c.Pos) < 70f)
                {
                    if (_planters.FindLinePlanter(c.Pos) != null)
                        DrawBadge(spriteBatch, _badgeC, c.Pos + new Vector2(22, -48));
                    break;
                }

        // cache fill pips
        Texture2D pip = GetOrCreateTexture("cachePip", new Color(226, 222, 210));
        Texture2D pipStripe = GetOrCreateTexture("cachePipStripe", new Color(44, 96, 58));
        foreach (var c in _caches)
        {
            Vector2 screen = WorldToScreen(c.Pos + new Vector2(0, -_cacheTexture.Height + 6));
            int n = Math.Min(c.Boxes, 8);
            for (int i = 0; i < n; i++)
            {
                var r = new Rectangle((int)(screen.X - n * 7 + i * 14), (int)screen.Y, 12, 8);
                spriteBatch.Draw(pip, r, Color.White);
                spriteBatch.Draw(pipStripe, new Rectangle(r.X, r.Y + 3, 12, 2), Color.White);
            }
        }
    }

    /// <summary>Ground elevation lift at a world position (0 when no relief data).</summary>
    private float Lift(float wx, float wy) => _tileMap?.ElevationAt(new Vector2(wx, wy)) ?? 0f;


    private Vector2 WorldToScreen(Vector2 world) => new Vector2(
        (world.X - _cameraPosition.X) * _cameraZoom,
        (world.Y - Lift(world.X, world.Y) - _cameraPosition.Y) * _cameraZoom);

    private void DrawBadge(SpriteBatch spriteBatch, Texture2D badge, Vector2 worldPos)
    {
        Vector2 s = WorldToScreen(worldPos);
        spriteBatch.Draw(badge, new Rectangle((int)s.X - 14, (int)s.Y - 14, 28, 28), Color.White);
    }

    /// <summary>Carried-box indicator under the speedometer.</summary>
    private void DrawBoxHud(SpriteBatch spriteBatch)
    {
        int boxes = _mounted ? _atvBoxes : (_carryingBox ? 1 : 0);
        if (boxes <= 0) return;
        Texture2D face = GetOrCreateTexture("boxFace", new Color(226, 222, 210));
        Texture2D stripe = GetOrCreateTexture("boxStripe", new Color(44, 96, 58));
        for (int i = 0; i < boxes; i++)
        {
            var r = new Rectangle(240 + i * 28, HudBase - 28, 24, 16);
            spriteBatch.Draw(face, r, Color.White);
            spriteBatch.Draw(stripe, new Rectangle(r.X, r.Y + 6, 24, 3), Color.White);
        }
    }

    /// <summary>
    /// Draw a world sprite anchored bottom-center at (wx, wy); scale is the
    /// final screen scale. Elevation lifts the draw; suspension/airtime lifts
    /// further and detaches the shadow (the airtime read).
    /// </summary>
    private void DrawWorldSprite(SpriteBatch spriteBatch, Texture2D tex, Rectangle src,
        float wx, float wy, float scale, SpriteEffects fx = SpriteEffects.None,
        bool shadow = false, float liftExtra = 0f, float rot = 0f)
    {
        float zoom = _cameraZoom;
        float destX = (wx - _cameraPosition.X) * zoom;
        if (destX < -150 || destX > _screenWidth + 150) return;

        float groundY = (wy - Lift(wx, wy) - _cameraPosition.Y) * zoom;
        float destY = groundY - liftExtra * zoom;
        if (destY - src.Height * scale > _screenHeight || destY < -20) return;

        // REAL cast shadow: one sun, high in the NW (the same light the
        // terrain hillshade uses). The sprite's own silhouette is flipped at
        // the feet (negative y scale), foreshortened, and rotated so it lies
        // toward the SE. Airborne sprites push the shadow further along the
        // sun ray and fade it — the detachment is the airtime read.
        bool airborne = liftExtra > 2.5f;
        if (shadow || airborne)
        {
            // Top-down drop shadow: the sprite's own outline, same
            // orientation, offset toward the SE (sun high NW) and drawn under.
            // Offset grows with sprite height — tall trees throw farther than
            // logs — and with airtime, where it also fades (the airtime read).
            float len = MathF.Min(14f, src.Height * scale * 0.22f) + liftExtra * zoom * 0.6f;
            var shPos = new Vector2(destX + len * 0.9f, groundY + len * 0.55f);
            float a = airborne ? 0.15f : 0.24f;
            spriteBatch.Draw(tex, shPos, src, Color.Black * a, rot,
                new Vector2(src.Width / 2f, src.Height), scale, fx, 0f);
        }

        // rotation pivots at the bottom-center anchor: the wheels stay planted
        // while the body leans with the slope
        spriteBatch.Draw(tex, new Vector2(destX, destY), src, Color.White, rot,
            new Vector2(src.Width / 2f, src.Height), scale, fx, 0f);
    }

    private void DrawSpeedometer(SpriteBatch spriteBatch)
    {
        // Calculate current speed as percentage of max speed
        float currentSpeed = _atvVelocity.Length();
        float speedPercent = currentSpeed / Tweaks.GearMax[5];

        // Background bar — bottom row of the shaded vista band
        Rectangle speedometerBg = new Rectangle(
            10, HudBase - 30,
            150, 20  // Size
        );

        // Draw filled bar based on speed
        Rectangle speedometerFill = new Rectangle(
            10, HudBase - 30,
            (int)(150 * speedPercent), 20  // Size - width scaled by speed percentage
        );
        
        // Draw the speedometer
        spriteBatch.Draw(GetOrCreateTexture("speedometerBg", Color.DarkGray), speedometerBg, Color.White);
        spriteBatch.Draw(GetOrCreateTexture("speedometerFill", Color.Green), speedometerFill, Color.White);
    }
    
    private Texture2D GetOrCreateTexture(string textureName, Color color)
    {
        // Solid color textures for UI elements, created once and cached
        if (_uiTextures.TryGetValue(textureName, out Texture2D cached)) return cached;
        Texture2D texture = new Texture2D(Game.GraphicsDevice, 1, 1);
        texture.SetData(new[] { color });
        _uiTextures[textureName] = texture;
        return texture;
    }
} 