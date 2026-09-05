# The 27 new spells — visual audit, scored

**Audited:** 2026-09-05 · **Subject:** the 27 spells shipped 2026-09-04 by
`.github/SPELL_EXPANSION_27_ROADMAP.md` · **Verdict: mean 3.04 / 10, median 2.8.**

**REMEDIATION PASS RAN 2026-09-05, same day.** Stages 0-3 of Part 5 are implemented and the
EditMode suite is green (**7321 / 7321**, console clean). What changed, and what is still open,
is recorded in **Part 6** at the end of this document. The scores in Part 1 are the BEFORE
state and are deliberately left untouched — they are the measurement the work was aimed at,
and rewriting them in place would destroy the only record of what was wrong.

> Three of the 27 are defensible on screen today (`ice_lance` 8.5, `arcane_barrier` 8.0,
> `guardian_light` 6.5). **Nine score at or below 2.0, meaning the spell has no visual
> identity of its own at all**, and six of those nine draw literally nothing between the
> muzzle and the impact. The expansion shipped its MECHANICS and its DATA; it did not ship
> its LOOK, and the roadmap's own Part 3 "**Look.**" paragraphs — which are binding
> specification, not description — went almost entirely unbuilt.

This document is the measurement. The roadmap remains the specification; it is not edited
except for a pointer, because the value of both depends on being able to tell "what we said"
from "what we shipped".

## What was measured, and what was not

Every spell was traced from `SpellCaster` to the pixel: the authored `.asset`, the executor,
the controller, the visual rig, the materials, the sorting, the light. Each was then scored
against the roadmap's own fifteen laws.

**Nothing here was observed running.** No frame of the game was rendered for this audit; the
scores are read off code and shipped data. That matters because this project's own CLAUDE.md
records the failure mode at length — *a number can be real, internally consistent, and about
something other than what you asked*. Two classes of claim below are therefore firmer than the
rest, and they are the ones the plan is built on: **a rig that does not exist cannot be seen**
(a grep result), and **a field no code reads cannot affect anything** (also a grep result).
The aesthetic judgements sitting on top of those — "this reads as a decal", "these two are the
same picture" — are inference from the code and should be confirmed in Play Mode before
anyone tunes a constant on their strength.

### The seven axes

| Axis | Question | The law behind it |
|---|---|---|
| **A** Silhouette | Does the rig have the SHAPE of the thing it draws, or is it a disc? | L1 |
| **B** Identity | Distinguishable from the other 72 spells at a glance? | L9 |
| **C** Legibility | Does the drawn boundary tell the truth about the damage? | L8 |
| **D** Layer & material | Is there a dark OPAQUE layer? Is the additive alpha budget sane? | L2, L3 |
| **E** Event rhythm | A discrete event layer near 30 % duty, or continuous motion? | L4 |
| **F** Key moments | Cast, sustain, impact, expiry — how many are drawn? | L13 |
| **G** Light & colour | Is `particleColor` authored AND read? Is there a `Light2D`? | L10, L14 |

---

# Part 1 — The scores

