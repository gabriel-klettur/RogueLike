---
name: vfx-authoring
description: Valkur particle/VFX authoring — the art direction (HD glow over pixel-art world), the ParticleVfxParams field reference, recipes per `kind`, the gradient/curve rules, the additive-vs-alpha decision, per-preset budgets, naming, and the known engine gaps that cap how beautiful a preset can get. Load before creating or tuning any particle preset, touching ParticleEmitter, or working in the Particles Editor (F1).
---

# VFX Authoring for Valkur

> Companion agent: `particles-editor`. Companion skills: `unity-development`, `unity-performance`, `asset-pipeline`.

## 1. Art direction — the one rule that decides everything

**The world is pixel-art. The VFX layer is not.**

Valkur renders 16-PPU tiles and 32-PPU buildings, but particles live on the `VFX` sorting
layer and are deliberately **high-definition**: smooth, volumetric, saturated, glowing —
the look of Dead Cells / Hades / CrossCode, where crisp sprite art sits under soft
HD light. This is an explicit project decision, not an oversight.

Concretely, on the `VFX` layer:

| Pixel-art rule | Status on VFX layer |
|---|---|
| Snap to 1/16 world unit | **Suspended.** Particles move sub-pixel; that smoothness is the point. |
| Point-filter, no bilinear | **Suspended.** Particle textures use `Bilinear` + mipmaps. |
| Limited palette, hard edges | **Suspended.** Wide gradients, soft falloff, HDR-ish overbright. |
| Nearest-neighbour scaling | **Suspended.** Particle sprites may be authored at 128–256 px and scaled down. |
| One flat color per element | **Suspended.** Every preset should read as having *depth*. |

Everything else in `asset-pipeline` still applies — naming, folders, atlas policy.

### The four beauty levers, in order of payoff

1. **Texture.** An untextured quad can never look good. A soft radial-falloff sprite
   turns the same preset from "white square confetti" into glow. Valkur generates these
   procedurally — see `ParticleTextureShape` in §2 and the shape catalog in §2.1. Leaving
   a preset on `Auto` already gives it a sensible soft texture; picking the right shape by
   hand is better still.
2. **Gradient over lifetime.** Color *and* alpha must both move. A particle that is born
   hot-white, ages to its element color, and dies transparent reads as 3D; a constant-color
   particle reads as a decal.
3. **Size over lifetime.** Nothing in nature pops into existence at full size. Ease in,
   ease out. Burst presets get expand-then-shrink; continuous emitters get grow-then-fade.
4. **Layering.** One `ParticleSystem` is one material and one behavior. Beautiful effects
   in this project are **2–5 stacked presets** — core + wake + sparks + smoke — which is
   why `PP_portal_*` exists as `_core_soft` + `_rim_add` + `_sparks_add` triplets and why
   the fireball is nine presets across three stacks.

   Spells stack through `SpellDefinition`, which has three preset slots, each with a
   primary field plus a `…Layers` list: `vfxPreset` (trail, parented to the projectile),
   `impactPreset` (spawned at the hit point at 5× scale — author impact sizes divided by
   five), and `castPreset` (spawned unparented at the caster). Placed world emitters stack
   by placing several presets in the F1 editor.

   Reach for a new layer whenever one emitter would have to be two things at once — the
   classic being additive light and alpha-blended mass.

   One layer in a stack may be a **single quad**, not a crowd. A `Vortex` gate is 2–3
   long-lived overlapping quads at `emitRate` well under 1/s: set `rotationOneWay` so they
   all spin the same way, `startRotationJitterDegrees = 0` so they coincide exactly, and
   pick the spin so that `360 / arms` degrees fall in exactly one emission interval —
   45°/s at one quad every 4 s puts each copy 180° from its neighbour, which for a 2-arm
   spiral is the same picture. Miss that and the copies read as offset ghosts. Give the
   alpha curve ramps as long as the interval and the population fades in and out with a
   constant sum, so the gate never pulses while its quads recycle.

   A `ParticlePresetDefinition` can also carry its own `layers` list — child presets
   rendered by the SAME `ParticleEmitter`, one extra `ParticleSystem` per entry. This is
   a different mechanism from the `…Layers` lists above: those spawn separate placed
   presets/emitters at the spell level, this stacks sub-effects onto ONE placed instance
   (or one spell slot) so a single F1 placement, or a single `vfxPreset`, can already be
   a whole core+rim+sparks stack. One level deep only — a layer's own `layers` are
   ignored — and a `lightning`-kind layer is skipped, since lightning draws with a
   LineRenderer, not a `ParticleSystem`.

