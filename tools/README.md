# ArtTool — Crewboss procedural asset pipeline

Console tool that **generates** environment art from code and outputs game-ready maps: rendered PNG and tile terrain data **in one pass from the same geometry**, so art and collision never drift apart. Only man-made props (trucks, cache tents) are reused sprite brushes — ground, trees, roads, and boundaries are all procedural and seeded.

## Commands

### Compose a block
```bash
dotnet run --project tools/ArtTool -c Release -- compose assets/blocks/Block1.json assets/brushes src/Content/GameTextures/Maps/Generated
```
Outputs `<Name>.png` + `<Name>.tiles.json` (~2.5s for an 80x60 block, ~8k trees). The game loads both at runtime — rebuild the game (`dotnet build`) so they copy to the output directory.

### Generate horizon "View" layers
```bash
dotnet run --project tools/ArtTool -c Release -- horizon src/Content/GameTextures/Generated
```
Sky + far ridge + mid ridge + treeline, all tiling in X. The game draws them as a parallax band across the top of the screen (horizontal-only scroll at 3% / 8% / 18% of camera X).

### Generate runtime prop sprites
```bash
dotnet run --project tools/ArtTool -c Release -- sprites src/Content/GameTextures/Generated
```
Currently `Cache.png` — caches are **player-placed at runtime**, never baked into maps.

### Extract a palette from reference images
```bash
dotnet run --project tools/ArtTool -- palette <imageDir> assets/palette.json 32
```
(Utility for future sprite ingest; the generators themselves use the fixed ramps in `GamePalette.cs`.)

## Generator architecture

| File | Generates |
|---|---|
| `GamePalette.cs` | Master 16-bit color ramps (soil, wood, cream, conifer, grass, road, trunk). Every generator draws only from these — that's what makes everything read as one game. |
| `Noise.cs` | Deterministic hash noise, tileable fractal value noise. |
| `TextureGen.cs` | Seamless ground textures: slash (soil + fallen logs + stumps + tufts), cream, forest floor; road/trail strips with wheel ruts and noisy dissolving edges. |
| `TreeGen.cs` | Individual conifer sprites: jagged lit canopy, per-tree color jitter, ~4% standing-dead snags. Forests are thousands of y-sorted trees, so any boundary shape works. |
| `PropGen.cs` | Generated prop sprites: tree cache (silver tarp A-frame over stacked seedling boxes, crossed peak poles). |
| `Compositor.cs` | Block assembly: irregular radial-polygon boundary, organic blob regions (union-of-circles + noisy edge), road hierarchy with tree-cleared corridors, per-cell texture flips against repetition, whole-map pixel-grid unify pass, terrain grid extraction. |

Everything is seeded — same block JSON in, identical map out. Change `seed`, get a sibling block.

## Block definition (`assets/blocks/*.json`)

```jsonc
{
  "name": "Block1",
  "tileSize": 32,
  "width": 80, "height": 60,      // tiles
  "seed": 7,
  "pixelSize": 2,                 // final pixel-grid unify factor
  "boundary": {                   // irregular block outline
    "centerTile": [40, 25],       // optional, default map center
    "extentTiles": [33, 20],      // optional half-extents
    "roughness": 0.14             // edge wobble
  },
  "landRegions": [                 // organic blobs; later entries paint over earlier
    { "type": "cream", "x": 12, "y": 28, "w": 16, "h": 12 },
    { "type": "swamp", "x": 58, "y": 30, "w": 13, "h": 10 },
    { "type": "rock",  "x": 60, "y": 10, "w": 11, "h": 9 }
  ],
  "leavePatches":  [ { "x": 30, "y": 14, "w": 10, "h": 8 } ],   // standing-timber islands
  "roads": [
    // kind: fsr (access road to block) | block (in-block road) | trail (quad trail)
    // free-form control points — a Catmull-Rom spline runs through them
    { "kind": "fsr",   "width": 3, "points": [[0, 50], [12, 52], [28, 49], [44, 53], [60, 50], [79, 52]] },
    { "kind": "block", "width": 2, "points": [[52, 51], [49, 42], [53, 33], [50, 23], [52, 14]] },
    { "kind": "trail", "width": 1, "points": [[51, 30], [43, 33], [34, 29], [26, 32], [24, 37]] }
  ],
  "props": [
    { "type": "truck", "tile": [16, 51] }    // sprite brush, impassable
  ]
}
```
Roads are smooth splines rendered by a distance field — ruts bend with the curve, edges dissolve into ground, and the terrain grid is marked from the same field so drawn road = drivable road. Trees auto-clear along every corridor. Caches are NOT map props (player places them in-game). A new map is ~25 lines of JSON.

## Tile legend (shared with `src/TileMap.cs`)

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

The game reads the legend dynamically — add a terrain here and it works in-game without code changes.

## Remaining sprite brushes (`assets/brushes/`)

`Truck.png` — the only remaining sprite brush. Everything else in that folder is legacy and no longer referenced by the compositor.
