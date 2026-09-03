# Boomerang — audit and rebuild

**Audited and rebuilt 2026-09-02.** Score as found: **2.5 / 10**. Score after this pass: **7.4 / 10**.

The boomerang was a 2021 prototype that survived three migrations without being looked at again.
It was not a spell that looked bad; it was a spell whose defining behaviour — coming back — had
never once run in a shipped build.

---

## The capital defect: the throw never returned

`BoomerangExecutor` instantiated the **shared ball prefab** (`ProjectilePrefabFactory`) and never
took its `Projectile` component off the clone. That component was never initialised, so it rode
along with its serialized defaults. Verified live in Play Mode:

```text
comp[4]=Projectile      <-- parasite, never Initialize()
comp[5]=BoomerangProjectile
comp[6]=ElementalProjectileVisual
proj.range=20   proj.lifetime=3   proj.damage=20   proj.speed=10
```

`Projectile.Update` runs `if (distSq >= range * range) Expire()` with `range = 20`. The spell was
authored `range: 26.25` at `speed: 82.5`, so the blade passed 20 units **0.242 s** after leaving
the hand and was deactivated and destroyed there. The return leg was unreachable code.

Two more consequences of the same passenger, both silent:

- Its `FixedUpdate` wrote `_rb.velocity = Vector2.zero * speed` every physics step. The blade
  only kept moving because `BoomerangProjectile` was added to the GameObject *later* and
  therefore wrote last. No `DefaultExecutionOrder` exists anywhere in the project — that was
  luck, not design.
- `Projectile.Awake` sets `freezeRotation = true`; the boomerang sets it false. Same ordering
  coin-flip, this time deciding whether the blade span at all.

**Neither half was wrong on its own.** The prefab is right to carry a `Projectile` — every ball
spell needs it. The boomerang is right to reuse the prefab. The composition was the defect, which
is the same shape as the spawner coordinate drift, and it needs the same answer: a test that
asserts the composition, not either half.

---

## What was found, by axis

| Axis | Was | Now | What was wrong |
| --- | --- | --- | --- |
| Functional correctness | **1** | **9** | Never returned. The parasite is stripped and the flight is integrated by `Step(dt)` |
| Damage model | **2** | **8** | `OverlapCircleAll` every `Update` with no memory of who it had hit: damage scaled with framerate, and the return leg never stopped hitting because only the outbound leg checked the phase. Now a 0.4 s per-victim cooldown, `OverlapCircleNonAlloc`, and obstacle blocking |
| Authored data | **3** | **8** | `speed 82.5` / `range 26.25` — the fourth sighting of the Python pixel scale, after `wallWidth`, the totem radius and the vortex radius. Crossed a 33.33-unit screen in 0.40 s. Now 24 / 10 |
| Colour coherence | **2** | **9** | Three colours for one spell: violet cast flourish, green blade, white impact. Now one authored swatch feeding all three, and the identity is a red saber |
| Audio | **0** | **7** | `spell_boomerang_throw` and `spell_boomerang_impact` are not in `AudioCatalog.asset` — which holds no `spell_*` id at all. Silent, plus one console warning per id. Now synthesised (`BoomerangAudio`) |
| Visual identity | **5** | **8** | Good palette, wrong plumbing: `Z_SKY` used as a sorting order on the Entities layer, alpha material that cannot blow out, a ghost trail that orbited the spinning blade instead of following it |
| Game feel | **2** | **6** | 82.5 u/s is a bullet. Now a readable throw with a catch that makes a sound |
| Performance | **4** | **7** | `Instantiate`/`Destroy` bypassing the pool, allocating overlap query every frame. Query is now `NonAlloc`; the ember GameObjects remain |
| Architecture | **3** | **7** | The last user of `ElementalProjectileVisual`, which `ProjectileExecutor.StripLegacyVisualRigs` exists to remove. Kept and fixed rather than migrated, because the blade accent is the one thing `ParticleProjectileVisual` cannot draw |
| Authoring in F4 | **4** | **8** | Of the 8 fields `SpellFieldRelevance` exposed, four did nothing. `impactPreset` and `sprite` now work; `maxInstances` is honoured by the cooldown instead of being claimed |
| Tests | **1** | **8** | Only structural coverage. Nothing could have seen the parasite. `BoomerangSpellTests` now flies whole throws |
| Code quality | **5** | **8** | Dead sentinel branch, hard-coded `returnSpd`, magic numbers |

---

## What changed

### `BoomerangExecutor`

- `StripBallProjectileRig` disables **and** destroys the passenger `Projectile`. Both are needed:
  destruction is deferred to the end of the frame, so a component that is only destroyed still
  runs its `Update` and `FixedUpdate` for the rest of it.
- `ResolveTint` replaces a dead branch. The old test was `particleColor != Color.clear`; no
  shipped spell carries an alpha-zero swatch, so the fallback was unreachable and every
  boomerang was thrown white. The project-wide sentinel is **opaque white**
  (`KiPalette.IsUnauthored`), and the fallback is now the boomerang palette's own core.