| Spell | School | A | B | C | D | E | F | G | **Score** |
|---|---|--:|--:|--:|--:|--:|--:|--:|--:|
| `ice_lance` | Cryomancy | 9 | 9 | 8 | 8 | 8 | 8 | 8 | **8.5** |
| `arcane_barrier` | Arcana | 9 | 9 | 8 | 8 | 8 | 9 | 8 | **8.0** |
| `guardian_light` | Radiance | 8 | 4 | 5 | 8 | 7 | 6 | 8 | **6.5** |
| `raise_thrall` | Umbramancy | 0 | 5 | 6 | 6 | 7 | 5 | 7 | **4.5** |
| `charged_bolt` | Pyromancy | 1 | 6 | 5 | 7 | 8 | 4 | 7 | **4.5** |
| `glacial_step` | Cryomancy | 5 | 4 | 1 | 3 | 4 | 5 | 6 | **3.8** |
| `shadow_step` | Umbramancy | 5 | 3 | 4 | 2 | 4 | 5 | 5 | **3.7** |
| `frozen_ward` | Cryomancy | 2 | 2 | 5 | 2 | 5 | 5 | 7 | **3.5** |
| `blessing` | Radiance | 2 | 2 | 5 | 2 | 5 | 4 | 7 | **3.5** |
| `barkskin` | Verdant | 1 | 2 | 5 | 1 | 5 | 5 | 6 | **3.0** |
| `spore_cloud` | Verdant | 3 | 4 | 0 | 4 | 1 | 5 | 2 | **3.0** |
| `sanctuary` | Radiance | 3 | 3 | 0 | 4 | 3 | 3 | 5 | **3.0** |
| `static_field` | Stormcalling | 3 | 3 | 6 | 3 | 2 | 5 | 2 | **3.0** |
| `radiant_burst` | Radiance | 3 | 3 | 3 | 2 | 1 | 3 | 4 | **2.8** |
| `frost_nova` | Cryomancy | 2 | 4 | 3 | 1 | 1 | 3 | 4 | **2.5** |
| `war_cry` | Martial | 1 | 1 | 4 | 1 | 4 | 3 | 5 | **2.5** |
| `leap_slam` | Martial | 2 | 2 | 2 | 2 | 3 | 2 | 6 | **2.5** |
| `thunderclap` | Stormcalling | 1 | 3 | 3 | 0 | 1 | 3 | 4 | **2.2** |
| `blizzard` | Cryomancy | 1 | 1 | 1 | 3 | 2 | 3 | 3 | **2.0** |
| `thorn_burst` | Verdant | 1 | 2 | 3 | 0 | 1 | 3 | 3 | **2.0** |
| `cinder_trail` | Pyromancy | 1 | 1 | 3 | 3 | 2 | 2 | 3 | **2.0** |
| `entangle` | Verdant | 1 | 2 | 1 | 0 | 1 | 2 | 3 | **1.5** |
| `summon_wolf` | Verdant | 1 | 1 | 2 | 2 | 0 | 2 | 1 | **1.0** |
| `curse_of_frailty` | Umbramancy | 0 | 1 | 1 | 2 | 0 | 1 | 3 | **1.0** |
| `void_lance` | Umbramancy | 0 | 0 | 0 | 0 | 1 | 2 | 3 | **0.5** |
| `seeking_shard` | Stormcalling | 0 | 0 | 0 | 0 | 2 | 2 | 3 | **0.5** |
| `scatter_volley` | Martial | 0 | 0 | 0 | 0 | 0 | 1 | 2 | **0.5** |

**Distribution:** 2 shippable (≥8) · 1 close (6-8) · 2 half-built (4-6) · 16 placeholder (2-4)
· 6 absent (<2).

**By school:** Arcana 8.00 (n=1) · Cryomancy 4.06 · Radiance 3.95 · Pyromancy 3.25 ·
Umbramancy 2.42 · Verdant 2.10 · Stormcalling 1.90 · **Martial Forms 1.83**. The school the
roadmap gave the strongest identity — *"nothing here glows because it is enchanted"* — scored
last, and for a reason worth stating on its own: two of its three spells are invisible and the
third casts a rotating magic sigil.

## The shape of the failure

| Axis | Mean | |
|---|--:|---|
| **A** Silhouette | **2.41** | `██████████` |
| **D** Layer & material | 2.74 | `███████████` |
| **B** Identity | 2.85 | `███████████` |
| **C** Legibility | 3.11 | `████████████` |
| **E** Event rhythm | 3.19 | `█████████████` |
| **F** Key moments | 3.74 | `███████████████` |
| **G** Light & colour | 4.63 | `███████████████████` |

The axes fail in almost exactly the order the roadmap predicted, which is the most useful
single result here. **A is the worst, and A is Law L1** — the law whose own text calls
reaching for the convenient radial rig *"the single most repeated mistake in this codebase"*.
It was written down at the top of the specification and then broken twenty-four times out of
twenty-seven. Writing the law did not prevent the defect; only a rig that already has the
right shape does.

G scores highest because the one instruction that was a FIELD rather than a BUILD was
followed: all 27 author a `particleColor`, none is the opaque-white sentinel, and the ten
legacy spells still in that state were not added to. That is a real win and it is also the
ceiling of what authoring alone can buy — as Part 2 shows, on eleven of the 27 that authored
colour never reaches the drawing.