### Depth cues that sell "3D" on a 2D billboard

- **Overbright core, tinted rim.** Center near-white (`a=1`), edge the element hue.
- **Two-speed layers.** Fast small sparks + slow large haze = parallax without a camera move.
- **Additive on the light, alpha on the mass.** Fire's glow is additive; fire's smoke is not.
- **Elliptical shapes for ground effects.** A circle squashed to ≈ `0.5` on Y reads as a disc
  lying on the floor instead of a ring standing up facing the camera. There is no field for
  this yet — `ellipseRatio` is dead (§7); squash by scaling the emitter's transform.
- **Asymmetric alpha curve.** Fast attack (0 → 1 in the first 10 % of life), long decay.
  Symmetric fades look like a light switch.

## 2. Data model

Presets are ScriptableObjects at
`unity/Valkur/Assets/_Project/Data/Catalogs/Particles/PP_<id>.asset`, aggregated by
`ParticlePresetCatalog.asset`.

```
ParticlePresetDefinition          ScriptableObject
├── id            string          unique key, snake_case, no PP_ prefix
├── displayName   string          human label shown in the F1 picker
├── type          string          category shown in the picker ("aura", "portal", …)
├── vfx           ParticleVfxParams
└── layers        List<ParticlePresetDefinition>   optional child presets, one extra
                                  ParticleSystem per entry, rendered by the same
                                  ParticleEmitter as the root vfx. One level deep —
                                  a layer's own `layers` are ignored. Null entries,
                                  self-references and `lightning`-kind layers are
                                  skipped. Scaled by the emitter's scaleMultiplier
                                  exactly like the root vfx.
```

Note: `ParticlePresetDefinition` uses `[SerializeField] public` fields. That is a
deliberate exception to the "never public fields" rule — it is a serialization DTO
mirrored 1:1 by `ParticleInstanceSerializer`. Do not "fix" it.

### Copy on place — a placement owns its configuration

Editing a preset changes what the NEXT placement is born with. It does not reach the emitters
already standing in the world: each took a copy of the preset when it was placed
(`ParticleInstanceConfig`, stored per record in `particles_instances.json` v4) and has owned it
since. So:

| You are editing | It reaches |
|---|---|
| a preset, nothing placed selected (F1 picker) | every FUTURE placement, the preview, the asset on disk |
| a placement selected on the map | that one emitter, and nothing else |
| "Reapply Preset → This / → All Placements" | one placement, or every placement of that preset — the deliberate version of the old coupling |

Consequence for authoring: **tune a preset before you place a hundred of it.** A mass retune
afterwards is one button, but it is a button someone has to press, and it overwrites whatever
those placements had drifted to.

Everything a placement owns is persisted: the config (root block + snapshotable layers) is
written per record, and so are its position, zone, guid and `scale_multiplier`. A pre-v4 file
is migrated on its first load in the Editor and written back once — a placement frozen only in
RAM would thaw at the next restart. The file pays for it: the world's 190 records go from
~25 KB to ~300 KB, because a frozen copy is a full block, deliberately not a diff against the
preset (a diff would re-link them to it).

Not persisted, on purpose: the F1 undo stack, the selected box / hovered edge / open tab, and
the outline's smoothing — all session state.

### The leaf-fall family shares one behaviour

`falling_leaf_30s` ("Leaf Fall") is the reference. `falling_leaf_canopy`,
`autumn_leaves_gradient`, `falling_petal_30s` and `flowers_petal_pink_60s` are the same fall in
another colour: every motion, timing and turbulence field is copied from it verbatim, verified
by comparing the COMPOSED ParticleSystem modules and not the YAML — 42 module values, zero
mismatches.

What each one still owns is exactly what makes it itself:

