# Survival progression and combat balance audit — 21 November 2026

## Scope and method

Audited the existing systems in the `main` snapshot used to create this branch:
resource harvest tiers, recipes, smelting/fuel, upgrade costs, XP/levels,
lapis/enchantment costs, melee/ranged damage, armor/protection, equipment wear,
arrows, shields, hostile populations, difficulty modes, the Ember Deep and
Ember Warden rewards. The implementation extends the existing `Player`,
`Inventory`, `EquipmentProgression`, `Mobs`, `DungeonSystem`, `MobProjectile`,
`FurnaceData` and trader UI paths. It does not introduce a second combat,
crafting, inventory or mob system.

Source-of-truth tuning values are recorded below. These are audited values,
not claims that every possible procedural seed or player playstyle has been
statistically simulated. The deterministic suite exercises cross-system
resource/crafting progression, damage rules, equipment, Warden phase thresholds,
save/load and a complete Warden health/death/reward contract. The real-scene soak
exercises the live world, population manager,
weather, surface/cave transitions, herd feeding/breeding and saves.

## Balance snapshot

### Resource and crafting progression

| Stage | Current rule | Balance observation |
|---|---|---|
| Wood | 1 log → 4 planks; 2 planks → 4 sticks | Enough for starter pick/sword and workstations; recipe yields remain unchanged. |
| Stone | Stone pick is tier 2; it can harvest iron ore | Clear first mining gate; wood cannot harvest iron ore. |
| Iron | 1 raw iron → 1 ingot; iron ore requires stone pick | Iron unlocks gold/diamond harvest tier, full iron armor and weapons. |
| Gold/diamond | Iron pick required; gold and diamond ore retain tier-3 harvest | Avoids skipping iron equipment. |
| Coal | 80 seconds of burn; 10 seconds per smelt | Eight items per coal; a large Tick now carries remainder across fuel boundaries. |
| Other fuels | Log 60s; planks 15s; stick 5s | Lower output than coal; keep early emergency fuels useful but inefficient. |
| Arrows | 1 gravel + 1 stick + 1 feather → 4 arrows | Four shots per recipe batch; one arrow is consumed per bow shot. |
| Enchanting table | 2 diamonds + 2 iron ingots + 1 lapis | A meaningful mid-game investment; one deterministic lapis fallback now exists. |
| Upgrade materials | Wood→stone: 2 cobblestone for sword/tool; stone→iron: 2 iron ingots for sword/tool; iron→diamond: 2 diamonds for sword/pick/axe, 1 diamond per armor piece | Existing ratios and ratio-preserving durability transfer are retained. |

World-generation resource density is depth-banded. `OreAt` uses a deterministic
coordinate hash before vein stamping: coal is 2/84 eligible samples at y≤112;
iron is 1/145 outside mountains or 1/105 in mountains at y≤82; gold is 1/390
or 1/245 at y≤44; diamond is 1/760 or 1/460 at y≤20. These are hash-sample
rates, not final visible block percentages. The additional V7 16×16 seed-cell
vein pass attempts 18–20 coal veins (4–11 blocks), 6–8 iron veins (3–8), 2–4
gold veins outside mountains or 4–6 in mountains (2–6), and 4–6 diamond veins
(2–4); only eligible stone is replaced, and stamps crossing cell boundaries
are clipped to the generated chunk. Ruined chambers add a few fixed/random ore
blocks. There is no lapis ore/vein or furnace recipe: lapis remains a cave reward.

Crystal Wisps remain a renewable lapis source: their loot includes 1–3 lapis
at 55% chance (expected value 1.1 lapis per defeated Wisp). Previously, a poor
Wisp drop sequence could block enchanting-table construction and future
improvements with no reliable alternate source. The trader now offers 1 lapis
for 2 gold ingots. Trade output capacity is checked before currency consumption,
so a full inventory cannot lose payment.