---

# Part 2 — Six systemic failures

Each is one root cause with many symptoms. Fixing one fixes a column of the table.

## S-1. Six spells draw nothing in flight

The single most important finding, and it is not a matter of taste.

`ProjectileExecutor.AttachVisual` gives a spell the bespoke `IceLanceProjectileVisual` only
when `IceLanceArt.Matches(spell)`, which is `spell.spellKey == "ice_lance"` —
a hardcoded string (`IceLanceArt.cs:17`). Everything else receives `ParticleProjectileVisual`,
whose `Awake` calls `HideRootSpriteRenderer()` and whose `StartTrail` opens with
`if (_trailPresetIds.Count == 0) return;`. That list is filled from
`spell.CollectVfxPresets()`.

**All 27 ship `vfxPreset` empty.** Renderer hidden, no presets, nothing drawn. The four legacy
balls (`fireball`, `darkball`, `iceball`, `lightball`) each carry a preset, which is the only
reason the plumbing looks like it works.

Affected: `void_lance`, `curse_of_frailty`, `seeking_shard`, `scatter_volley`, and the
payloads of `charged_bolt` and `raise_thrall`. A player casting `scatter_volley` sees a cast
flourish, then five correctly-fanned nothings, then five generic impact blobs.

This is why the expansion can look finished in a data test and be absent on screen: no test
asserts that a spell can be SEEN. `SpellExpansionDataTests` pins the `particleColor` sentinel
and stops there.

## S-2. One rig, many spells — the monoculture

Grouped by what is actually built rather than by school:

| Rig | Spells | What varies between them |
|---|---|---|
| `SimpleVFX` soft disc, 0.5 s | `frost_nova`, `thorn_burst`, `entangle`, `radiant_burst`, `thunderclap` | hue only |
| `BuffAuraFX` ring + glow + 6 motes | `frozen_ward`, `barkskin`, `blessing`, `war_cry` | hue only |
| `AreaFXRig` four concentric discs | `blizzard`, `cinder_trail`, `sanctuary`, `spore_cloud` | hue only, and two share a palette |
| `TransporterFX` | `glacial_step`, `shadow_step` (+ legacy `teleport`) | tint only |
| Bespoke | `ice_lance`, `arcane_barrier`, `guardian_light`, `charged_bolt`(charge), `raise_thrall`(mark) | — |
| Nothing | the six of S-1 | — |

Two measurements make "hue only" precise rather than rhetorical:

- **`BuffAuraFX` reads exactly two fields of the `SpellDefinition`: `duration` and
  `particleColor`.** Every geometry, count, alpha, spin, radius and interval is a `const`
  (`BuffAuraFX.cs:33-56`). Ice armour, growing bark, a column of light from the sky and a
  shout are the same picture. All four also author `radius: 0`, so even the cast flourish
  resolves to the same 1.7 for all four.
- **`blizzard` and `cinder_trail` share one palette object.** `PuddleController.cs:108` calls
  `AreaPalette.LavaPuddle()` unconditionally, and the comment above it admits the element
  branch was removed because both arms returned lava. Since `AreaFXRig.MakeChild` never
  receives `radius`, the two spells draw the same sprites at the same size in the same colour.

## S-3. Eleven spells announce one colour and arrive in another

`particleColor` is authored on all 27 and reaches the drawing on sixteen. Where it does not,
a hardcoded palette wins:

- **`blizzard` is orange.** Authored `element: Ice`, `particleColor (0.72, 0.90, 1.00)`;
  drawn through `LavaPuddle()` as Ring `(1, 0.55, 0.10)` with embers rising.
- **`static_field` is gold and green.** `AuraController._tint` is assigned at line 121 and
  read nowhere; the palette is `GoldCore`/`GreenCore`, so a lightning field draws as a holy
  healing circle. Twelfth "authored and inert" sighting in this project.
- **`spore_cloud` is grey**, via `AreaPalette.Smoke()`.
- **`summon_wolf` is violet** — aura, light and both bursts are `SpellElement.Arcane` —
  against an authored Nature green.

## S-4. Colour cannot carry identity, because within a school there is none

With the rig shared, hue was the last thing left to tell two spells apart. It does not.