| Kept per preset | Why |
|---|---|
| `colors`, `color`, `colorOverLife`, `colorIntensity`, `additive` | the colour is the whole point of the variant |
| `textureShape`, `textureSoftness` | Leaf (8) and Petal (9) are different silhouettes |
| `sizeMin` / `sizeMax` / `sizeAspect` | quad size is a look — no motion term reads it |
| `spawnWidth` | how wide the effect covers the world, and the axis the F1 drag handle owns |
| `sortingLayer` / `sortingOrder` | the deliberate stacking of co-located fields (canopy 10 over petals 5 over leaves 0) |

`spawnHeight` is NOT in that list even though it is a size: the visible band is
`spawnHeight + |gravityVector.y| x lifespan`, so a 1.2-tall box under the reference's 2-second
clock collapses the curtain to less than half of what the same preset drew at lifespan 8. It
travels with the timing fields.

Adding a new colour: duplicate `PP_falling_leaf_30s.asset`, change only the rows in that table.

### Per-instance size overrides

A preset is shared by every placement of it, so editing `spawnWidth` resizes all 84 leaf fields
at once. A single placement is resized instead through `ParticleInstanceOverrides` — three
RATIOS against the preset, stored on the instance and folded over the preset by
`ParticleOverrideApplier` when the emitter builds its systems:

| Ratio | Multiplies | Authored by |
|---|---|---|
| `spawnScaleX` / `spawnScaleY` | the emission area per axis (`spawnWidth`/`spawnHeight`; the emission radius by their geometric mean for the circular kinds, which have no ellipse to stretch) | dragging the EMISSION box's edges in F1 |
| `reachScale` | every motion term at once — `speed`, `gravity`, `gravityVector`, `radialSpeed`, `noiseStrength`, `swayAmp` | dragging the REACH box's edges in F1 |

Ratios rather than absolute sizes, so retuning a preset carries its instances with it, and so
they compose with `scale_multiplier` (also a ratio) instead of one silently overriding the
other. Persisted in `particles_instances.json` (schema v3) only when they differ from 1.

Two rules the applier is built on, and neither is optional:

- **The preset object is never mutated.** It belongs to a ScriptableObject every placement
  shares; writing to it resizes all of them.
- **One implementation, used by the emitter AND the marker.** A second copy of these rules is a
  box that stops matching the effect the first time either is touched.

`lifespan` is deliberately outside the reach ratio: stretching it changes how many particles are
alive at once — the density and the frame cost — which is a different edit from "reaches further".

### `ParticleVfxParams` field reference

Source: `Scripts/Data/Spells/ParticleVfxParams.cs`. Consumed by
`ParticleEmitter.ParticleSystem.cs` / `.Colors.cs` / `.Lightning.cs`.