Ore blocks all have hardness 3.0. A matching tool mines at `hardness × 1.5 /
speed`; a hand/wrong tool takes `hardness × 5` seconds (15s for ore). Coal is
tier 1; iron tier 2; gold/diamond tier 3. Pick speeds are 2.5/4/6/8 for
wood/stone/iron/diamond, so the minimum tier mining durations are 1.8s for coal
with wood, 1.125s for iron with stone, and 0.75s for gold/diamond with iron.
Diamond picks cut those tier-3 ores to 0.5625s. Lower-tier picks can still
break an ore block slowly but do not satisfy its harvest/drop tier.

### Equipment and defense

| Equipment | Damage / protection | Durability |
|---|---:|---:|
| Wooden sword | 4 damage, 0.85s cadence | 70 |
| Stone sword | 5 damage, 0.72s cadence | 150 |
| Iron sword | 6 damage, 0.62s cadence | 320 |
| Diamond sword | 7 damage, 0.55s cadence | 900 |
| Ember Blade | 8 damage, 0.52s cadence | 1,200 |
| Bow | 5 arrow damage, 0.80s cadence | 384 |
| Shield | Front-facing melee/projectile block | 420 |
| Wood / stone / iron / diamond pick | Tier 1/2/3/4; speed 2.5/4/6/8 | 60/130/250/450 |

The shield's forward check accepts attacks within about 69.5° of the facing
vector (`dot >= 0.35`); it blocks melee and physical projectiles, not direct
`DamageKind.Fire` pulses such as the Warden volley. That attack is answered by
positioning and armor rather than holding a shield indefinitely.

Base armor reduction is four percentage points per armor point, capped at 80%.
Full iron armor has 15 points (60% reduction); full diamond has 20 (80%). Each
Protection level reduces the remaining damage by 4% per equipped piece; four
Protection III pieces therefore move a full diamond set to about 88% total
mitigation, not 92% additive reduction. Iron armor durability is 200/300/250/180
(helmet/chest/legs/boots), while diamond is 400/600/500/350. Each landed damage
hit that armor mitigates wears every equipped piece by one point; Drown/Starve
are excluded. A blocked shield hit wears the shield by one point, and a landed
melee hit wears the held sword/tool by one point. Bow shots consume one arrow
and one bow durability point.

Unbreaking uses the existing per-use chance `level / (level + 1)` to negate a
wear point: level I prevents 50% of wear, level II prevents 66.7%, and level III
prevents 75%. These probabilities apply to melee weapons, tools, bow, shield and
armor, without changing stack serialization.

Found and fixed during audit:

- The population manager queued dawn-retreating mobs for deletion but kept
  their wrappers in its list until a validity check that ran before deferred
  deletion. A later tick could read `GlobalPosition` from a disposed wrapper.
  Mob death, dawn retreat and peaceful cleanup now remove entries synchronously
  before `QueueFree`.
- A pick/axe/shovel/hoe could be used as an emergency melee weapon (2–5 base
  damage from wood through diamond) but landed hits did not consume its
  durability. It now shares the existing weapon durability path and Unbreaking
  calculation.
- A shield could lose durability when a hit was rejected by the player's
  respawn invulnerability window. Invalid, creative, dead and invulnerable hits
  now return before shield wear.
- Armor was worn by starvation/drowning even though those damage types bypass
  armor mitigation. They still reduce health but no longer wear armor.
- Full furnace updates that crossed a fuel boundary discarded leftover elapsed
  time. `FurnaceData.Tick` now carries that time forward across fuel items and
  can complete all smelts covered by a large update.

### Hostiles, XP and difficulty

| Actor | Health / XP | Normal → Hard attack damage |
|---|---:|---:|
| Shade | 20 HP / 5 XP | 2 → 3 melee |
| Rot | 14 HP / 5 XP | 1.9 → 2.7 melee, plus 0.35 per nearby pack member (max 3 bonuses) |
| Hollow | 12 HP / 5 XP | 2 → 3 ranged |
| Briarback | 20 HP / 1 XP (passive; defensive when provoked) | 2.5 → 3.25 melee |
| Cave Stalker | 11 HP / 5 XP | 3 → 3.6 melee |
| Crystal Wisp | 9 HP / 5 XP | 2.5 → 3 ranged |
| Dreadwing | 16 HP / 5 XP | 3 → 3.6 melee |
| Ember Warden | 120 HP / 25 XP | 4 → 5 slam; 2 → 3 fire volley |
| Passive animal | Species health / 1 XP | No hostile attack unless provoked/defensive |

