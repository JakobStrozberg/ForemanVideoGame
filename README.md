# Crewboss

A 16-bit top-down strategy game about running a treeplanting crew: drive the
quad, keep the caches stocked, move the planters, get the block in. Built with
C# / .NET 8 / MonoGame. Desktop now; Android and iOS heads planned.

## Layout

```
game/            ALL gameplay code — one class library shared by every platform
  Core/          bootstrap (CrewbossGame), screens plumbing, Tweaks, font, Input
  Maps/          WorldMap, TileMap, TreeLayer — a block at runtime + terrain queries
  Mechanics/
    Quad/        driving physics, gears, drift, suspension + tilt, dust/smoke/tracks
    Player/      the crewboss on foot: mount/dismount, boxes, caches, coaching
    Planters/    crew AI, line-ins, pathfinding, caches
    Day/         the day clock and scoring
  Rendering/     Camera, Presenter (pixel-art rig), GameArt, WorldRenderer, Hud
  Screens/       MainMenu, Gameplay (thin orchestrator)
  Content/       tweaks.json, GameTextures, Maps/<Block>/ (generated), Content.mgcb
platforms/
  Desktop/       DesktopGL head — window, icon, content build. No gameplay code.
dev/
  ArtTool/       procedural asset generator (maps, sprite atlases)
  scripts/       run.sh, regen.sh
assets/          generator inputs: block definitions, sprite brushes, palette
docs/            design docs + reference photos
```

## Run

```bash
dev/scripts/run.sh            # build + launch straight into Block1
dev/scripts/run.sh Block1     # any block under game/Content/Maps/
```
Or open `Crewboss.sln` and run the `Crewboss.Desktop` project. Esc quits,
F5 rebuilds and relaunches into the same block, H hides the controls card.

## Regenerate art

Maps and sprite atlases are generated, not drawn. After editing a block
definition in `assets/blocks/` or a generator in `dev/ArtTool/`:

```bash
dev/scripts/regen.sh          # all sprite atlases + every block
dev/scripts/regen.sh Block1   # one block's map only
```
See [dev/README.md](dev/README.md) for the generator pipeline and the block
definition format.

## Tuning

`game/Content/tweaks.json` holds live feel values (gearing, foot speed, dust,
zoom). Edit, press F5 in-game.

## Adding a block

1. Copy `assets/blocks/Block1.json`, rename, change the seed and geometry.
2. `dev/scripts/regen.sh <Name>` writes `game/Content/Maps/<Name>/`.
3. Point a menu button (or `CREWBOSS_AUTOSTART=<Name>`) at it.

## Mobile

Gameplay code never touches raw devices or window size: input arrives as
intents through `Core/Input/GameInput`, and rendering goes through the
`Presenter`. An Android or iOS head is a MonoGame platform project under
`platforms/` that references `game/` and adds a touch source to `GameInput`.