| Field | Type | What it actually drives | Notes |
|---|---|---|---|
| `kind` | string | `ConfigureShape()` switch + a few special cases | See §3 for the full list. Unknown kind → Sphere r=0.15. |
| `loops` | bool | `main.loop` + `stopAction` | **Single source of truth** for burst vs continuous. `false` → `StopAction.Disable`. |
| `emitRate` | float | `emission.rateOverTime` | Continuous only. Floored at 0.02 — sub-1 rates are legal, and are how a preset made of two or three long-lived quads holds its population. |
| `count` | int | one `Burst(0f, count)` | Burst only (`loops=false`). Cast to `short`. |
| `burstIntervalSeconds` | float | `BurstLoop()` coroutine | **Dead** — gated by `IsBurstWithInterval()` which always returns false. |
| `speed` | float | `main.startSpeed` as `MinMaxCurve(0, speed*scale)` | Randomized 0→speed, so mean is half. |
| `gravity` | float | `main.gravityModifier = gravity / 9.81` | Ignored when `useGravityVector`. |
| `gravityVector` + `useGravityVector` | Vector2, bool | `velocityOverLifetime` in Local space | For sideways drift (rain, wind). NOT scaled by the emitter scale. |
| `orbitalSpeedDegrees` | float | `velocityOverLifetime.orbitalZ` | Angular rate about the emitter centre — the only field that makes a swirl. Unity's own unit is **radians/s**; the emitter converts. Not scaled (an angular rate is already size-independent). |
| `radialSpeed` | float | `velocityOverLifetime.radial` | World u/s along the centre→particle line; **negative draws inward**. Scaled by the emitter scale, so `radius / abs(radialSpeed)` is the time to the centre at any placed size — author `lifespan` to match. |
| `drag` | float | `limitVelocityOverLifetime.dampen` (clamped 0–1) | Only enabled when `> 0`. |
| `direction` | Vector2 | — | **NOT IMPLEMENTED — dead field.** No caller and no emitter path reads it. Authoring it does nothing. |
| `lifespan` | float | `main.startLifetime` | Floored at 0.05. |
| `sizeMin` / `sizeMax` | float | `main.startSize` range × scale | World units. |
| `colors[]` | Color[] | `BuildColorParameter` / `BuildFadeOutGradient` | **Only `[0]` and `[last]` reach `startColor`.** Middle entries only survive via `BuildFadeOutGradient` (max 8). |
| `color` | Color | fallback when `colors` empty | |
| `additive` | bool | blend factors + `_Blend` + `_EMISSION` on the shared material | See §4. |
| `textureShape` | enum | which procedural billboard texture the material gets | `Auto` derives it from `kind` + `additive`; `None` = the legacy hard quad. See §2.1. |
| `textureSoftness` | float 0–1 | falloff width of the procedural shape | Quantised to 16 steps before the texture cache is keyed. |
| `customSprite` | Sprite | overrides `textureShape` entirely | For hand-authored art. Uses `sprite.texture`, so the sprite should be its own texture, not an atlas sub-rect. |
| `startRotationJitterDegrees` | float | `main.startRotation` as ±jitter | 0 leaves every quad axis-aligned, which reads as a repeated stamp. Written unconditionally. |
| `rotationSpeedDegrees` | float | `rotationOverLifetime.z` as ±speed | Sign is per-particle. Written unconditionally. |
| `rotationOneWay` | bool | drops the ± and spins at the signed value | For a shape that IS the effect (a `Vortex` gate). Two overlapping quads with independent signs cancel into a flicker. |
| `worldSpace` | bool | `main.simulationSpace` | **The trail switch.** See §2.2. `kind == "dash"` forces world space regardless. |
| `radius` | float | shape radius for aura / portal | |
| `shapeFill` | float | `shape.radiusThickness`, applied after every kind and override | `-1` (default) keeps the kind's own choice — `aura` and `portal` emit from the RIM, every other circle from the whole area. `0` rim, `1` filled. |
| `outerRadius` | float | overrides `radius` for `portal` | |
| `ellipseRatio` | float | — | **NOT IMPLEMENTED — dead field.** No code squashes any shape; it only exists in `ParticleVfxParams.cs` and is serialized as `1` in every `.asset`. |
| `arcRangeDegrees` | float | `shape.angle * 0.5` for `slash` cone | |
| `dispersion` | float | shape radius for `smoke_emitter` | |
| `segments`, `lightningOffset`, `thickness` | int, float, float | `ParticleEmitter.Lightning.cs` LineRenderer | `kind="lightning"` only — no ParticleSystem is created. |
| `spouts[]`, `splashCount`, `dropletSize` | float[], int, float | — | **NOT IMPLEMENTED — dead fields.** Water-preset importer debt, no consumer. See §7. |
| `swayAmp` / `swaySpeed` | float | `noise.strength` / `noise.frequency` | **`kind == "falling_leaf"` only, AND only when the authored noise is off.** `ConfigureNoise` takes the authored branch whenever `noiseEnabled && noiseStrength > 0`; the legacy sway is its `else if`. Every shipped leaf preset authors noise, so on all of them these two rows are read by nothing — tune `noiseStrength` / `noiseFrequency` instead. |
| `stripeGap`, `rippleAmp`, `alphaBase`, `alphaWave`, `highlightColor` | — | — | **NOT IMPLEMENTED — dead fields.** `water_flow` legacy, no consumer. See §7. |
| `sizeOverLife[]` | Keyframe2D[] | `sizeOverLifetime.size` | If empty and `loops=false`, engine injects 0.3→1.0→0. If empty and looping, module is **off**. |
| `alphaOverLife[]` | Keyframe2D[] | gradient alpha keys (max 8) | Presence switches to `BuildGradientFromCurves`. |
| `colorOverLife[]` | ColorKeyframe[] | gradient color keys (max 8) | **Only read when `alphaOverLife` is non-empty.** Authoring color keys without alpha keys silently does nothing. |