Measuring RGB euclidean distance between every pair inside a school: **28 of 39 pairs fall
below 0.15**, roughly the threshold under which two additive glows read as the same colour in
motion.

| School | n | closest pair | Δ | hue spread |
|---|--:|---|--:|--:|
| Radiance | 4 | `radiant_burst` / `sanctuary` | 0.022 | **1.1°** |
| Cryomancy | 5 | `frost_nova` / `frozen_ward` | 0.028 | 5.4° |
| Verdant | 5 | `thorn_burst` / `summon_wolf` | 0.035 | 28.6° |
| Fire | 2 | `charged_bolt` / `cinder_trail` | 0.072 | 2.4° |
| Stormcalling | 3 | `seeking_shard` / `static_field` | 0.077 | 1.5° |

Radiance spreads 1.1 degrees of hue across four spells. Combined with S-2, `radiant_burst` and
`sanctuary` are the same disc in the same colour.

## S-5. Nothing is opaque, so nothing affects the world

Law L3 asks for exactly one dark, opaque layer per rig — the chips, grit or debris that
separate "the world was affected" from "something was lit".

`AreaFXRig.MakeChild` puts every child on `SharedUnlitMaterial`; `BuffAuraFX` puts every layer
on `SharedAdditiveMaterial`; `SimpleVFX` assigns no material at all, so the five Area spells
inherit Unity's default LIT sprite material and dim at night. Across all 27, exactly three
rigs carry an opaque layer: `arcane_barrier`'s ground seal, `ice_lance`'s body, and
`sanctuary`'s totem post.

The inversions are the sharp end. The roadmap's brief for `thorn_burst` is explicit — *"the
thorns are the SILHOUETTE and they are NOT additive… deep green on `Sprite-Unlit-Default`"* —
and it ships as a disc of light: the exact opposite statement. `barkskin`'s brief says *"the
only additive layer… is what makes it read as living wood"* and *"No light: bark does not
glow"*; it ships 100 % additive with an unconditional 3.1 u light.

## S-6. Twenty-one of 27 have no event layer

Law L4: an effect made only of continuous motion stops being read after about a second, and
the fix is a discrete event at roughly 30 % duty. Measured duties on the persistent fields:

| Spell | Duty | What it is |
|---|--:|---|
| `arcane_barrier` | ~50 % across 3 desynchronised glyphs | the only one built as an event |
| `static_field` | **170 %** | pulse rings overlap permanently — continuous by arithmetic |
| `blizzard`, `cinder_trail` | 80 % | a size bump on an alpha material |
| `sanctuary` | 50 % | a light spike on the heal tick — the one beat carrying real information |
| `spore_cloud`, `summon_wolf` | 0 % | Perlin breathing and a sine pulse |

For the five Area spells the number is not a duty cycle at all: the whole spell is one
monotonic 0.5 s fade. `frost_nova`'s 3 s Chill, `entangle`'s 3 s Root and `glacial_step`'s
2.5 s Freeze all end with zero pixels on screen.

---

# Part 3 — What the audit found that is not aesthetic

An aesthetics pass traced every spell to the pixel and turned up six defects in the mechanics.
They are listed here because each one also destroys the look, and because four of them are
`particleColor`-shaped: a field authored, round-tripped, and read by nobody.

1. **`leap_slam` deals no damage.** `DashExecutor.ApplyPathContact` opens with
   `if (ctx.Spell.collisionDamage <= 0 …) return;` and the asset authors
   `collisionDamage: 0`. Its `damage: 30`, `radius: 2.6`, `knockback: 6` and `Stun 0.8 s` are
   all unread. `spawnAtMouse: 1` is ignored too — the executor uses `ctx.Direction * dist`, so
   it cannot leap to a point. The spell is a reskinned dash.
2. **`spore_cloud` deals no damage and applies no status.** `SmokeEmitterExecutor` is 32 lines
   with no `Physics2D` call anywhere. `damagePerTick: 4`, `Poison 4 s` and `Slow 1.2 s` reach
   no code.
