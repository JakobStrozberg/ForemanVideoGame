# Crewboss — Master Design Document

A 16-bit real-time strategy game about being a tree-planting crewboss, built with C# / .NET 8 / MonoGame. Based on the real job: you and 6–11 planters drive to a block, and your day is a rolling logistics-and-people puzzle played on foot and from the seat of an ATV.

> Companion doc: [DESIGN-PHASE1.md](DESIGN-PHASE1.md) — the detailed vertical-slice spec.

---

## 1. Vision

Most RTS games put you above the map commanding units. Crewboss inverts it: **you don't command a workforce, you serve one.** Planters are piecework contractors who plant autonomously and earn per tree — your job is to remove everything that slows them down: empty caches, closed-out pieces, bad pairings, slipping quality, sagging morale. And you do it all from one body on the block.

Tone and fiction come from real Canadian tree planting: blocks, FSRs, reefer trucks, caches, bag-ups, cream and slash, highballers, repo. The game should feel like the job — strategic, physical, human.

**Elevator pitch:** Pikmin meets a dispatch sim, set in the BC bush, where your workers work for themselves and the enemy is distance, time, and morale.

## 2. Design Pillars

1. **You are physically on the block.** No god-camera, no click-orders. Everything happens through your avatar — mounted on the ATV or on foot. Distance and time are the core currency.
2. **Planters work for themselves.** Piecework economy. They plant autonomously; idle planters lose money and get angry. You enable, not command.
3. **Information is earned.** Cache levels, planter progress, quality, and mood are only visible up close. You learn the state of the block by moving through it — patrol is gameplay.
4. **One body, ten problems.** Every mechanic feeds one triage question: refill the far cache, move the finished planter, or check the rookie's line? You can only be one place.

## 3. The Day Loop (one day = one level, ~12 real minutes)

| Beat | What happens |
|------|--------------|
| **Pre-game map** | Block overview screen: roads, boundary, land types, cache-able road edges. Plan your attack — where to cut in, where caches go. |
| **Morning** | Truck arrives at block entrance with tree boxes. Load the ATV, drive out, cut in each planter into an opening piece. |
| **Midday** | The real game. Planters plant, caches drain, pieces close out, quality drifts, morale moves. You triage in real time. |
| **Day end** | Timer expires. Score screen: trees in ground, quality %, repo count, idle time, morale delta, crew earnings. Star rating. |

## 4. Player Avatar

Two locomotion states, one Mount/Dismount action when adjacent to the ATV.

### Mounted (ATV)
- Fast on roads, slower off-road (terrain multipliers).
- Cargo: **4 tree boxes**.
- Drive-by reads only: rough cache levels, planter state icons. No close interactions from the seat.
- Reuses the existing quad physics and 16-direction sprite set.