### 2.1 Texture shapes

`ParticleTextureLibrary` generates every shape procedurally at runtime — no art assets, no
atlas entries, no `Resources/` footprint. Textures are 128², white RGB with the shape in
the alpha channel, so the preset's own colours do all the tinting. They are cached by
(shape, quantised softness) and marked `HideFlags.DontSave`.

| Shape | Looks like | Use for |
|---|---|---|
| `Auto` | resolved from `kind` + `additive` | The default. Safe, never ugly, rarely optimal. |
| `None` | hard-edged quad | The legacy look. Only when you genuinely want a square. |
| `SoftDot` | radial falloff | The general-purpose particle. |
| `Glow` | bright plateau core + wide skirt | Light, magic, auras, portal rims. |
| `Spark` | tight hot core, fast falloff | Sparks, slashes, embers, dash trails. |
| `Smoke` | cloudy value-noise puff | Smoke, dust, haze. |
| `Ring` | hollow annulus | Shockwaves, portal rims, ripples. |
| `Star` | four-point anamorphic flare | Sparkle, holy accents. |
| `Vortex` | two-arm logarithmic spiral with a hub | Portal mouths. Meant to be used ALONE — one long-lived quad plus `rotationOneWay` IS the effect; a crowd of them is mush. Circular texture: an oval gate comes from `sizeAspect`. |

`Auto` mapping: smoke kinds → `Smoke`; `slash` / `dash` / `firework` → `Spark`;
`aura` / `healing_aura` / `arcane_flame` → `Glow`; `portal` → `Glow` if additive else
`SoftDot`; leaf and water kinds → `SoftDot`; anything unrecognised → `Glow` if additive
else `SoftDot`.

`textureSoftness` widens the falloff: `0` is a crisp disc, `1` a diffuse haze. It also
controls ring band width and star arm thickness.

Materials come from `ParticleMaterialCache`, shared per (texture, blend mode) and assigned
to `sharedMaterial`. Never build a material inside an emitter — that breaks SRP batching
and leaks instances in EditMode.

### 2.2 Simulation space — the difference between a trail and a halo

`worldSpace` decides whether a particle, once emitted, belongs to the world or to the
emitter that made it.

- **Local (default).** Particles are carried along by the emitter. Correct for anything
  that IS the object: an orb, an aura ring, a shield.
- **World.** Particles stay where they were born. Required for anything that should be
  *left behind*.

This is not a subtlety. On an emitter parented to a projectile moving at 16 u/s, a
local-space "trail" travels with the projectile and leaves nothing at all — the whole
effect moves as one rigid blob. The fireball shipped that way: its layer was named
`fireball_wake` and could not wake.

Two consequences worth knowing before authoring:

- In world space the separation between particles comes from the **emitter's** motion,
  not from particle `speed`. Turning speed up scatters the trail sideways instead of
  lengthening it; lengthen with `lifespan` instead. Trail length ≈ `lifespan × emitter speed`.
- `kind == "dash"` forces world space regardless of the flag, which predates the field.

`ParticleEmitter` writes the module unconditionally, so a reused emitter cannot inherit
the previous preset's space — the same rule as shape, drag, bursts and rotation.

### The three gradient paths (know which one you are on)

`ParticleEmitter.Colors.cs` picks one of three, and the choice is not obvious:

1. `alphaOverLife` non-empty → `BuildGradientFromCurves` — full control, uses
   `colorOverLife` if present, else flat `colors[0]`. **This is the one you want.**
2. `alphaOverLife` empty → `BuildFadeOutGradient` — spreads `colors[]` evenly over life,
   hardcoded alpha `1.0 → 0.5 @ 0.6 → 0.0`. `colorOverLife` is ignored.
3. `startColor` independently uses `BuildColorParameter` — a two-color random pick of
   `colors[0]` and `colors[last]`, multiplied on top of path 1 or 2.

Consequence: `startColor` and `colorOverLifetime` **multiply**. Authoring a saturated
color in both fields double-darkens. Keep one of them near white.

## 3. `kind` recipes