3. **`sanctuary` heals a circle 0.21 units wide.** `TotemExecutor.cs:53` still reads
   `ctx.Spell.radius / 16f` — the **seventh** Python-pixel sighting, and the one that survived
   the commit that fixed its sibling: `AuraExecutor` was corrected on 2026-09-04 and the totem
   in the same commit had only its sweep fixed, not its units. The tell the roadmap names is
   present and was not read: the unauthored fallback is `13.75f`, sixty-four times anything
   the asset can produce.
4. **`glacial_step` freezes nothing.** `radius: 1.9`, `damage: 10`, `duration: 3` and
   `Freeze 2.5 s` are authored; `TeleportExecutor` performs no overlap, no damage and no status
   application.
5. **`summon_wolf` is still the white circle.** `summonTemplate` is EMPTY in the asset, so the
   executor's procedural 24 px disc is the creature — no `MonsterDefinition`, no animator, no
   `MeleeCombat`, no FSM brain, no `AlliedUnit`. It cannot attack. The roadmap's own Part 2
   flagged `SummonExecutor` as producing no creature; S8 shipped the faction targeting that
   makes allies possible and this spell was never pointed at it.
6. **`guardian_light`'s absorb pool is invisible and the shell stacks.**
   `ShieldController.Integrity` has zero readers in the project. `maxInstances: 0` lets a
   recast add a second shell, doubling every additive layer and re-opening the
   `SetInvincible` save/restore ordering the executor documents as fixed.

Two smaller ones: `curse_of_frailty` has no sigil, though `VulnerableEffect`'s doc-comment
twice justifies its weak tint on the grounds that a sibling rig "carries the reading" — that
rig was never written; and `SummonController` calls `PlaySfxById("spell_summon_create")`
ungated, which is a guaranteed console warning per cast since `AudioCatalog` holds no
`spell_*` id at all.

## Two more gaps that affect all 27

- **Icons: 0 of 27.** 47 of the 55 older spells carry one. In the grimoire and on the spell
  bar the entire expansion falls back to a role glyph, so the player's FIRST contact with each
  new spell is a generic shape. `Valkur > Spells > Assign Icons` already exists.
- **Sound: 3 of 27.** `AudioCatalog.asset` contains no `spell_*` id whatsoever, so every
  `PlaySfxById("spell_…")` in the project is a miss. Only `arcane_barrier`, `charged_bolt` and
  `raise_thrall` have a synthesised set. Twenty-four spells are silent, and Law L15 — the
  report of a distant effect arriving after its picture — cannot apply to a spell with no
  report.

---

# Part 4 — What the two good ones do

`arcane_barrier` (8.0) and `ice_lance` (8.5) are not better-funded versions of the others.
They are built on a different premise, and the difference is worth naming precisely, because
it is the template for everything below.

**They are shaped like what they draw.** The barrier has an unrotated, unscaled root with the
wall's direction carried in CHILD POSITIONS — three posts, four hexagon rows, a lattice cap —
so it stands up on screen whichever way it runs. The lance is a faceted spear 1.58 × 0.34 u
with two rear fins, rotated to its travel heading every frame. Neither could be produced by
scaling a disc, which is exactly why neither used one.

**Their events are events.** The barrier's glyphs run on 2.4-4.6 s periods with staggered
start ages and an in-fast/out-slow envelope, so the attack is sharper than the decay. The
lance's pierce steps `_power` 1.0 → 0.68 across its budget, recolours the body toward `Deep`,
narrows both trails and fires a shard spray — the damage falloff is legible on the projectile
instead of being a number in a tooltip.

**They own their whole timeline.** The barrier has a knit-in that runs ends-first, a hit beat,
progressive fracture with the collider resized to the surviving span, a shatter, and a melt
that runs the knit backwards — plus `ISpellEffectDissipates`, so eviction is a fade and not a
cut. Four of the five exit paths a persistent effect has are covered because the interface was
implemented.

**They derive colour instead of hardcoding it.** `ArcaneBarrierPalette.From` handles the white
sentinel, the achromatic request and the near-black case, so the spell's authored swatch
actually drives the rig.

The one thing `ice_lance` does NOT do is generalise: `Configure(spell)` ignores the
SpellDefinition entirely, so its `particleColor` and `scale` are dead and the rig is
hardcoded ice. That is the difference between a beautiful spell and a beautiful SYSTEM, and it
is the first item in the plan.

