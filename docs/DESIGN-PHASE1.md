# Crewboss — Phase 1 Design Doc (Vertical Slice)

> **STATUS: ✅ COMPLETE (July 2026).** All eight build-order steps shipped and playable.
> Deviations from this spec, decided during build:
> - **Controls**: context keys instead of one interact button — `E` mount/dismount, `Q` boxes/caches, `F` crew pickup/drop, `C` line-in, `T` coach.
> - **Cut-in redesigned into the line-in**: drop + stock a cache first, then `C` aims an arrow from the cache and the planter marches the bearing solo, planting the line (bags up from that cache). Cut lines still bound flood-filled pieces.
> - **Presentation grew beyond spec**: oblique projection baked into map generation (32x22 tiles), terrain elevation relief with hillshade + cast shadows, living horizon ridge, parallax View band, sprite drop shadows, runtime tree layer with tips-first horizon cresting.
> - **Numbers as shipped**: 8-minute day, 40-tree bags, 4-planter crew with per-planter quality drift, camera zoom 1.7.

A 16-bit real-time strategy game about being a tree-planting crewboss. You don't command an army — you serve a crew. One body, ten problems, real time.

## Design Pillars

1. **You are physically on the block.** No god-camera, no click-orders. Everything happens through your avatar — mounted on the ATV or on foot. Distance and time are the core currency.
2. **Planters work for themselves.** Piecework: they plant autonomously and earn per tree. Your job is removing everything that stops them — empty caches, closed-out pieces, bad pairings, slipping quality.
3. **Information is earned.** Cache levels, planter progress, and tree quality are only visible up close. You learn the state of the block by moving through it.

## Phase 1 Goal

Prove the core loop is fun with the smallest complete day:

> Load trees → cut in planters → shuttle trees to caches → check quality and coach → move finished planters → day ends → score.

If this loop creates "one more day" pull with 4 identical-stat planters on one block, the game works.

---

## The Day Loop

| Beat | What happens |
|------|--------------|
| Pre-game map | Static block map screen: roads, block boundary, cache-able road edge. Player taps "Start Day." (Static image fine for Phase 1.) |
| Morning | Truck parked at block entrance with tree boxes. Player loads ATV, drives out, cuts in each planter. |
| Midday | The real game. Planters plant, caches drain, pieces close out, quality drifts. Player triages. |
| Day end | Timer expires (12 real minutes). Score screen. |

## Player Avatar

Two locomotion states, toggled with a single Mount/Dismount action when adjacent to the ATV.

### Mounted (ATV)
- Fast on roads, slower off-road (terrain multiplier).
- Carries tree boxes: capacity **4 boxes**.
- Existing quad physics and 16-direction sprites reused as-is.
- Cannot do close interactions (quality check, coaching) from the seat.

### On Foot
- Slow, but goes anywhere.
- Carries **1 box** (top-up option, not a logistics strategy).
- Required for close interactions: quality check, coach planter, inspect cache exact count.
- ATV stays where parked — a resource you can strand in the wrong place. Walking back to a badly parked quad is a self-inflicted time tax. This is intentional.

### Interactions (context prompt when in range)
| Interaction | Requires | Effect |
|---|---|---|
| Load / unload boxes | At truck or cache | Transfer boxes ATV ↔ truck/cache |
| Mount / dismount | Adjacent to ATV | Swap locomotion state |
| Cut in | On foot or mounted, adjacent to idle planter | Planter follows you; your path becomes their cut line |
| Check quality | On foot, on a planter's planted line | Walk-the-line inspection (see Quality) |
| Coach | On foot, adjacent to planter, after a check | Corrects quality drift |
| Move planter | Adjacent to planter who has finished piece | Planter follows you to new spot |

## Planters (Phase 1: 4, identical stats)

Autonomous agents with a simple FSM:

```
IDLE → FOLLOWING (cut-in) → PLANTING → OUT_OF_TREES → PLANTING
                                     → PIECE_DONE → FOLLOWING (move) → PLANTING
```