`ConfigureShape()` recognises exactly these. Anything else falls through to a
0.15-radius sphere.

| `kind` | Shape | Beauty recipe |
|---|---|---|
| `aura`, `healing_aura` | Circle, `radiusThickness=0` (edge emit) | Slow rise, `additive`, long life, low `emitRate`. It emits as a true circle — `ellipseRatio` is dead (§7), so a floor-lying disc needs the emitter transform scaled on Y. |
| `portal` | Circle edge, `outerRadius` | Never one preset — stack `_core_soft` (alpha) + `_rim_add` (additive) + `_sparks_add` (additive, tiny, fast). The MOUTH is two more layers: a `Vortex` quad spun by `rotationOneWay` and an inflow of motes on `orbitalSpeedDegrees` + negative `radialSpeed`. See `PP_portal_oval_*` for the shipped five-layer version. |
| `dash` | Circle r=0.1, **World simulation space** | The only kind in world space; particles stay behind the mover. Short life, fast shrink. |
| `slash` | Cone, `angle = arcRangeDegrees/2` | Very short life (< 0.2 s), high count, additive. |
| `explosion`, `smoke_burst`, `firework` | Sphere r=0.1 | Set `loops=false` so the auto expand-shrink curve applies. Two layers: additive flash (life 0.15) + alpha smoke (life 1.2). |
| `smoke_emitter`, `smoke` | Circle, radius = `dispersion` | Alpha, never additive. Large `sizeMax`, low alpha, slow. |
| `arcane_flame` | Circle r=0.2 | Additive, upward `gravityVector`, hot-white → hue gradient. |
| `water_fountain` | Cone 15°, aimed +Y | `gravity > 0` for the arc. |
| `falling_leaf` | Box 2×0.1, replaced by `spawnWidth`×`spawnHeight` the moment either is authored | The flutter is `noiseStrength` / `noiseFrequency`; `swayAmp` / `swaySpeed` are the pre-noise fallback and are dead on every shipped leaf preset. |
| `water_flow` | Box 3×0.1 | |
| `lightning` | *No ParticleSystem* — LineRenderer | `segments`, `lightningOffset`, `thickness` only. Nothing else in the params applies. |

## 4. Additive vs alpha

`additive = true` sets `SrcAlpha / One`, `ZWrite off`, and enables `_EMISSION`.

**Use additive for:** light, fire, magic, sparks, energy, portal rims, holy/healing glow,
lightning. Anything that *emits* photons.

**Use alpha for:** smoke, dust, mud, leaves, petals, rain, blood, debris. Anything that
*blocks* photons.

Additive traps:
- Additive stacks toward white. Ten overlapping additive particles = a white blob with no
  hue. Compensate by **lowering per-particle alpha**, not by lowering count.
- Additive never darkens. A dark-magic effect needs an alpha layer for the mass and an
  additive layer only for the rim.
- Additive over a dark tile is dramatic; over a bright tile it is invisible. Check both.
- **The trap scales with area, not with count.** Overlap is what saturates, and a big soft
  particle overlaps everything near it, so the ceiling drops fast as size rises. A
  two-world-unit volumetric haze at a *sixth* of a percent of full alpha still pinned the
  frame to white once ~20 of them overlapped, square edges and all — the pollen stack's
  haze layer, which had to go back to alpha. Rule of thumb: **the light is additive, the
  volume is not.** If a layer's job is to occupy space rather than to glow, it is mass —
  author the warmth into its gradient, not into its blend mode.

### 4.1 Two things that bite when a preset is resized in F1

- **A small reach is a frozen effect, not a small one.** At the 0.05 minimum a leaf's drift is
  0.0275 u/s — nine tenths of a pixel over its whole life — while spawning and dying carry on
  unchanged. The F1 handles refuse to author past a measured floor (four art texels of lifetime
  travel, or a fifth of what the preset was authored to travel, whichever is larger) and say so
  in the status line. An orbital preset hits the same wall through the EMISSION box, because an
  orbit's arc is proportional to the radius it turns around.
- **Noise carries much further than its strength suggests.** Measured over the catalog's 44
  noisy presets, displacement reaches `3.67 x strength x lifetime` — a 0.22-strength haze with a
  seven-second life wanders 4.4 units. Budget the area accordingly, and expect a noisy preset's
  marker to be much larger than its spawn box.