---

# Part 5 — The plan

Ordered by beauty bought per unit of work. Each stage ends with something visibly better, so
stopping early is safe.

### Stage 0 — Make the invisible visible (hours, not days)

Nothing else on this list matters while six spells draw nothing.

1. Author a `vfxPreset` on the six of S-1, copying the pattern the four legacy balls already
   prove works. This is a data edit and it converts six 0.5-scores into something.
2. Add the structural test the expansion lacked: **every player-castable spell must produce at
   least one renderer.** Per CLAUDE.md's own rule, this is the check that is independent of the
   value — it cannot be satisfied by authoring a colour, and it would have caught all six on
   the day they shipped.
3. Fix the four inert-field defects that are one line each: `TotemExecutor`'s `/16f`,
   `leap_slam`'s `collisionDamage`, `guardian_light`'s `maxInstances`, and the ungated
   `PlaySfxById` in `SummonController`.

### Stage 1 — Break the monoculture with profiles, not with rigs

The project already has the answer twice — `SlashProfile` dispatches on arc angle and
`CastFlourishProfile` on `SpellType`. Neither forks a class per spell.

4. **`BuffAuraFX` takes a shape profile.** One enum plus a small table turns four identical
   buffs into ice plates, bark strips, a descending column and a martial shockwave. The
   sprites already exist and are unreferenced: `ShieldSprites.Facet`, `RootSprites.Tendril`,
   `ElementalSprites.Snowflake`, `RootSprites.Clod`, `IceSprites.Crack`. **The gap is wiring,
   not art.**
5. **A `projectileVisualKey` on `SpellDefinition` plus a small factory**, replacing
   `IceLanceArt.Matches`'s hardcoded string and the four literal branches around it. Then
   `void_lance` is the ice lance rig with an opaque dark core and an additive violet rim —
   the spec's exact inverse — for a fraction of 806 lines.
6. **Kill the five-spell disc.** `AreaExecutor`'s entire visual output is one
   `SpawnAreaIndicator` call whose sprite overshoots to 1.2× the radius and collapses to 0.1×,
   so it is never the size of the damage on any frame. Replace with a ring pinned at
   `radius / 0.39` — the pinning alone lifts axis C for all five — and give each of the five
   its specified silhouette.

### Stage 2 — The identity repairs

7. **`war_cry` must stop casting a sigil.** `SpellType.Buff` routes to the `Ward` flourish,
   whose profile spins an expanding sigil with 18 orbiting motes. Martial Forms' whole identity
   is that nothing there is enchanted. This needs a martial family in
   `CastFlourishProfile`, and it is the single most damaging identity defect in the audit.
8. **`scatter_volley` gets opaque blades and no additive layer at all** — the one metal spell
   among twenty-six magic ones.
9. **Read the colour that is already authored**: `blizzard` off `LavaPuddle`, `static_field`'s
   dead `_tint`, `spore_cloud`, `summon_wolf`. Four spells, four palettes, no new rigs.
10. **Widen the in-school hue spread.** Radiance's 1.1° is not a palette, it is one colour used
    four times. Aim for a minimum pairwise ΔRGB of 0.15 within a school.

### Stage 3 — Event layers for the persistent fields

11. `static_field`'s crawling arcs (the spell's entire specified identity, currently absent),
    `spore_cloud`'s blooming puffs, `blizzard`'s gusts. Target ~30 % duty and, for the arcs,
    terminate preferentially on an enemy inside the dome — that turns a decorative layer into a
    damage indicator for free.
12. `guardian_light`: thread `Integrity` into `UpdateFacets`/`UpdateRim`. **One scalar unlocks
    all three of the spec's coupled readings** (facet opacity, accumulating cracks, colour
    cooling), and `ArcaneBarrierVisual.Damage.cs` already implements per-panel crack accretion,
    so the technique is in-project.

### Stage 4 — The expensive ones, honestly priced

13. `summon_wolf` needs a real `MonsterDefinition` and an `AlliedUnit`, plus the ally rim that
    is the only thing telling a player which creature is theirs. `raise_thrall` needs the same
    rim — its mark and its rising are already good, and beats 4 and 5 (the body rise, the
    reversed `GrayscaleDeath`) are what remain.
