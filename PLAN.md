# PLAN — Minecraft-like → Full Playable Game (and better)

Generated 2026-10-13 by an 8-agent code-audit swarm (worldgen, content, survival,
management, A/V, perf, UX, multiplayer). Current-state claims are backed by
file:line evidence in the agent reports; this document condenses them into
dependency-ordered milestones.

Effort scale: **S** = hours, **M** = 1–3 days, **L** = week+, solo dev + AI.
Aspect tags: `WG` worldgen · `CT` content/items · `SV` survival · `MG` management ·
`AV` audio/visual · `PF` performance · `UX` experience · `MP` multiplayer.

## 0. Current state (one paragraph)

Working sandbox: infinite-streaming 16×64×16 chunks (seeded noise, 4 biomes,
threshold caves, 4 ores, trees/cacti, campfires) with worker-thread meshing and
far-LOD; player with instant break/place, 9-slot hotbar, always-available flight;
6 wandering sheep, one-punch kills; day/night, weather, particles, fully
procedural textures and 22 kHz audio; code-built HUD; single JSON save
(`user://minecraft_save.json`: edits + player + time), no menus, no items, no
health, no crafting, unrebindable input.

## 1. Decisions (LOCKED 2026-10-13)

| # | Decision | Answer |
|---|----------|--------|
| D1 | Game identity | **Cozy-leaning sandbox with opt-in danger** — peaceful-leaning default, difficulty/mood toggle; campfires/weather/mobs support it |
| D2 | Multiplayer | **Not now, keep the door open** — the 5 keep-the-door rules (§2) apply to all future work; LAN co-op stays an M4 *option* |
| D3 | World limits | **Keep streaming, target view distance 8–12; keep Y=64** until M4 chunk-store refactor, then consider 128/256 |
| D4 | Assets | **Hybrid** — procedural textures/systems stay; import music (and maybe footsteps) where synthesis is weak |
| D5 | Death penalty | **Softcore: respawn at spawn/campfire, keep inventory** first; drop-on-death entities later |
| D6 | Multi-world layout | **Yes** — `user://worlds/<id>/` in M0 |
| D7 | Flight default | Cozy default; **flight behind a creative toggle** (user-facing mode) |

## 2. Keep-the-door rules (adopt immediately, near-zero cost)

1. Every world mutation goes through `SetBlock`/`ApplyEdit` (one seam to later
   validate/replicate).
2. Worldgen seed is an explicit, persisted value; never `GD.Hash`; never
   unseeded RNG (determinism across versions/machines).
3. Stop adding new `World.Player` reads — introduce an observers list when next
   touching streaming.
4. Player state stays separate from world persistence (multi-player safe).
5. Gameplay logic reads InputMap actions, never raw `Input.*` keys (enables
   rebinding, gamepad, and later command structs).

## 3. Milestones

### M0 — Foundation (unblocks everything)

- `MG` (S) **Atomic + rotating-backup saves** — temp write → rename → keep 2 `.bak`; independent win, do first.
- `MG` `UX` (M) **UI/menu layer + main menu scene** — reusable Control stack; new `run/main_scene`; session autoload.
- `MG` (S) **Pause menu** — Resume / Save / Settings / Quit-to-menu via `SceneTree.Paused` + process-mode exemptions.
- `MG` `AV` (S) **Settings persistence + menu** — ConfigFile: sensitivity, FOV, invert-Y, volumes, view distance, gfx preset (keybinds arrive in M3).
- `AV` (S) **Audio buses** — Master/Music/SFX/Ambient; prerequisite for all volume + filtering work.
- `UX` `MP` (S) **Full InputMap migration** — mouse buttons, hotbar 1–9, fly toggle, Escape → actions with `physical_keycode`; gameplays reads keybinds only.
- `MG` `WG` (M) **Save v2 + world layout** — `user://worlds/<id>/manifest.json` (seed, spawn, worldgenVersion) + `save.json`; migrate existing save; refuse mismatched worldgenVersion.
- `WG` `PF` (S) **Seed plumbing + spawn safety** — one seed drives all five noises; own integer hash for campfires; clearance check before spawn teleport.
- `WG` (S) **Dev console** — `/seed /tp /time /spawn /give /vd`; multiplies speed of every later milestone.
- `MG` (S) **Autosave policy** — dirty on time/preset change, save on pause/quit/focus-loss, HUD save indicator.

*Exit criteria: create a world from a menu with a chosen seed; pause, rebind, and
mix volumes; saves never lose data.*

### M1 — Content core (items make it a game)