## 5. Budgets

The runtime cost is `emitRate × lifespan` = steady-state live particle count. That number,
not `count`, is the one to hold.

| Context | Steady-state cap | Notes |
|---|---|---|
| Ambient world emitter (placed via F1) | **≤ 40** | Many are on screen at once; `ParticleInstancesLoader` culls off-camera but on-screen density adds up. |
| Player aura / trail | ≤ 60 | Always visible. |
| Signature spell (fireball) | ≤ 120 | A deliberate exception. NOT enforced by anything: `maxInstances` is unread metadata (see §7) and `FireballSignatureTests` never asserts a particle budget. Do not treat it as the general rule. |
| Spell impact (burst) | ≤ 120 per burst | Sub-second life, so peak-only. |
| Boss / set-piece | ≤ 250 across all stacked layers | Budget the *stack*, not each preset. |

Rules of thumb:
- Prefer **bigger, fewer, softer** particles over many small ones — cheaper and prettier.
- `sizeOverLife` shrinking to 0 is free; culling by shortening `lifespan` is not free
  because it changes the look. Shrink first.
- Every additive particle is overdraw. On the `VFX` layer overdraw is the dominant GPU
  cost; see the `unity-performance` skill.

## 6. Conventions

- Asset file: `PP_<id>.asset`, snake_case, in `Data/Catalogs/Particles/`.
- `id` field = the filename without `PP_`. `ParticleInstanceSerializer` persists this
  string into `StreamingAssets/Particles/*.json`; **renaming an `id` orphans every placed
  instance in the world.** Rename only with a migration.
- Layered effects share a stem and suffix the role:
  `PP_portal_oval_core_soft`, `PP_portal_oval_rim_add`, `PP_portal_oval_sparks_add`.
  Suffix `_add` marks an additive layer, `_soft` an alpha haze layer.
- Register every new preset in `ParticlePresetCatalog.asset` — an unregistered preset is
  invisible to the F1 picker and resolves to null at load.
- Never hand-edit `StreamingAssets/Particles/*.json`; it is written by the F1 editor
  through `IParticleInstanceRepository`.

## 7. Known engine gaps (what currently caps beauty)

These are the reasons a preset cannot yet look as good as the art direction demands.
Fix them in `ParticleEmitter` before spending long tuning numbers.

| Gap | Where | Impact |
|---|---|---|
| **No `textureSheetAnimation`.** | `ParticleEmitter.ParticleSystem.cs` | No animated smoke/fire sprite sheets. `PP_textured_spark_flipbook` is still misnamed — it has a texture now, but no flipbook. |
| **No `trails` module.** | same | Sparks have no streaks. |
| **No `lights` module.** | same | VFX do not light the URP 2D scene. |
| **`colors[]` middle entries dropped** in `BuildColorParameter`. | `ParticleEmitter.Colors.cs` | A 4-color preset renders as a 2-color random. |
| **Gradient keys capped at 8.** | `BuildGradientFromCurves` | Unity's own limit — fine, but silently truncates. |
| **`burstIntervalSeconds` dead.** | `IsBurstWithInterval()` returns `false` unconditionally | Repeating ambient bursts impossible. |
| **Noise hardcoded to `falling_leaf`.** | `ConfigureParticleSystem` | No turbulence for smoke or fire. |

**Dead fields — Python-importer debt.** These serialize, show up in the F1 inspector and
survive every round trip, but a grep over `Assets/_Project/Scripts/` finds **zero runtime
consumers** for any of them. They are leftovers from the Pygame preset importer. Authoring
them changes nothing on screen; do not tune them, and do not cite them in a recipe:
`direction`, `ellipseRatio`, `spouts[]`, `splashCount`, `dropletSize`, `stripeGap`,
`rippleAmp`, `alphaBase`, `alphaWave`, `highlightColor`. Either wire them up in
`ParticleEmitter` or delete them from `ParticleVfxParams` — leaving them half-present is
what caused presets to be authored against effects that never existed.

**Already fixed (2026-08-18)** — do not re-report these as gaps:

- Texture support (`ParticleTextureShape`, `ParticleTextureLibrary`, `customSprite`).
- Shared cached materials (`ParticleMaterialCache`), assigned via `sharedMaterial`.
- Transparent surface setup. URP's particle shader defaults to **Opaque**, and the old
  code never changed it — so every non-additive preset was rendering as a solid quad in
  the geometry queue. Materials now set `_Surface=1`, `_ZWrite=0`, correct blend factors,
  and `renderQueue = 3000`.
- Rotation (`startRotationJitterDegrees`, `rotationSpeedDegrees`). Both ranges are
  symmetric so each particle picks its own angle and spin direction — a whole system
  turning the same way reads as a rotating texture rather than as fire.
- Simulation space (`worldSpace`). See §2.2; this is the difference between a trail and
  a halo, and it was the reason the fireball's "wake" preset could not wake.

**Already fixed (2026-08-24)** — do not re-report these as gaps:

- Orbital and radial velocity (`orbitalSpeedDegrees`, `radialSpeed`), one-way spin
  (`rotationOneWay`) and authored emission fill (`shapeFill`). Unity's orbital velocity is
  ANGULAR and in RADIANS per second — measured, an `orbitalZ` of 1 turns a particle 57.296 deg
  in one second at any radius — while `radial` is linear world units per second; the emitter
  converts and scales only the linear term.
- The `Vortex` texture shape: a two-armed logarithmic spiral meant to be used ALONE, spun by
  `rotationOneWay`, with `sizeAspect` for an oval gate.
- Sub-1 emission rates. `emitRate` floors at 0.02/s, which is what lets a preset be two or three
  long-lived quads instead of a crowd.
- Per-instance size overrides and the F1 resize handles — see "Per-instance size overrides".

**Already fixed (2026-08-21)** — do not re-report these as gaps:

- Sub-emitters (`ParticlePresetDefinition.layers`). A preset can carry child presets
  rendered by the same `ParticleEmitter`, one extra `ParticleSystem` per valid entry
  (see §1 "Layering" and §2). Multi-stage effects no longer have to be hand-stacked as
  separate placed presets — one placed instance, or one spell slot, can already be the
  whole core+rim+sparks stack. One level deep only; `lightning`-kind layers are skipped.
- Spawn-area axes (`spawnWidth` / `spawnHeight`). The box override aimed itself with
  `FromToRotation`, which pins only the emission axis and leaves the roll around it
  wherever the shortest rotation lands — for the default upward heading that put the
  authored HEIGHT on world Z, the camera's own look axis. Area Height therefore spent its
  extent as invisible depth and every authored box emitted as a bare horizontal LINE.
  `BoxRotationFor` now fixes all three axes, so width and height both stay in the screen
  plane at any heading. Only the hair-thin depth axis is ever placed on Z. Re-check any
  preset authored against the old behaviour — its Area Height did nothing before and does
  something now.

Priority for the next step-change: `trails`, then `lights`, then
`textureSheetAnimation`. Each is a small additive change to `ParticleVfxParams` plus
one `ConfigureX` block — unlike further tuning of numeric fields, which is hitting
diminishing returns.

## 8. Workflow for a new preset

1. Decide the **stack**: how many layers, which is the light (additive) and which is the
   mass (alpha).
2. Duplicate the closest existing `PP_*.asset` rather than starting blank — the field
   defaults are not all sensible.
3. Set `kind` from §3 (it drives the emission shape), then `loops`.
   Then set `textureShape` from §2.1 — `Auto` is acceptable, explicit is better — and dial
   `textureSoftness`.
   If the layer should be left behind by a moving emitter, set `worldSpace` (§2.2). If it
   IS the moving thing, leave it local.
4. Author `alphaOverLife` **first** — without it you silently fall onto the hardcoded
   fade path and `colorOverLife` is ignored.
5. Author `colorOverLife` hot-to-cool. Keep `colors[0]` near white so the multiply does
   not double-darken.
6. Author `sizeOverLife`. Never leave a looping emitter without one.
7. Check the steady-state count against §5.
8. Register in `ParticlePresetCatalog.asset`.
9. Preview in play mode with F1 (`ParticlePreviewService`) over both a dark and a bright
   tile.
10. Run `mcp_unity_refresh_unity` + `mcp_unity_read_console` — console clean before done.