### On Foot
- Slow, goes anywhere (including terrain the quad can't handle — swamp, steep slash).
- Carries **1 box** — a top-up, not a logistics strategy.
- Required for all close interactions: quality check, coaching, exact cache counts, conversations.
- The ATV stays where parked. Stranding it in the wrong place is a self-inflicted time tax — intentional.

### Context interactions (single interact button, prompt by proximity)
Load/unload boxes · mount/dismount · cut in · check quality · coach · move planter · partner planters · talk (morale read).

## 5. Land & Blocks

- Tile grid underneath the pixel-art map: road / plantable / boundary / obstacle, plus land type.
- **Land types** affect plant speed, quality risk, and morale:
  - **Cream** — open soft ground. Fast, happy, low fault risk.
  - **Slash** — logging debris. Slow, angry, higher fault risk.
  - **Swamp** — slow, quad can't cross, morale drain.
  - **Rock** — sparse plantable spots, high fault risk.
- Roads (FSRs) are the arteries: fast ATV travel, cache placement, block access.
- Fairness is a mechanic: who gets the cream and who eats the slash is a morale decision you make every time you cut someone in.

## 6. Cut-In & Pieces

The signature spatial mechanic, taken straight from the job.

- A planter follows you; on "start line" they plant a visible row along your path; "end line" stops it.
- **A piece = an enclosed region** bounded by cut lines, roads, and the block boundary (flood-fill detection on the tile grid).
- Release a planter into an enclosed region and it's theirs; they fill it autonomously.
- Piece sizing is skill: too small and they finish before you're back; too big and the day ends with holes. Planter speed variance (Phase 2) makes this a real read — you size pieces to people.

## 7. Planters

6–11 autonomous agents. The heart of the game.

### FSM
```
IDLE → FOLLOWING (cut-in) → PLANTING ⇄ BAG-UP (walk to cache, refill)
                                     → OUT_OF_TREES (cache dry) → IDLE
                                     → PIECE_DONE → FOLLOWING (move) → PLANTING
```

### Stats (data-driven, JSON)
| Stat | Effect |
|---|---|
| **Speed** | Trees/hour. Drives piece sizing and pairing decisions. |
| **Quality** | Base fault risk and how fast their quality meter drifts. |
| **Stamina** | Fade curve after lunch — speed and quality sag when tired. |
| **Morale** | Multiplier on everything. The stat you manage rather than hire. |

### Personalities (archetypes)
- **Highballer** — very fast, ego. Hates small pieces and slow partners; demands cream.
- **Rookie** — slow, cheap, high drift. Needs cream and coaching; grows over a season.
- **Veteran** — steady, low drift, morale anchor for planters near them.
- **Wildcard** — fast but streaky; quality swings, morale volatile.

### Morale
Moves on: land quality of assigned piece · idle time (worst offender — idle piecework = losing money) · dry caches · pairings (friends up, rivals down) · being coached respectfully vs. checked constantly · weather (campaign). Low morale → slower, sloppier, and in the campaign, quits.

### Partnering
Two planters in one piece. Uses: pair a rookie with a veteran (drift slows, rookie learns), merge two nearly-done planters into one big piece, manage a highballer's boredom. Bad pairings backfire on morale.

## 8. Caches & Tree Logistics

- **Truck** at the block entrance: the source. Finite boxes per day in later phases — order the right amount at pre-game.
- **Cache** = boxes dropped at a road edge. Planters auto-bag-up from their nearest cache.
- Burn rate = the planters drawing from it. A dry cache idles everyone on it — the cascading failure the whole game orbits.
- Visibility follows pillar 3: exact counts on foot, rough pips on a drive-by, nothing from afar.

## 9. Quality, Checking & Repo

Quality is **hidden state that drifts** — surfacing it costs body-time, the scarcest resource.

- Each planter has a hidden quality meter that drifts downward — faster when tired, unhappy, or in bad land. Trees planted while it's low are silently flagged as **faults**.
- **Check quality (manual, on foot):** walk a planter's planted line; trees in your inspection radius reveal green/red. A few seconds of walking gives you a read on where their meter sits.
- **Coach:** after a check, talk to the planter — meter resets, small morale bump if they weren't idle-angry, costs them a few seconds of planting. Catching a slip early is the whole point: faults already in the ground stay there.
- **The Checker (NPC, Phase 2+):** arrives unannounced, samples pieces. Failed sample → **repo**: the planter replants that section unpaid. Big morale hit, big score hit, and the crew remembers whose piece it was.
- Player skill: build a mental model of who slips when, and spend your patrol time where the risk is.

## 10. Economy & Scoring

- Piecework: planters earn per tree; the crew's day earnings are your headline number.
- Day score: trees planted · fault/repo count · total idle seconds · morale delta → composite 1–3 star rating.
- Campaign: contract pays per tree; costs (camp, fuel, tree orders) come out; season profit and crew retention are the long game.

## 11. Meta Layer — Season Campaign

- A **contract** = a sequence of blocks over a season.
- **Roster:** hire, retain, lose planters. Rookies grow; stars quit if morale stays low. Skills persist.
- **Pre-day decisions:** tree order size, cache plan, who's on the crew today.
- **Weather days**, camp events, and contract deadlines create season texture.
- Structure is lightly roguelite: a season is a run; crew and reputation carry the narrative.

## 12. Controls

- **Desktop (first):** WASD/arrows move, E/Space contextual interact, Tab minimap (terrain + you only — no intel).
- **Mobile (Phase 4):** virtual stick + single context button; same one-button interaction design is the mobile insurance policy.

## 13. Phase Plan

| Phase | Contents | Proves |
|---|---|---|
| **1 — Vertical slice ✅ DONE (July 2026)** | One block (procedurally generated, 4 land types + relief), 4 planters, mount/dismount, cache placement + line-ins + pieces, box shuttling, quality drift + walk-the-line check + coach, day timer, score screen, pre-game overview. Spec + as-built deviations: [DESIGN-PHASE1.md](DESIGN-PHASE1.md). | The core loop is fun. |
| **2 — Depth** | Stat variance, personalities, morale, partnering, land types, checker NPC + repo, 8–11 planters, pre-game map planning, finite tree orders. | The people layer creates stories. |
| **3 — Campaign** | Season structure, roster/hiring, contracts, weather, progression, save/load. | Long-game pull. |
| **4 — Polish & mobile** | Touch controls, audio, juice, tutorial (your first day as a rookie crewboss), balancing. | Shippable. |

## 14. Technical Architecture

Existing base (kept): MonoGame DesktopGL, screen architecture (`Game1` → `ScreenManager` → `Screen`), ATV physics + 16-direction sprites, menu flow.

To build:
1. **Tile layer** — grid data (terrain, land type) aligned under map art; maps become art + JSON data instead of bare JPEGs.
2. **Avatar states** — mounted/on-foot, context-interaction prompt system.
3. **Planter agents** — FSM + A* pathfinding on the grid.
4. **Piece system** — cut-line recording → planted-row tiles → flood-fill region detection and assignment.
5. **Inventory** — truck/cache/ATV/bag box transfers; planter auto-bag-up.
6. **Quality system** — hidden meters, drift, walk-the-line reveal, coaching.
7. **Sim clock** — day timer, drift/burn ticks, end-of-day scoring.
8. **HUD** — context prompts, carried boxes, day clock. Deliberately no global status bars (pillar 3).
9. Data-driven definitions (planters, land types, blocks) in JSON from day one.

Known cleanup owed in current code: hardcoded absolute asset paths in `GameplayScreen.cs`, per-frame texture creation in `GetOrCreateTexture`, swapped Map1/Map2 loads.

## 15. Locked Decisions

- Avatar-locked control — no click-orders, ever. The constraint is the game.
- Desktop-first; mobile after the loop is proven.
- ~12-minute days.
- One context-sensitive interact button; no mid-game menus.
- Quality checking is manual and physical, not a UI readout.
- Info by proximity — no omniscient HUD.