- `SpellCastFlourishFX.ResolveSwatch` gained a `Boomerang` case, so the gather and the blade are
  asked the same question — the rule that file's own doc-comment already stated.
- `impactPreset` is passed through; an authored `sprite` keeps the root renderer visible.

### `BoomerangProjectile`

- **Motion is integrated, not delegated.** `Step(float deltaTime)` owns the flight; `Update`
  just forwards `Time.deltaTime`. The path is scripted, so the solver bought nothing, and a
  model that reads the frame clock itself cannot be measured from a test or from
  `execute_code` — the same argument `VortexFieldController.Tick` makes.
- Per-victim hit cooldown (0.4 s). A cooldown rather than a once-per-pass set, because the turn
  happens **on** a victim: clearing a set at the turn hands whoever caused it a free second hit
  on the next frame.
- Obstacle sweep against `WorldCollisionLayers.BlockingMask()` — the blade used to fly through
  every wall in the world. It turns back at the surface.
- A lifetime ceiling derived from the throw (three round trips), so a blade whose caster is
  teleported cannot chase forever.
- The return step is clamped to the remaining distance, so a fast blade lands in the hand
  instead of orbiting it.
- Hit feedback is spawned here rather than through `IProjectileVisual.OnImpact`: that seam is a
  one-shot, built so `Projectile` can announce the single impact that ends its flight, and a
  boomerang strikes several victims and keeps going.

### `ElementalProjectileVisual`

- Sorting moved from `Z_SKY (600)` on **Entities** to small orders on **Projectiles**. Z_SKY is a
  Z depth, not a sorting order — the identical mistake `LightningBoltFX` made — and Entities sits
  below Decorations, WallsTop, ObjectsHigh, Projectiles and VFX, so the spell in flight rendered
  under every wall top on screen.
- Every light layer moved to `ElementalSprites.SharedAdditiveMaterial`. On
  `Sprite-Unlit-Default` the brightest pixel a glow can make is its own colour, so the halo was a
  net luminance **loss** over pale ground. The accent stays on the alpha material on purpose: it
  is the only layer with a silhouette, and a blade dissolved into its own glow is not a blade.
- A new non-spinning **`Aura`** container holds every layer that has to face the direction of
  travel. The ghost trail hangs at negative local X and the motion stretch is applied on local X,
  so parenting them to a root that spins twice a second made the trail orbit the blade rather
  than follow it. The ember spray reads travel direction for the same reason.

### `BoomerangAudio` (new)

Three synthesised one-shots — throw, impact, catch — following `IceWallAudio` and `ShieldAudio`.
The whoosh is band-passed noise whose centre frequency sweeps with the throw and is chopped at
twice the spin rate, because a flat blade presents its edge to the air twice per revolution. The
impact is a bright 25 ms crack over inharmonic wood partials; the catch is the same body darker,
shorter and undamped by a ring, because a hand stops it.

### Shipped data

| Field | Was | Now | Why |
| --- | --- | --- | --- |
| `speed` | 82.5 | 24 | Inside the projectile family (16–30). 82.5 crossed the screen in 0.40 s |
| `range` | 26.25 | 10 | 26.25 is 79 % of the camera's width |
| `damage` | 18 | 22 | Damage used to be per frame; it is now at most one hit per victim per leg |
| `cooldownDuration` | 0.8 | 1.0 | Longer than the 0.83 s round trip, so the `maxInstances: 1` the asset claims is actually true |
| `particleColor` | opaque white | `(1.00, 0.12, 0.10)` | The unauthored sentinel. The cast flourish had no element and no swatch and gathered **arcane violet** in front of a green blade |

---

## Measured after the rebuild

Flown live in the Editor through `BoomerangProjectile.Step`, and through the executor on the
real shared prefab:

| Probe | Result |
| --- | --- |
| 60 Hz throw | turns at **10.01 u**, caught **0.40 u** from the hand, **0.83 s** (round trip is 0.833) |
| 144 Hz throw | turns at 10.02 u, caught 0.50 u, 0.82 s |
| 20 Hz throw | turns at 10.80 u, caught 0.00 u, 0.95 s |
| Caster walking away at 4 u/s | caught 0.31 u from the hand, 0.87 s |
| Caster destroyed mid-flight | blade destroyed, no orphan |
| Executor clone components | `Transform, Rigidbody2D, CircleCollider2D, SpriteRenderer, BoomerangProjectile, ElementalProjectileVisual` — **no `Projectile`** |
| Rig | 8 layers, all on `Projectiles`, orders 0–5; `Valkur/SpriteAdditive` on every light layer, `Sprite-Unlit-Default` on the blade accent only; all under a non-spinning `Aura` |
| Swatch | blade and cast flourish both resolve `RGBA(1.00, 0.12, 0.10, 1.00)` |

EditMode suite: **6619 / 6619 passed, 0 failed** (155.9 s).