14. `leap_slam` needs the shadow that detaches from the feet and shrinks. Without it there is
    no jump, and every landing beat is decoration on a spell that never left the ground.
15. `cinder_trail` needs the patch-dropping trail that `followCaster` and `ttl` already
    describe and nothing reads.
16. Icons for all 27, and a synthesised audio set for the 24 silent ones.

## The tests that would have caught this

The project's recorded failure mode is authored-and-inert content. Five of the six defects in
Part 3 are exactly that, and none was caught. Add, in this order:

1. **Every castable spell renders something.** A structural check, independent of any authored
   value. This is the highest-value test in the list.
2. **World-unit sanity** — the roadmap already specified it and it would have caught
   `TotemExecutor`'s `/16f` on day one: no shipped spell has a radius, range or distance whose
   resolved value is below 0.5 u or above 20 u.
3. **Every new `SpellDefinition` field has a reader**, the direct analogue of
   `PlayerStatsWiringTests`. `collisionDamage`, `followCaster`, `ttl`, `summonTemplate` and
   `Integrity` would each have failed it.
4. **`particleColor` reaches the rig** — not merely that it is authored, which is what
   `SpellExpansionDataTests` checks today, but that the drawn colour derives from it.
5. **In-school hue separation** — a cheap guard against the palette collapsing again as the
   grimoire grows toward 100.

---

## Honest summary

The 27 spells are mechanically real and visually unbuilt. The expansion's own specification
was unusually good — the fifteen laws in Part 1 of the roadmap are hard-won and correct, and
this audit is essentially a measurement of how completely they were not followed. The reason
is structural rather than careless: **every law describes what a rig must BE, and the codebase
offers a convenient rig that is none of those things.** Given `AreaFXRig`, `BuffAuraFX` and
`SimpleVFX` as the path of least resistance, twenty-four spells took it.

The fix is therefore not twenty-four rigs. It is three profiles — one for buffs, one for
projectiles, one for ground fields — plus Stage 0's data pass, and the sprite vocabulary those
profiles need is already written and sitting unreferenced.

---

# Part 6 — What the remediation pass changed (2026-09-05)

Stages 0-3 of the plan above ran the same day the audit was written. **EditMode: 7321 / 7321,
console clean.** The work was done as three profiles plus a data pass, exactly as the closing
argument prescribed — not as twenty-four rigs.

## The three profiles

Each derives its look from mechanics the SpellDefinition ALREADY declares, so no new authored
field exists to fall out of sync with behaviour. That is the same pattern `SlashProfile` (arc
angle) and `CastFlourishProfile` (SpellType) use, and it is why a spell authored to pierce four
bodies cannot end up drawn as a ball: one number decides both.

| Profile | Dispatches on | Silhouettes |
|---|---|---|
| `ProjectileVisualProfile` | volley → pierce → homing → low-damage-with-duration | Blade, Lance, Spark, Wisp, Orb |
| `AreaBurstProfile` | the STATUS applied, not the element | Snare, Thorns, Rime, Shock, Radiance, Bloom |
| `BuffAuraProfile` | the stat trade the buff makes | Shell, Growth, Radiance, Fervor, Aura |
| `GroundFieldProfile` | field behaviour | Pool, Storm, Trail, Roots |

`AreaBurstProfile` dispatches on status rather than element for a measured reason: two of its
five spells author `element: Nature`, which parsed to nothing (see below).

## The defect that was invisible in code and fatal on screen

**Six projectiles drew ZERO pixels in flight.** `ProjectileExecutor.AttachVisual` gave the
bespoke rig only to `IceLanceArt.Matches` — a hardcoded `spellKey == "ice_lance"` — and
everything else got `ParticleProjectileVisual`, which hides the root SpriteRenderer in `Awake`
and returns early from `StartTrail` when the preset list is empty. All 27 ship `vfxPreset`
empty. Measured after the fix: 4-8 renderers each, plus trail and light, in five distinct
silhouettes. `scatter_volley` resolves to **0 lights and 3 opaque layers** — Martial Forms'
identity held by construction rather than by care.

## `SpellElement` had no `Nature`