Normal and hard keep mob health, telegraph timings, attack cooldowns, and cave
composition unchanged. Hard increases damage and raises the active hostile cap
from 7 to 10. Existing hostile spawn intervals are 5.5 seconds in clear weather
and 2.8 seconds during storms; spawn attempts still require valid spawn points
and their existing biome/darkness conditions. Peaceful already suppressed
automatic hostiles; this audit closes the remaining paths by rejecting manual
hostile/Warden spawns, suppressing dungeon boss UI/wakeup, cancelling already-
winding melee attacks to zero damage and clearing hostile projectiles.

The Warden's 120 health and two add-summon thresholds remain intact: it calls
for two adds below 66% health and three below 33%. It awards one Ember Core at
100% drop chance plus 25 XP through the existing mob-death
reward path. Its 4/5 damage slam cadence remains 2.4 seconds and its 2/3 damage
volley cadence 4.2 seconds. No health inflation or damage inflation was used to
make the fight harder.

### Food and experience pacing

Hunger starts at 20 points. Outside peaceful mode, idle/moving/sprinting drain
rates are 0.35/1.0/1.8 hunger per 90 seconds of accumulated activity (roughly
one point per 257/90/50 seconds respectively). At 18+ hunger, a damaged player
regenerates one health point every 3.5 seconds, spending one hunger point per
regeneration tick. Starvation ticks once every four seconds: normal mode stops
at one health, hard mode can kill, and peaceful disables drain/starvation.

| Food | Hunger restored | Healing |
|---|---:|---:|
| Apple | 3 | — |
| Raw chicken | 1 | — |
| Raw beef / pork / mutton / fish | 2 | — |
| Bread | 5 | 1 health |
| Cooked chicken / fish | 5 | 1 health |
| Cooked mutton | 6 | 1 health |
| Cooked beef / pork | 7 | 1 health |

Campfires cook raw meat/fish and can be used every three seconds to set the
respawn point, heal eight health points and restore three hunger. Furnace
cooking uses the same meat conversion recipes. Cooking therefore gives a clear
food progression jump over raw drops, while bread offers a farmable alternative.

Experience is currently combat sourced: ordinary hostile defeats give 5 XP,
passive animals (including defensive Briarbacks) give 1 XP, and the Ember Warden
gives 25 XP. Mining, smelting, farming and fishing do not currently award XP.
For levels up to 16, the cumulative threshold is `level² + 6×level`: level 6
requires 72 XP and level 8 requires 112 XP. Sharpness and Protection require
1/2/3 levels and 1/2/3 lapis for each sequential I/II/III step; the full path to
III spends six levels and six lapis. Unbreaking requires 2/3/4 levels and
1/2/3 lapis per sequential step (nine levels and six lapis to III). This creates
a deliberately combat-led late-game enchantment grind; it is the main balance
risk to validate with human playtime data, rather than something that should be
changed by raising enemy health or damage. The trader fallback only addresses
lapis scarcity, not XP acquisition.

## Reproducible gameplay scenarios

`tests/SurvivalProgressionTests.cs` is the deterministic end-to-end progression
scenario. It is run as the `survival-progression` suite by `scripts/verify.sh`.
The individual stages are:

1. **No equipment:** assert a new inventory is empty and base incoming damage
   multiplier is 1.0.
2. **Wood and stone:** turn logs into planks/sticks, craft a wood pick/sword,
   upgrade the pick using cobblestone, and validate iron ore harvest gates.
3. **Iron:** smelt 32 raw iron using four coal, craft an iron tool, sword and
   four armor pieces, and equip the armor.
4. **Caves and ammunition:** check cave-wisp lapis loot; craft four arrows from
   gravel/stick/feather; retain projectile damage through the common IDamageable
   path. The real-scene soak spawns live cave fauna in generated caves.
