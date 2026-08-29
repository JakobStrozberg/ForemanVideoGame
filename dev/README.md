# dev/ — tooling

## ArtTool — procedural asset pipeline

Console tool that **generates** environment art from code and outputs game-ready
maps: rendered PNG and tile terrain data **in one pass from the same geometry**,
so art and collision never drift apart. Only man-made props (trucks) are reused
sprite brushes — ground, trees, roads, and boundaries are all procedural and seeded.

Run everything through the scripts:

```bash
dev/scripts/regen.sh          # sprite atlases + every block in assets/blocks/
dev/scripts/regen.sh Block1   # one block's map only
dev/scripts/run.sh [Block]    # build + launch the desktop game into a block
```

### Commands

```bash
# a block: <Name>.png + <Name>.{tiles,trees,debris,veg}.json into the map folder
dotnet run --project dev/ArtTool -c Release -- compose assets/blocks/Block1.json assets/brushes game/Content/Maps/Block1

# every runtime sprite atlas: trees, debris, veg, quad, foreman, planters,
# seedlings, cache, prompt badges, font
dotnet run --project dev/ArtTool -c Release -- sprites game/Content/GameTextures/Generated
```
The game loads generated files at runtime from `game/Content`; the desktop head
copies them to its output on build. F5 in-game rebuilds and relaunches.

### Generator architecture

| File | Generates |
|---|---|
| `GamePalette.cs` | Master 16-bit color ramps (soil, wood, cream, conifer, grass, road, trunk). Every generator draws only from these — that's what makes everything read as one game. |
| `Noise.cs` | Deterministic hash noise, tileable fractal value noise. |
| `TextureGen.cs` | Seamless 512px ground textures: slash, cream, swamp, rock, forest floor; road/trail strips with wheel ruts and dissolving edges. |
| `TreeGen.cs` | Conifer sprite atlas: jagged lit canopy, per-tree color jitter, standing-dead snags. |
| `PropGen.cs` | Cache tent, seedlings, debris (logs, stumps), vegetation, prompt badges, the bitmap font. |
| `FigureGen.cs` | Foreman and planter walk/plant atlases. |
| `QuadGen.cs` | The quad: 32 directions x (parked, rider) x boxes on the racks. |
| `RoadPath.cs` | Catmull-Rom road splines with distance-field queries; drawn road = drivable road. |
| `Compositor.cs` | Block assembly: irregular boundary, organic land regions (noise-jittered edges), roads with tree-cleared corridors, calmed ground textures, terrain relief with hillshade, contour lines, hypsometric tint, and cast shadows from one NW sun; tile grid + elevation extraction. |

Everything is seeded — same block JSON in, identical map out. Change `seed`, get a sibling block.

### Block definition (`assets/blocks/*.json`)

```jsonc
{
  "name": "Block1",
  "tileSize": 16,
  "width": 160, "height": 172,    // tiles (16 wide x 11 tall — 3/4 view; one tree per tile)
  "seed": 7,
  "pixelSize": 1,                 // pixel-grid unify factor (1 = full detail)
  "hilliness": 1.0,               // 0 = billiard table, 1 = full zoned relief
  "boundary": {                   // irregular block outline
    "centerTile": [40, 36],
    "extentTiles": [33, 29],
    "roughness": 0.14
  },
  "landRegions": [                // organic blobs; later entries paint over earlier
    { "type": "cream", "x": 12, "y": 40, "w": 16, "h": 17 },
    { "type": "swamp", "x": 58, "y": 43, "w": 13, "h": 14 },
    { "type": "rock",  "x": 60, "y": 14, "w": 11, "h": 13 }
  ],
  "leavePatches": [ { "x": 30, "y": 20, "w": 10, "h": 11 } ],   // standing-timber islands
  "roads": [
    // kind: fsr (access road) | block (in-block road) | trail (quad trail)
    // width in tiles (fractions allowed); a Catmull-Rom spline runs through the points
    { "kind": "fsr",   "width": 1.5, "points": [[0, 72], [12, 74], [28, 70], [44, 76], [60, 72], [79, 74]] },
    { "kind": "block", "width": 1,   "points": [[52, 73], [49, 60], [53, 47], [50, 33], [52, 20]] },
    { "kind": "trail", "width": 0.5, "points": [[51, 43], [43, 47], [34, 41], [26, 46], [24, 53]] }
  ],
  "props": [ { "type": "truck", "tile": [16, 73] } ]   // sprite brush, impassable
}
```
Roads are smooth splines rendered by a distance field, and the terrain grid is
marked from the same field (padded so narrow roads still give a continuous
drivable corridor). Trees auto-clear 26px along every road. Caches are NOT map
props — the player places them in-game. A new map is ~25 lines of JSON.

### Tile legend (read dynamically by `game/Maps/TileMap.cs`)

| Char | Terrain | Passable | Speed |
|------|---------|----------|-------|
| F | forest | no | 0 |
| S | slash | yes | 0.55 |
| C | cream | yes | 0.85 |
| W | swamp | yes | 0.25 |
| X | rock | yes | 0.7 |
| R | road (fsr/block) | yes | 1.0 |
| T | quad trail | yes | 0.8 |
| O | obstacle (truck) | no | 0 |

Add a terrain here and the game picks it up without code changes.

### Sprite brushes (`assets/brushes/`)

`Truck.png` — the only hand-made sprite the compositor still places.