The enum was Dark/Ice/Light/Lightning/Boomerang/Arcane/Fire. The five Verdant spells have all
authored `element: Nature` since they shipped, so `Enum.TryParse` failed on every one, they fell
through the legacy key switch, and it returned null — the whole school drew from whatever each
caller used for "no element". That is why a wolf summoned by a green spell arrived violet.
`Nature` is appended (safe: `element` is a STRING parsed by name, so no shipped asset stores
this enum as an integer) with a deliberately low-luminance green palette, because Verdant's rigs
carry their weight in OPAQUE layers and a bright additive green behind them washes the
silhouettes out.

## Mechanical defects fixed, all found by the aesthetics trace

- **`leap_slam` dealt no damage.** `collisionDamage: 0` short-circuited `ApplyPathContact`,
  leaving `damage: 30`, `radius`, `knockback` and its Stun unread. The dash-composed-with-area
  the roadmap assumed existed did not; it does now, and `spawnAtMouse` is honoured.
- **`spore_cloud` did no damage and applied no status** — `SmokeEmitterExecutor` had no
  `Physics2D` call at all.
- **`sanctuary` healed a 0.21 u circle.** `TotemExecutor` still read `radius / 16f`: the SEVENTH
  Python-pixel sighting, and the one that survived the commit that fixed its sibling.
  `healing_totem.asset` was re-authored 13.75 → 3 in the same pass, mandatory once the divide
  went.
- **`glacial_step` froze nothing** — the authored area is applied at both ends now.
- **`blizzard` was orange**, drawn through an unconditional `AreaPalette.LavaPuddle()`.
- **`static_field` was gold and green**: `AuraController._tint` was assigned and never read.
- **`guardian_light`'s absorb pool had zero readers.** `ShieldController.Integrity` now drives
  three coupled readings — facet opacity, cracks accumulating a quarter at a time (an EVENT, not
  a ramp), and the rim cooling gold→white. `maxInstances` 0 → 1, which had let a recast stack a
  second shell and double every additive layer.
- **`curse_of_frailty` had no sigil.** `VulnerableEffect`'s doc-comment justified its weak tint
  by pointing at a rig nobody had written. `CurseMarkFX` is that rig, and it PULSES at ~15 %
  duty rather than tightening — its sibling `ThrallMarkFX` tightens because a thrall mark is a
  bet against the clock; a curse is just an open window.
- **`summon_wolf` was still the white circle.** Routed through the existing
  `AlliedSummonService` / `AlliedUnit` / `MonsterSpawner` pipeline.

## Two traps re-encountered, both already in CLAUDE.md

`Destroy` is an outright ERROR in Edit Mode, and the new rig-swap path runs from EditMode tests —
it surfaced as an unhandled log message on an unrelated fixture, exactly as the note warns.
Both sites are mode-aware now. And `EntityStats` is a **struct**, so `stats == null` does not
compile — the same shape as the `InventorySlot` note.

## Still open, deliberately

1. **No wolf exists.** `Data/Catalogs/Monsters/` holds barbols, `knight_red`, `mon1` and six
   vendors. `summon_wolf` authors `barbol_musgo` as an explicit placeholder — verdant, non-boss,
   real art and FSM — so the spell works end to end. A **pet HP budget** (`PET_HP_BUDGET = 90`,
   measured against level-scaled stats and never scaling UP) makes the template's own pool
   irrelevant, so drawing a real wolf later changes its look and behaviour and not its
   durability.
2. **Icons: still 0 of 27.** A parallel session is building them (`tools/atlas/wave7/`).
3. **Sound: still 3 of 27.** `AudioCatalog` holds no `spell_*` id at all, so every
   `PlaySfxById("spell_…")` in the project is a miss. New call sites added in this pass are
   `HasSfx`-gated; the ungated one in `SummonController` was a guaranteed warning per cast.
4. **`guardian_light` vs `sphere_magic_shield`** now differ in the pool readings, but the two
   still share one rig and one silhouette.
5. **The scores in Part 1 have not been re-measured.** Nothing here was observed running: the
   claims that are firm are the greps (a rig that does not exist cannot be seen) and the live
   renderer counts. Re-scoring honestly needs a Play Mode pass with eyes on the screen.