5. **Combat:** exercise melee and projectile damage; test shields from the front
   and rear, successful-block durability, respawn/creative/zero-damage rejection,
   and armor wear rules for physical versus starvation/drowning damage.
6. **Experience and lapis:** assert hostile/passive/Warden XP rewards and barter
   lapis at one per two gold; craft the Enchanting Table from its diamond/iron/
   lapis recipe while reserving six additional lapis for Sharpness progression.
7. **Enchanting:** apply Sharpness I–III, Unbreaking I and Protection I using
   increasing level/lapis costs; verify armor and weapon enchantment fields.
8. **Equipment upgrades:** turn the iron sword into a diamond sword and assert
   Sharpness/Unbreaking are preserved; assert full iron/diamond mitigation.
9. **Ember Deep / Warden:** verify Warden stats and guaranteed reward, apply
   scripted projectile-kind hits through the shared damage interface across all
   120 health, and assert one death event plus the 25 XP
   and Ember Core reward contract. `DungeonTests` separately verifies the
   deterministic arena placement for legacy V6 and current V7 seeds, including
   the V7 arena's vertical bounds.
10. **Death, overflow, save/reload:** verify softcore death/respawn restores the
    player and preserves XP, full inventory barter refuses without consuming
    gold, overflow reports leftovers, equipment/enchantment/armor and XP fields
    survive serialization and reload.

The deterministic suite focuses on systems rather than emulating player input
or a human-timed manual boss fight. Warden health, accepted projectile-kind
damage, death-event uniqueness, XP and guaranteed loot are asserted, but the
live 66%/33% boss phase transitions, summoned adds, player dodging, shield timing
and arena navigation are not driven by a human or an automated avatar. Mining
and resource acquisition are tested at the harvest/recipe boundaries, not by
walking a player through a full seeded world. The 60-minute real-scene soak is a
separate runtime stability/AI/population/save scenario; it does not claim a human
combat playtest or visual gameplay sign-off. The soak starts the real main scene
in Normal Survival, detects a generated cave, alternates surface/cave and
storm/daylight cycles, feeds passive herds, observes breeding, records per-minute
population counts, and writes the world before exit.

## Verification record

| Check | Result |
|---|---|
| Baseline build on Godot 4.7.2 Mono / .NET 9 | 0 warnings, 0 errors |
| Audit Debug build | 0 warnings, 0 errors |
| Audit Release build (`minecraft.csproj`) | 0 warnings, 0 errors |
| Baseline full headless suite | 1,236 checks, 0 failures |
| Audit full headless suite | 1,331 checks, 0 failures |
| Audit test shutdown allocations | 18 ObjectDB / 17 Jolt (same as baseline test harness) |
| Soak | **Running after population cleanup fix; final duration/result updated after completion.** |

Commands:

```bash
dotnet build -c Debug --nologo
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 ./scripts/verify.sh
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 bash scripts/soak-survival.sh 3600
```

The headless save suite deliberately emits warnings while forcing an atomic
write failure and a backup-recovery path. It passes. The test process exits with
18 ObjectDB and 17 Jolt allocations, matching the baseline runner's known
shutdown allocation count. The main scene emits
invalid cached UID warnings for several script references in this worktree but
falls back to their valid `res://scripts/*.cs` paths and continues loading; this
was not a gameplay script error. See the soak outcome below after it completes.

## Compatibility and scope boundaries

- No `BlockId` or `ItemId` file was modified. Persisted IDs remain untouched.
- No new save fields, save version, world-generation change or generator version
  were introduced. Furnace continues using its existing `BurnTime`, `BurnMax`
  and `SmeltTime` fields. Legacy player, inventory, world and manifest formats
  remain unchanged.
- No new loot IDs or item IDs were added; the lapis fallback uses existing
  `GoldIngot` and `LapisLazuli` IDs.
- V7 terrain generation, greedy meshing, chunk streaming, voxel mutation,
  world persistence and existing mob architectures were left intact.
