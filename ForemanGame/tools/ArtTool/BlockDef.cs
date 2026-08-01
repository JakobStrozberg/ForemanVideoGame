namespace ArtTool;

public class BlockDef
{
    public string Name { get; set; } = "Block";
    public int TileSize { get; set; } = 32;
    public int Width { get; set; } = 80;    // tiles
    public int Height { get; set; } = 60;   // tiles
    public int Seed { get; set; } = 1;
    public int PixelSize { get; set; } = 2; // final pixel-grid unify factor
    public double Hilliness { get; set; } = 1.0; // 0 = billiard table, 1 = full zoned relief

    public BoundaryDef Boundary { get; set; } = new();
    public List<LandRegionDef> LandRegions { get; set; } = new();
    public List<BlobDef> LeavePatches { get; set; } = new();
    public List<RoadDef> Roads { get; set; } = new();
    public List<PropDef> Props { get; set; } = new();
}

/// <summary>Organic land-type region inside the block. Later entries paint over earlier ones.</summary>
public class LandRegionDef : BlobDef
{
    /// <summary>cream | swamp | rock (default ground is slash)</summary>
    public string Type { get; set; } = "cream";
}

/// <summary>Irregular block outline: a rectangle-ish radial polygon with noisy radius.</summary>
public class BoundaryDef
{
    public int MarginTiles { get; set; } = 5;      // min forest depth around the block
    public double Roughness { get; set; } = 0.10;  // radius noise amplitude (fraction)
    public int[] CenterTile { get; set; }          // optional; default = map center
    public int[] ExtentTiles { get; set; }         // optional half-extents; default = fill minus margin
}

/// <summary>Organic region: union of random circles seeded inside the given rect (tile coords).</summary>
public class BlobDef
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}

public class RoadDef
{
    /// <summary>fsr = access road to the block (wide) | block = in-block road | trail = quad trail.</summary>
    public string Kind { get; set; } = "block";
    /// <summary>Free-form control points, tile coords. A Catmull-Rom spline runs through them.</summary>
    public List<int[]> Points { get; set; } = new();
    public double Width { get; set; } = 2; // tiles (fractional widths allowed)
}

public class PropDef
{
    public string Type { get; set; } = "truck"; // caches are player-placed at runtime, not map props
    public int[] Tile { get; set; } = new int[2];
}