### Colour identity — red saber

Requested after the rebuild: the spell reads as an intense lightsaber red, everywhere.

- `ElementPalette.Boomerang` went from green/wood to a three-part saber split — a near-white
  **hot core** `(1.00, 0.93, 0.90)`, a saturated red **mid** `(1.00, 0.22, 0.16)`, and a deep
  crimson **bloom** `(1.00, 0.08, 0.10)` over a dark `(0.62, 0.02, 0.06)` halo. The accent —
  the blade silhouette, and the only layer on the alpha material — is held hot and pale
  `(1.00, 0.72, 0.70)` so it stays the bright bar INSIDE the bloom. Dropping it to the same red
  as the glow dissolves the shape into its own light, which is the difference between a saber
  and a red smear.
- Every value is a step brighter than the green it replaced, and the light went 0.9 to 1.15.
  Luminance is not linear in hue — green carries 0.587 of it against red's 0.299 — so the same
  numbers in red land visibly dimmer.
- `particleColor` is `(1.00, 0.12, 0.10)`, which carries the hue into the cast flourish through
  `RecolouredTo`: measured, all six gather fields land at **hue 1 degree**, value 0.85 or above.
  It also drives the impact tint and the F4 picker preview, which reads `particleColor`
  directly whenever it is not white.
- The spell bar icon was a wooden boomerang with a GOLD energy arc (mean hue 26 degrees). It is
  recoloured in place to crimson (mean hue 359 degrees), brightest pixels desaturating toward
  white so the arc keeps a hot rim. Git holds the original.

Two tests pin it: `EveryLitLayerOfTheBladeIsRed` (every lit layer within 20 degrees of red and
saturated, hot core deliberately near-white) and `TheGatheredCastFlourishIsTheSameRed`.

### Angle consistency — the arc had to fit the world

Reported after the loop shipped: it misbehaved at some headings. Two causes, both measured over
24 headings from one spot in the shipped world.

**The obstacle probe was five times too fat.** The sweep used `hitRadius` (0.75, a 1.5-unit
circle) as the blade's physical width. That field answers a different question — how far from
the blade a victim can still be hurt, authored generously so a near miss on a moving target
still lands — and `Projectile` sweeps its own 0.15 collider. So the boomerang caught on scenery
nobody aimed at: **16 of 24 headings turned back early, one after 2.66 units of a 10-unit
throw.** `ObstacleRadius` is now the blade, not its reach. One number doing two jobs, again.

**The loop always turned the same way.** A wall on the bow side broke one heading while the
opposite heading flew clean — a behaviour that changes with the aim for a reason the player
cannot see. `ChooseBowSide` samples both sides once at cast time and turns toward the free one,
defaulting to clockwise so there is still one shape to learn.

**And the bow is sized off the leg, not off the range.** The first attempt at this measured
the room to the side and narrowed the loop to fit. It protected the flight and destroyed the
spell: measured from where the player actually stands in the shipped town, **17 of 24 headings
came back with less than half the authored bow**, most under a tenth of it — a boomerang flying
in a straight line, which is the thing this rebuild exists to stop. The clamp is gone. Each leg
now bows by a fraction of ITS OWN length, so a leg cut short by a wall is a small lens rather
than a full-width bulge on a three-unit run, and the shape is the same at any size.

Measured after the fix:

| Where | Result |
| --- | --- |
| Open ground, 24 headings | **24/24 identical** — full 10.00 reach, 1.28 s, bow 3.80, all caught |
| Where the player stands in town, 24 headings | **0/24 flat loops** (the clamp left 17/24 flat); 17/24 reach the full range; bow 0.38 to 0.74 of its leg; **24/24 caught** |
| Headings that still turn back early | they are the ones with a wall inside the throw on a straight, blade-thin cast — geometry, not a defect. A fireball dies at the same walls, and the blade still loops back from wherever it turned |

## Still open

- **The embers are still one `GameObject` each**, about 20 a second per blade. The rig predates
  `ParticleProjectileVisual`; pooling them, or porting the trail to a particle system while
  keeping the blade accent as a sprite, is the remaining performance item.
- **The spell is still not pooled.** `BoomerangExecutor` instantiates and destroys, where
  `ProjectileExecutor` goes through `VFXManager`. Worth doing when a second throwing spell exists.
- **No recorded audio.** The synthesised clips are honest but a recorded set is better, and the
  catalog path (`spell_boomerang_*`) is still the right home for it — `AudioCatalog.asset` holds
  no `spell_*` entry for any spell in the game, which is a project-wide gap and not this spell's.
- **No return telegraph.** The blade is readable going out; nothing on screen says where it will
  come back through. A ground trail marking the outbound path would answer it.

## What to remember

- A shared prefab is a set of components, and instantiating it accepts **all** of them. Check what
  rides along before adding your own behaviour to a clone.
- A test that asserts one half of a composition proves nothing about the composition.
- An unauthored colour has exactly one sentinel in this project, and it is **opaque white**.