- `CT` (S) **Item registry** — `ItemId`, block↔item mapping, MaxStack (64; tools 1).
- `CT` (S) **Block metadata** — Hardness, Drop, RequiredTier in `BlockDef`; break-time accumulation + crack overlay.
- `CT` `UX` (M) **Inventory** — 27 backpack + 9 hotbar stacks, E to open, mouse-mode toggle; optional save field (backward compatible).
- `CT` (M) **Drops + pickup** — direct-to-inventory first behind a `Drop` abstraction (entities in M2); MMB picks any block into an empty slot.
- `CT` (M) **Crafting** — recipe registry (C# data first), 2×2 inventory + 3×3 table block, **craftable-now filter + one-click craft** (differentiator).
- `CT` (S) **Block palette browser** — expose all 22 block ids (today only 9 reachable).
- `CT` (M) **Tools** — wood/stone/iron/diamond tiers, durability, speed multiplier, harvest gating.
- `WG` `CT` (M) **Ore veins + depth bands** — blob placement (2–8 blocks) so tiers have progression targets.
- `PF` (M) **Async edit remesh** — route `SetBlock` through worker queue with per-chunk epochs; kills the up-to-5-chunk synchronous rebuild spike while building.
- `PF` (S) **Profiler hooks** — queue depth, build ms, memory, frame timing; evidence for all later tuning.

*Exit criteria: punch tree → wood item → craft planks/table/tools → mine stone → iron progression.*

### M2 — Survival & life

- `SV` (S) **Player stats** — health / hunger / breath + signals.
- `SV` (S) **Damage pipeline** — `IDamageable` + `DamageInfo`; all damage routes through it.
- `SV` `AV` (S) **Survival HUD** — hearts/hunger/breath bars, damage vignette, hit/heal feedback.
- `SV` (S) **Fall + drowning damage; softcore death** — respawn at spawn/campfire, keep inventory (D5).
- `SV` (M) **Melee v1** — swing cooldown, per-hit damage, knockback, hit sparks; tools carry damage.
- `SV` (M) **Mob base class** — health, hit reaction, loot table; retrofit sheep (no more one-punch kills).
- `SV` (M) **Hostile AI + first hostile** — idle/chase/attack; night/light-gated spawning via `TimeOfDay`; difficulty setting (peaceful/normal/hard).
- `SV` (M) **Functional campfires** — respawn anchor, heal/rest, hostile-repel radius (differentiator).
- `SV` `UX` (M) **Companion sheep v1** — tamed follow/stay (the home-leash logic already exists to invert).
- `SV` (S) **Weather danger hook** — storms spike hostiles; lightning strikes (`Cloudiness`/`SkyFlash` exist).

*Exit criteria: survival loop closes — eat, fight at night, die, respawn, recover.*

### M3 — World richness & polish

- `WG` (M) **Cave polish** — guaranteed entrances, rare large caverns, lava at y<10 (new static Lava block), underground water pockets.
- `WG` (M) **Biome pass** — continent/relief noise (hills, not flat ±13), beach biome, ~16-block blended edges, biome-specific depth/decoration.
- `WG` (M) **Surface decorators** — tall grass, flowers, snow layers, boulders.
- `WG` (M) **Structure framework** — per-cell deterministic templates (generalize the campfire pattern) → watchtowers, ruins, small mines.
- `WG` (L) **Rivers + proper lakes.**
- `AV` (M) **Fx system** — break/place debris from atlas tiles, impact dust, shoreline splash.
- `AV` (S) **Fog/atmosphere modes** — cave darkness, biome tint, storm mood.
- `AV` (M) **Water upgrade** — waves/foam after shader audit; rain splashes, visible lightning bolt.
- `AV` (M) **Adaptive soundscape** — material sound families, underwater lowpass, biome/time/weather ambience scheduler (differentiator).
- `AV` (S) **Camera feel** — bob/landing kick; swim feel. (S) **Photo mode** — time-scrub + free camera + HUD hide (differentiator).
- `UX` (M) **Onboarding** — first-run contextual tutorial (move→mine→place→survive night), rebind-aware help, adaptive contextual hints (differentiator).
- `UX` (M) **Gamepad support** — joypad events, right-stick look, deadzone/turn-rate options.
- `UX` (M) **Minimap/compass + waypoints**; light session objectives ("shelter before night").
- `PF` `WG` (M) **Edit-aware far LOD** — overlay edited chunks on the noise LOD + chunk fade-in (differentiator).
- `AV` (L) **Music** — adaptive generative bed or licensed tracks on the existing Music bus (D4).

### M4 — Scale, dimensions, optional multiplayer

- `PF` (M) **Payload pooling + persistent task groups + velocity prefetch.**
- `PF` (L) **Greedy mesher** behind a flag (only when the profiler says).
- `PF` (M) **Chunk store** — palette/compressed + LRU budget → view distance 8–12 with a memory budget.
- `PF` (M) **Mid-LOD from coarse chunk data** (edits visible mid-ring); section/occlusion culling.
- `PF` `WG` (L) **World height 128/256** — de-const ChunkY; mesher/FarLod/memory updates (D3).
- `WG` (L) **Dimension framework** — Nether-like parameterized worlds + portals.
- `WG` (S) **World presets** — flat/amplified/sky islands + seed sharing UI.
- `MP` (L) **LAN co-op** (only if D2 = yes) — `ENetMultiplayerPeer`, host-authoritative; terrain never transmitted (seed + edit log), clients predict own movement.
- `MP` (L) **Dedicated server** — headless export, reconnect, interest management.

## 4. Differentiators — "better than Minecraft" menu

Ranked by value/effort; pick 3–5 as identity features:

1. **Adaptive soundscape** (biome/time/weather procedural mixer) — `AV` M
2. **Functional campfires** (respawn/heal/repel) — `SV` M
3. **Companion mobs with commands** — `SV`/`UX` M–L
4. **Adaptive hint onboarding** (teaches through play, no wiki) — `UX` M
5. **Craftable-now recipe filter + one-click craft** — `CT` S–M
6. **Edit-aware far LOD** (your terraforming visible on the horizon) — `PF` M
7. **Ghost placement build guides** (line/plane/3×3 preview) — `UX` M
8. **Tiny shareable worlds** (seed + edit-diff saves, diffable text) — `MG` S
9. **Dynamic quality governor** (auto-steps gfx preset by frame time) — `PF`/`AV` M
10. **Photo mode** + auto-framed screenshot moment — `AV` S
11. **Wet-world response** (gloss + drips while raining, not just darkening) — `AV` M
12. **Cozy/survival mood toggle at world creation** — `MG` M

## 5. Risks & explicit unknowns

- **Y=64 ceiling**: no real mountains until the M4 height refactor; M3 relief
  noise must stay within budget.
- **JSON save grows with edits**: fine at current scale; per-world files in M0;
  binary/region format only if profiling demands (tiny worlds are a feature).
- **Non-cube blocks (slabs/stairs)** need mesher work — biggest mesher lift,
  deliberately deferred to post-M4 backlog.
- **Water shader capabilities unverified** — audit source before promising waves.
- **Procedural audio ceiling** — music likely needs real assets + licensing (D4).
- **Sync remesh spikes** at large builds until M1 async remesh lands.
- **Multiplayer determinism** (FastNoiseLite/Jolt across platforms) unverified —
  only matters if D2 = yes; `worldgenVersion` guard covers noise changes.

## 6. Verification anchors

- Chunk `(7,7)` SHA-256 `c7fe5ea262d362606196e2fffcdc65dd130eb2df1fc58ab051fb54d3623a352a`
  is the byte-parity anchor from the C# port; use it to prove M0/M1 refactors
  don't disturb worldgen.
- Existing save (`version/edits/player/time`) must keep loading through every
  save-format step (backward-compatible migrations only).

## 7. Progress log

- **2026-10-13 — M0 complete.** Main menu (Play/world select/create with seed,
  Settings, Quit), pause menu (Resume/Save/Settings/Quit-to-menu, tree pause),
  settings persistence `user://settings.cfg` (sensitivity, invert, FOV, four
  volumes, view distance, gfx preset), audio buses (Master/Music/Sfx/Ambient),
  full InputMap migration (20 new actions: ESC pause, `/` console, F3/F6–F9,
  hotbar 1–9, mouse buttons; gameplay reads actions only), world folders
  `user://worlds/<id>/` (manifest + world.json + player.json, temp-rename
  atomic writes, two-backup rotation, corrupt-file recovery), seeded worldgen
  plumbing (manifest carries master seed + resolved noise seeds; gen v1 keeps
  legacy constants and GD.Hash campfires for byte-parity), automatic legacy
  save migration, dev console (`/seed /tp /time /vd /spawn /save /help`).
  **Verified:** anchor chunk hash unchanged after migration; 20 edit chunks +
  player position preserved; menu→world flow; pause/resume; settings live-apply
  and persist; console commands; hotbar keys via actions; save backups rotate.
  *Working note for future sessions:* `game_eval` code must be PascalCase for
  C# members and must not mix indentation (use single-line lambdas); a runtime
  error in eval parks the game in a debugger break (stop + relaunch to recover);
  custom C# classes (e.g. `Inventory`) are not GDScript-accessible — verify via
  saves/UI instead.

- **2026-10-13 — M1 complete (gameplay side; perf hardening deferred).**
  Item registry (block items, materials, 12 tools), `BlockDef` metadata
  (hardness / preferred tool / tier / drops), timed mining with procedural
  crack overlay and per-block tick sounds, drops → inventory (hotbar-first
  fill, backpack overflow, full-inventory warning), inventory screen on E
  (27+9 slots, left/right-click stack moves with cursor-held stack, 2×2 and
  3×3 crafting-table grids, recipe matcher, one-click craftable-now recipe
  book, block palette), tools (wood→diamond × pick/axe/shovel with speed
  multipliers, harvest gating, durability and breakage), Crafting Table block
  + procedural tiles, ore veins worldgen for **gen v3** (deterministic
  per-chunk blobs in depth bands; v1/v2 worlds keep byte-parity), player.json
  v2 (`selected` + `items` with durability, old saves get the starter kit),
  dev console `/give`.
  **Verified:** mining 0.3–2.6 s by hardness with correct drops and tool
  speed; durability 60→51 over 9 breaks; grid craft (log→4 planks) and
  one-click craft (4 planks→table); save→load→save round-trip stable; anchor
  hash `c7fe5ea2…` unchanged on the v1 world; new v3 world ores per 3 chunks:
  482 coal / 142 iron / 77 gold / 41 diamond.

- **2026-10-13 — M2 complete (Survival & Life).** The survival loop closes:
  eat, fight at night, die, respawn, recover.
  - **Stats + damage pipeline**: `PlayerStats` (health 20 / hunger 20 / breath
    10, hunger drain by activity, regen while well-fed, starvation, drowning)
    and `DamageInfo`/`IDamageable` — fall, drown, starve, melee, lightning all
    route through one seam. `GameMode` per player: survival or creative
    (flight, no damage, no hunger); new worlds default **survival**, pre-M2
    worlds stay creative.
  - **Survival HUD**: hearts + drumsticks + breath bubbles (procedural icons),
    damage vignette (pulses near death), death screen with Respawn button.
  - **Death (softcore, D5)**: respawn at world spawn or the last campfire
    rested at; inventory kept; 2.5 s spawn protection; hostiles within 10 m of
    the respawn point are cleared.
  - **Melee v1**: swing cadence 0.45 s, hand 1 dmg + tool tier (diamond pick
    = 5), knockback (works on mobs and the player), hit flash, hold-to-attack.
  - **Mobs**: `Mob` base (health, hit flash, knockback, loot table, C# events)
    → `Sheep` (10 hp; drops 1–2 Raw Mutton + 1 Wool; tame with right-click →
    follow → stay cycle, teleports if >48 m behind) and **`Shade`** — the
    first hostile (20 hp, slow chase, 2/3 dmg by difficulty, ember eyes;
    rises at night and in storms, burns away in daylight, never spawns on
    peaceful, repelled by campfires).
  - **Functional campfires** (differentiator #2): right-click the log to rest
    (heal 8 + hunger 3, sets the respawn anchor, 3 s cooldown), cook Raw
    Mutton → Cooked Mutton, and a 10 m hostile-repel ring (verified: shades
    stop dead at exactly 10.0 m).
  - **Food**: apples (6% from leaves), mutton raw/cooked; eat with RMB in hand.
  - **Difficulty setting** (peaceful/normal/hard, persisted): peaceful = no
    hostiles, no starvation; hard = 3-dmg shades, starvation can kill.
  - **Weather danger hook**: storms raise shades (spawn interval 5 s → 2.5 s
    and they spawn even by day) + real lightning bolts that hurt within 2.5 m.
  - Save v3: `player.json` gains `mode`/`health`/`hunger`/`respawn` (all
    optional — old saves load unchanged); manifests gain a default `mode`.
  **Verified live:** mode switch + flight gate; melee 5 dmg/hit (diamond
  pick), sheep 10→5→dead with RawMutton+Wool into the backpack; eat +3
  hunger (even aiming at the sky); fall 9 blocks → 5 hp; /kill → death screen
  → respawn exactly at the campfire anchor with full bars + items; cook
  (mutton 2→1, +1 cooked); shades chase (13.1→10.0 m, stopped by the fire
  ring), attack for 2 dmg/1.4 s (20→14), player knockback 0.83 m; peaceful
  despawns all shades; save round-trips mode/health/hunger/respawn; anchor
  hash `c7fe5ea2…` unchanged. *Not live-verified:* storm spawn boost and the
  lightning bolt visual (both need a natural storm; code-reviewed only).
  *Test artifacts left in "My World":* world now saved as **survival** mode,
  inventory has mutton/wool/cooked/apple test items, player at spawn with
  16 hp; `/gamemode creative` restores the old sandbox feel.

- **2026-10-13 — M3 complete (World richness & polish).** The world is a
  place now: hills and rivers, living surfaces, landmarks on the horizon.
  - **Worldgen v4** (new worlds only; v1/v2/v3 worlds regenerate byte-identical
    — anchor hash re-verified): relief/continent layer (rolling hills to ~45 m,
    ocean ≈ a third of the map), winding **rivers** carved below sea level,
    domain-warped biome edges (3 flips per 128 blocks — smooth, not gridlocked)
    with **snow caps** above y44, **cave entrances** (holes in the top two
    layers so caves are findable from above), rare large **caverns**,
    **lava** at the molten floor (y≤4) and pooled at deep cave cores, and
    underground **water pockets** (y9–22).
  - **Surface decorators**: tall grass (~1 in 8 columns), red/yellow flowers,
    ragged boulders — grass biome only, air-checked so trees and structures
    win.
  - **Structure framework** (differentiator #2 generalized): one deterministic
    template per 160×160 cell — **watchtower** (cobble base, log posts, plank
    walls with weathered gaps, roof), **ruin** (crumbled brick/cobble walls +
    pillar), **small mine** (timbered 3×3 shaft 14 deep into an ore-flecked
    chamber). `/struct <tower|ruin|mine>` teleports to the nearest one.
  - **Lava block** with a dedicated **emissive** mesh surface — it genuinely
    glows in dark caves (verified visually); standing in it burns (4 dmg/0.5 s).
  - **Fx**: rain ground-splash pops, landing dust scaled by fall impact,
    water-entry splash puff.
  - **Atmosphere**: cave darkness (fog closes in + ambient collapses to 20 %),
    biome fog tint (desert warm / snow cool / birch green), stronger storm fog.
  - **Camera feel + photo mode**: head bob while walking, landing dip; **P**
    enters a free-flying photo camera with the clock frozen, arrow keys scrub
    the time of day, HUD hidden — exit restores everything.
  - **Minimap + compass (M)**: cached surface-colour map (noise + biome +
    player edits), heading needle, campfire/respawn waypoint dots; dusk/dawn
    toasts ("Night falls — shades rise in the dark" on normal/hard).
  - **Adaptive soundscape**: material dig families (stone crunch / wood knock /
    sand hiss / dirt), underwater low-pass muffle on the Master bus, night
    cricket chorus (silenced by rain or water), wind that rises with the
    clouds.
  - **Streaming polish**: chunk **fade-in** (per-instance transparency tween)
    and **edit-aware far LOD** (edited columns overlay the noise LOD, plus
    beach colouring).
  **Verified live:** v4 world heights 13→45 over 480 m; 136 lava blocks per 9
  chunks (glowing, screenshot); 139 grass / 15 red / 19 yellow decorators per
  9 chunks; 3 biome flips per 128 blocks; watchtower/mine generated and
  walked into (collision held on the mine floor); photo mode toggle (HUD
  hidden, time scrub, clean exit); minimap renders (land/water/needle/N);
  cave darkness + glowing lava; rain state with ground splashes; v1 anchor
  hash `c7fe5ea2…` unchanged; v3 New World still gen v3; My World v1 loads
  survival at its saved spot; run logs clean. *Not live-verified:* crickets/
  underwater muffle audibility (no audio capture), chunk fade-in smoothness
  (subtle), structure interiors fully explored (tower + mine only).
  *Deferred from M3:* snow-layer partial-height blocks (needs a mesher
  partial-cube path), swim feel, onboarding tutorial, gamepad support, music,
  and the still-open M1 perf items (async edit remesh + profiler hooks).

- **2026-10-13 — M4a complete (scale foundation, world height, presets, shadow fix).**
  - **Shadow dance fix (user report: "shadows move and dance")**: measured in-game
    (frozen sun ⇒ frame stable ⇒ the dance is the rotating sun re-rasterising
    the shadow map in tiny quantised steps). Fix: **PSSM 4 splits** +
    `blend_splits` (near-cascade texels ≈ mm → per-frame motion is sub-pixel),
    sun disk at the physical 0.5°, blur 0.25, normal bias 1.0, **quantisation
    removed** (smooth per-frame rotation), **32-bit** shadow atlas (was 16).
    Temporal-instability metric dropped (whole view 6.0→4.0, foreground
    4.0→3.0) and near-field leaf/trunk shadows are crisp instead of smeared.
  - **Profiler hooks**: `Profiler.cs` (ring buffer of worker mesh-build ms +
    queue/worker/data counters) feeding the F3 panel
    (`Stream q f+r · workers · Mesh avg/max · Data chunks`, plus
    `preset · H128`). Baseline at MAX/VD12/v5: RAM ~215 MB, VRAM ~430 MB,
    data cache 729 chunks ≈ 23 MB → **palette/LRU chunk store is unnecessary
    at the target view distance** (deferred with evidence).
  - **Async edit remesh**: `SetBlock` no longer rebuilds on the main thread;
    edits enqueue the touched chunks (self + neighbours) and workers re-mesh
    them. The main thread applies a **budget of 4 payloads/frame**, rebuilds
    before fresh streaming. Placing a block verified (data + visual, no spike).
  - **Payload pooling + persistent task groups + velocity prefetch**: mesher
    buffers are pooled (`PayloadPool`, ConcurrentBag); streaming runs as
    **WorkerThreadPool group tasks** — each element is a worker loop pulling
    until the queues drain, with group completion waited at exit — plus a
    velocity **prefetch bias** (chunks ahead stream first) and a bounded
    apply backlog (`MaxInFlight*3` backpressure).
  - **World height 128 + gen v5**: `ChunkY` const → per-world `Height`
    (64 for gen ≤4 — **v1 anchor hash `c7fe5ea2…` re-verified byte-identical**;
    128 for v5). v5 worldgen: base 52 ± relief 30, clamp 3..118, sea 40,
    caverns to y50, water pockets to y44, ore bands scaled (diamond <26,
    gold <46, coal <66, iron <48), snow caps >80, clouds follow Height.
    Mesher, far LOD, minimap and spawning are all height-driven.
  - **World presets** (gen v5, manifest `preset`): **normal**, **flat**
    (superflat at sea+4, no caves), **amplified** (base 54 + relief 44, peaks
    ~100+), **sky** (floating islands: noise mask + 26-block slabs at
    y46..112, no bedrock/sea, void kills below y −8, minimap/LOD show open
    sky). UI: New World dialog has a **world-type dropdown**, the seed field
    accepts **share codes** (`1337·flat`, `777 amplified`, `sky`), and the
    world list shows the preset tag.
  - **Latent spawn bug found & fixed**: `FindSpawn` could choose the origin
    column, which always grows a tree (`Hash2(0,0) = 0`); spawning inside its
    trunk dropped the player through the concave mesh. Spawn now skips
    tree/cactus/boulder/campfire/structure columns.
  **Verified live:** v5 normal (H128, sea 40, heights 25–78, 27 % water);
  flat (all 44, clean spawn, off the origin tree); amplified (preset tag +
  heights 17–89); sky (spawn on an island, overhanging undersides seen from
  below, minimap gaps); 625 chunks streamed per world; placed block appears;
  profiler panel; menu dropdown, share-code placeholder and world list tags;
  v1 anchor hash unchanged and the v1 world renders through the new pipeline;
  logs clean. *Not live-verified:* shadow dance motion (stills + metric only),
  steady-state mesh ms, dropdown popup list (panel renders).
  *Deferred to M4b:* mid-LOD + section/occlusion culling, greedy mesher
  (only if profiling demands), dimension framework + portals, LAN co-op
  (D2 still "no").

- **2026-10-13 — M4b complete (performance hardening + dynamic quality).**
  The frame budget story on the real machine (NVIDIA MX230 laptop, MAX preset
  at the drifted view distance 24): 6 FPS, 166 ms main-thread frame, 2.4k
  chunks / 14k nodes. Work done, all profiler-driven:
  - **View-distance drift fixed**: `settings.cfg` had `view_distance=24`
    (a leftover `/vd` dev command) — 2401 chunks. Target per D3 is 8–12.
  - **Dynamic quality governor** (differentiator #9): samples FPS only while
    the window is focused and past a settle/cooldown window; if fps < 26 for
    4 s it steps the preset down (persisted, toast "quality auto-adjusted"),
    and after 45 s above 55 fps it steps back up toward the preset the player
    chose (never above it). Settings checkbox "Auto quality" + `/autoq on|off`;
    F3 shows `(auto)`. Verified live: MAX→ULTRA→HIGH on the MX230
    (17 → 36 FPS), and it also caught the deliberate error-spam regression
    during development by downgrading — working as intended.
  - **Greedy meshing behind a flag — now the DEFAULT** (`Settings.GreedyMeshing`,
    default true; `/greedy on|off` re-meshes in place): merged coplanar opaque
    quads with AO/block/tile in the merge key, texture-array shader (one array
    layer per atlas tile, `fract()` UV tiling — a plain atlas cannot tile a
    merged quad) with alpha scissor for cutout leaves. Water/glass/lava keep
    the per-face path. **Measured at VD8/HIGH: 822 k → 419 k triangles (−49 %),
    34 → 44 FPS (+29 %)**; per-face-direction surface area parity verified
    exactly (491/491/244/183/387/231 both modes) and the view is
    pixel-comparable to the per-face renderer.
  - **Minimap cost**: the 96×96 noise rebuild ran 2 Hz regardless of movement
    or visibility (25–50 ms hitches). Now: skipped while hidden, rebuilt only
    when the player moves > 2 m (else every 3 s), and a height-aware
    `BiomeAt(x, z, h)` overload (also used by worldgen callers that already
    know h) halves its noise cost.
  - **Debugging note (greedy slice off-by-one)**: the first greedy build
    emitted every quad one block low along its face normal (padded base is 1,
    so the slice must be `base + (sn-1)*step`), which silently dropped the
    world-bottom -Y faces and buried top faces → see-through holes. The
    numeric signature that found it: per-normal surface-area buckets of the
    mesh vs the per-face mesh (-Y lost 384 while +X/+Z gained); area parity
    per normal is now the regression check.
  **Verified live:** governor stepping + persistence; greedy on/off with
  remesh-in-place; visual parity screenshots (same camera, before/after);
  face-area parity per normal; prim/FPS deltas; minimap still renders.
  *Deferred (M4c):* mid-LOD from coarse chunk data + section/occlusion
  culling (profiler says the remaining cost is main-thread scene processing;
  mid-LOD is a visual-pop fix at VD12+, no measured win at the playable
  VD8 config), palette/LRU chunk store (data cache measured ≈ 23 MB at
  VD12 — unnecessary), dimension framework + portals.

## 8. Resume / handoff (state @ 2026-10-13, end of M4b)

**Status:** M0–M3 + M4a + **M4b** complete. Performance now adapts to the
machine: greedy meshing is the default (≈ half the triangles, no visual
change), a frame-rate governor picks the preset (and remembers it), the
minimap no longer hitches, and the MX230 laptop runs HIGH at ~44 FPS
(previously 6 FPS at the drifted MAX/VD24). Build clean (0 warnings), game
stopped, editor ready.

**Settings (current)**: `gfx_preset=1` (HIGH, VD8), `auto_quality=true`,
`greedy_meshing=true`, difficulty peaceful. The governor owns the preset when
auto quality is on; F6 still cycles presets manually (which resets the
governor ceiling to that choice).

**Worlds on disk** (`~/.local/share/godot/app_userdata/minecraft/`):
- `worlds/default/` — "My World", seed 1337, **gen v1** (anchor
  `c7fe5ea2…352a` re-verified); survival, M2 test items, standing at
  (60.5, 34, 121.6).
- `worlds/w801E78D6/` — "New World", gen v3, creative.
- `worlds/wv4test/` — gen v4 test world (tower + mine).
- `worlds/wv5test|wv5flat|wv5amp|wv5sky/` — **gen v5** preset test worlds
  (normal / flat / amplified / sky, all seed 777, creative). Keep for M4b.
- New worlds from the menu are **gen v5 + survival + normal**; difficulty
  stays peaceful by default.
- **Manifest `playedUtc` churn**: any session that saves rewrites `playedUtc`
  to real "now", clobbering bash-side ordering hacks — re-hack after each run
  when forcing `project_run(mode="current")`.

**Next steps — M4c:** mid-LOD from coarse chunk data + section/occlusion
culling (visual-pop fix at VD12+; the profiler says the remaining cost is
main-thread scene processing, not vertices — hence deferred with evidence),
dimension framework (Nether-like parameterised worlds + portals) — new
`DimensionInfo` (noise seeds, sea level, height, presets) + portal blocks +
per-dimension save namespaces. Then M5+ (see §3). Open smaller items: snow
layers, swim feel, onboarding, gamepad, music.

**Tooling notes (learned the hard way):**
- Build: `dotnet build` in `/home/firas/minecraft` (0-warnings policy).
- Run via MCP: `project_run(mode="current")` boots straight into the most
  recent world — **skips the menu-click dance entirely** (menu clicking was
  unreliable: clicks silently missed while the menu was still booting).
  Which world is "most recent" = highest `playedUtc` in its manifest; to force
  a world, edit that field (bash) before launching.
- `game_eval` rules (each violation parks the game in a debugger break →
  stop + relaunch):
  - Engine properties are **snake_case** (`global_position`, `velocity`);
    C# script members are **PascalCase** (`ChunkData`, `Run`, `CampfireInReach`).
  - C# public fields/properties with Variant-compatible types are readable via
    `get("Name")` and writable via `set("Name", value)` (enum-backed fields
    accept ints, e.g. `weather.set("Current", 2)`).
  - **Not callable from GDScript**: methods with nullable/custom-type
    signatures (e.g. `NearestCampfire` returns `Vector3?`) — the source
	generator doesn't expose them. Work around via engine APIs (e.g. campfire
	positions = children of `/root/Main/Campfires`).
  - **Not accessible at all**: custom classes (`Stats`, `Inventory`). Verify
	via saves (`player.json`), HUD screenshots, or public wrappers.
  - Multi-line code with **consistent tabs** works; mixing tabs+spaces breaks.
- Input simulation: `input_action` only sets the action *state* — handlers
  that listen for InputEvents (photo mode, toggles) need `input_key`
  (e.g. `{"key": "P"}`) which sends a real event.
- UI anchoring gotcha: a top-right/bottom-right `SetAnchorsAndOffsetsPreset`
  on a PanelContainer **before** its content exists locks the width at zero
  and grows the panel off-screen — always add children first, anchor after.
- Testing a hit: teleport the mob to `cam.global_position + fwd*2 + (0,-1,0)`
  (its body sits above its origin), `await` ~0.1 s, `ray.force_raycast_update()`,
  then call `p.AttackOrStartMining()` / `p.PlaceBlock()` directly.
- Console commands (all callable via `DevConsole.Run`): `/give`, `/hold <item>`
  (into the selected slot), `/gamemode`, `/difficulty`, `/heal`, `/hunger n`,
  `/kill`, `/mob <shade|sheep> [n]`, `/struct <tower|ruin|mine>`, `/time`,
  `/tp`, `/vd`, `/spawn`, `/save`.
- Regression anchors: v1 chunk (7,7) SHA-256
  `c7fe5ea262d362606196e2fffcdc65dd130eb2df1fc58ab051fb54d3623a352a`;
  `player.json` must round-trip save→load→save unchanged.
- Gen-gating rule: all worldgen changes are gated on `_genVersion >= N` and
  the seed-derivation / xorshift helpers are **frozen** — existing worlds must
  regenerate byte-identical forever.
- **Shadow debugging** (M4a): you cannot judge temporal jitter from stills.
  Method that worked: freeze the sun (`DayNight.Paused = true`) and take two
  viewport images ~0.25 s apart in `game_eval`, computing a per-pixel diff
  (`get_viewport().get_texture().get_image()`) — a stable frame proves the
  remaining motion comes from the sun. Then drive the light manually at
  different step sizes and compare diffs. Current setup: PSSM 4 splits,
  `blend_splits`, angular distance 0.5, blur 0.25, normal bias 1.0, **no
  quantisation** — do not reintroduce `Snapped()` on the sun rotation.
- `game_eval` callability: **nullable struct returns are not exposed**
  (`GetTreeAt`, `NearestCampfire`, `GetStructureSite` → call the same logic
  around them or use `GetBlock`/`SurfaceHeight`). Public get-only properties
  (`PresetName`, `IsSky`, `Height`) read fine via `get()`.
- Streaming architecture (M4a): worker loops run inside WorkerThreadPool
  group tasks and enqueue payloads into `_applyFresh`/`_applyRebuild`; the
  main thread applies ≤4/frame (rebuilds first). If you add a new path that
  creates payloads, it MUST end in `PayloadPool.Release(...)` (guard is the
  `Released` flag) or pooled buffers leak logically.
- Spawn safety: `FindSpawn` skips tree/cactus/boulder/campfire/structure
  columns (`SpawnBlocked`) — spawning inside solid concave geometry drops the
  player through the world (`Hash2(0,0)==0` guarantees an origin tree).
- Perf workflow (M4b): the game window must be **focused** for real FPS
  (`xdotool search --name DEBUG windowactivate`); unfocused readings are
  throttled. The governor also refuses to sample while unfocused (by design).
  Long `game_eval` awaits (>8 s total) abort mid-coroutine and can park the
  game — use short evals plus bash `sleep` between them. `dotnet build` must
  run inside `~/minecraft`.
- Greedy meshing invariants: merge key = id | tile<<8 | 4×AO(2 bits) at 16;
  slice base index is padded (1,1,1) and advances `(sn-1)` steps; quads emit
  via `TriOrder` (6 verts) with UV = per-block repeats (shader `fract()`s)
  and UV2.x = array layer. Regression check: per-face-direction surface-area
  buckets must match the per-face mesher exactly; `/greedy on|off` re-meshes
  in place, and Settings.GreedyMeshing persists.

**Export (2026-10-14):** release builds shipped —
- **Linux**: `~/minecraft-build/minecraft.x86_64` (+ `.pck` + `data_minecraft_linuxbsd_x86_64/`),
  packaged as `minecraft-linux-x86_64.tar.gz` (66 MB)
- **Windows**: `~/minecraft-build/windows/minecraft.exe` (109 MB) + `.pck` +
  `data_minecraft_windows_x86_64/`, packaged as `minecraft-windows-x86_64.zip`
  (78 MB); smoke-tested under wine (boots, runs, exit 0 — unsigned, so
  SmartScreen may warn on first run)
Recipe for re-exporting:
- Editor binary: `~/Desktop/anki-linux/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64`
- **.NET templates live in `~/.local/share/godot/export_templates/4.7.2.stable.mono/`**
  — the `.mono` suffix matters for the C# editor; the pack is
  `Godot_v4.7.2-stable_mono_export_templates.tpz` (1.2 GB) from the official
  GitHub release, unzip `templates/*` into that folder.
- Presets in `export_presets.cfg` ("Linux/X11" + "Windows Desktop", x86_64).
  Export command:
  `<godot> --headless --path ~/minecraft --export-release "<preset>" <out>`.
  Windows cross-export from Linux works (dotnet fetches the win-x64 runtime
  pack); the godot_ai addon strips its `_mcp_game_helper` autoload from
  exported packs automatically.
- Exports are **self-contained** (the .NET runtime natives ship in `data_*`)
  — target machines need no runtime install, just a Vulkan GPU. The exported
  game shares `user://` with editor runs (same `minecraft` userdata dir), so
  existing worlds carry over.
- Release builds use **Release** C# assemblies (`.godot/mono/temp/bin/ExportRelease/`)
  and release engine templates → faster than editor play runs (which use
  Debug assemblies); the measured FPS numbers in this log are debug numbers,
  i.e. a floor.

**Backups:** `~/minecraft-src-backup-2026-10-13-m4b-export.tar.gz` (source
including `export_presets.cfg`),
`~/minecraft-userdata-backup-2026-10-13-m4b.tar.gz` (worlds + settings);
M4a/M3/M2/M1 tarballs kept alongside.

## 9. Integration checkpoint — 2026-11-22 (all outstanding PRs)

The ten open PRs were resolved against `main` in dependency order. The
performance chain merged first (#37 benchmark runner, then #38 MX230 work,
then #46 streaming payload accounting). #45 unified voxel material art and
surface responses; #40, #43 and #44 were closed as superseded after their
remaining unique deltas were ported (water/organic material classification and
the texture-array mipmap/detail regression test). #42 added deterministic V9
structure weathering with atlas tiles renumbered to 52–55 so they no longer
collide with the wood-grain tiles 49–51. The draft #41 branch was reconstructed
as generator V10 biome foliage (BlockId 56–58 / ItemId 105–107) rather than
merged, because its branch predated the village/quest save format, the
hardened world-delete guard and the slab/stair/boat/bucket integrations. #39
kept its player-journey audit as roadmap section 23, and #47 added the
environment/weather/water visual pass on top.

**Compatibility:** BlockId 0–58 and ItemId 0–107 are append-only; no released
value moved. Save format stays v4. Generator versions 1–9 are byte-identical;
new worlds use V10, whose only difference from V9 is the leaf material of tree
species V8 already selected. All voxel writes still flow through
`VoxelWorld.SetBlock`/`ApplyEdit`; no meshing, lighting or streaming seam was
replaced.

**Validation:** Debug, Release and ExportRelease builds have 0 warnings and 0
errors. `scripts/verify.sh` reports 186,327 checks and 0 failures across 38
suites, including V1–V8 golden hashes, V8-vs-V9 weathering deltas and the V10
foliage-only delta. No new GPU capture, benchmark run or human playthrough was
performed during this integration; the committed captures and benchmark
records come from the individual PRs.


## 10. Consistent block-edge shading — 2026-11-22

- The existing mesher already baked packed sky/block light and four-corner AO;
  the gap was that the AO stencil sampled whole neighbouring block IDs. A slab
  or stair therefore occluded all of its neighbouring corner samples even when
  the relevant half-cell was empty. Partial faces also reused the full-cube
  stencil despite sitting on half-voxel planes.
- Replaced the duplicated face and greedy-key stencil code with one half-voxel
  corner sampler. It maps each sample into the existing 2×2×2 occupancy mask,
  checks the neighbour's shape state, averages the four matching packed-light
  samples, and produces the AO value carried by the existing vertex colour.
- The per-face cube path, cube faces split around partial neighbours, slab/stair
  micro-faces and greedy face merge keys share the same corner convention.
  Diagonal block and sparse state borders now use the actual four diagonal
  chunks, removing a cardinal-edge alias at chunk intersections.
- Design boundary: retain the classic four-sample AO response and existing
  face-direction tint; this is not a new shadow system or screen-space AO pass.
  No extra mesh vertices, material attributes, scene nodes or per-frame work
  are introduced. The occupancy checks and neighbour lookups run only while
  chunk meshes are built. The atlas and first-person item response are unchanged.
- Compatibility: no BlockId/ItemId values, saves, world-generation code or
  generator versions changed. V1–V8 stay frozen; voxel edits still use the
  existing SetBlock/ApplyEdit ownership path. Greedy merging and chunk streaming
  remain enabled and structurally unchanged.
- Added corner regressions for lower-vs-upper slab occupancy, upright and
  inverted stair risers, diagonal chunk state sampling, per-face/greedy
  shading parity, and independent sky/block corner-light averaging.