- **PLANTING:** consumes trees from personal bag; plants along rows filling their piece. Visible planted-tree sprites accumulate (the block visibly fills in — key satisfaction).
- **Bag-ups:** when bag empty, walks to assigned cache, refills (fixed bag size), walks back. If cache empty → **IDLE**.
- **IDLE:** stands, visible "!" state. Idle seconds are tracked and scored. Idle = losing money = the failure state to prevent.
- **PIECE_DONE:** piece area fully planted. Stands at piece edge, "✓" state, waits for you.

Phase 1 stats (same for all four): plant speed, walk speed, bag size, quality drift rate. Data-driven from JSON so Phase 2 variance is a data change, not a code change.

## Cut-In & Pieces

- Player walks/drives with planter following; on "start line," planter plants a visible straight-ish row along the player's path; on "end line" the row stops.
- **A piece = enclosed region** bounded by cut lines, roads, and block boundary. Detected by flood fill on the tile grid.
- When a planter is released into an enclosed region, that's their piece; they fill it autonomously.
- Piece size matters: too small → planter finishes fast and waits on you; too big → day ends unfinished. (With identical stats this is pure area math; personalities complicate it in Phase 2.)

## Caches & Tree Logistics

- **Truck** at block entrance: infinite-for-Phase-1 source of boxes.
- **Cache** = spot on road edge where boxes are dropped. Planters auto-refill from their nearest cache.
- Cache display: exact count visible only when player is close (on foot: exact; mounted drive-by: rough low/med/high pips). Far away: nothing. Forces patrol.
- Burn rate = planters drawing from it. Dry cache → planters go IDLE → idle-time score damage.
- Core tension: refill the far cache vs move the finished planter vs cut in the next piece. One body.

## Quality (Phase 1 version — includes manual checking per design intent)

Quality is **hidden state that drifts** — the signature mechanic.

- Each planter has a hidden **quality meter** (0–100). It drifts downward slowly over the day (faster in Phase 2 when tired/unhappy).
- Trees planted while the meter is low are silently flagged as **faults**.
- **Check quality:** dismount, walk along a planter's planted line. Trees within inspection radius reveal as good (green flash) or faulty (red flash). Checking samples the line — a few seconds of walking gives a read on the planter's current meter.
- **Coach:** after a check, interact with the planter → their quality meter resets to 100. Short interaction pause (a few seconds of their planting time — coaching isn't free).
- **Catch them early:** the earlier a slip is caught, the fewer faulted trees exist. Faults already in the ground stay in the ground (Phase 1 — no repo/replant yet).
- **Day end audit:** score counts total faulted trees. High fault count = big score penalty. Phase 2 turns this into the checker NPC + unpaid repo replanting + morale.

Loop this creates: quality is invisible unless you spend body-time surfacing it, and body-time is the scarcest resource. Exactly the real job.

## Scoring (Day End Screen)

- **Trees planted** (total, and % of block filled)
- **Quality** (faulted trees count → penalty)
- **Idle time** (total planter idle seconds → penalty)
- Composite **star rating (1–3)** for the day

## Controls (desktop-first)

- WASD/arrows: move (both locomotion states, existing ATV physics when mounted)
- E / Space: contextual interact (mount, load, cut-in start/stop, check, coach, move)
- Tab: minimap overlay (static block map + player dot only — no planter/cache intel; info is earned by proximity)

## Tech Build Order

1. **Tile layer under the map** — grid data (road / plantable / boundary) aligned to existing map art. Replace JPEG-only maps with map art + JSON tile data.
2. **Avatar states** — mount/dismount, on-foot movement, context-interaction prompt system.
3. **Planter agent** — FSM above, A* pathfinding on the tile grid.
4. **Cut-in + piece detection** — path recording → planted row tiles → flood-fill region detection.
5. **Caches + truck + box carrying** — inventory transfer interactions, planter auto-refill.
6. **Quality system** — hidden meter, drift, walk-the-line check reveal, coach reset.
7. **Day timer + score screen.**
8. **HUD** — context prompts, carried-box indicator, day clock. (No global planter status bar — pillar 3.)

## Explicitly Out of Scope (Phase 2+)

- Planter stat variance, personalities, morale
- Checker NPC, repo replanting
- Partnering mechanics
- Land types (cream/slash/swamp) — Phase 1 block is uniform plantable land
- Multiple blocks, season/campaign, roster
- Weather, stamina/fatigue
- Mobile touch controls
- Save/load
