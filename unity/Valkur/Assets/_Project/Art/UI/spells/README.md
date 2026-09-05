# Spell HUD Icons

Square icons for the player spell action bar (`SpellBarHUD`, WoW-style grid). The
HUD slot is 40 px in-game; assets are square with a **transparent background** so
they downscale cleanly and stay sharp in tooltips/zoom.

Canvas size is **whatever the source gives**, never an upscale: the 30
prompt-generated icons are 1024×1024 because that is what the generator emitted,
and the 27 wave-7 icons are 320×320 because they were cut from a sheet whose
cells are 100–280 px. Blowing those up four- to ten-fold to match would buy no
detail and be paid for in `ui.spriteatlas`, for a slot that draws them at 40 px.

## How they wire up

`SpellDefinition` carries **two separate sprite fields** under the `Visual`
header (`Assets/_Project/Scripts/Data/Spells/SpellDefinition.cs`):

- **`sprite`** — visual of the in-world projectile / area / mine / boomerang /
  summon / wall. Read by `ProjectileExecutor`, `BoomerangExecutor`,
  `MineExecutor`, `PuddleExecutor`, `SummonExecutor`, `WallExecutor`. Leave
  `null` to let the procedural visual (`ParticleProjectileVisual`,
  `ElementalProjectileVisual`, etc.) drive the look — that is the default for
  fireball/iceball/lightball/darkball/lightning/laser_beam/etc.
- **`iconSprite`** — square HUD icon shown in the spell bar, drag-preview, and
  skill tree. Read by `SpellBarHUD`, `SpellDragContext`, `DraggableSpellItem`.
  This is the field the auto-assigner writes to. The HUD falls back to
  `sprite` only for legacy assets that still pack the icon there.

The PNGs in this folder map **only** to `iconSprite`. They must never be
written into `sprite` — doing so makes the icon fly across the screen as the
projectile, which is exactly the bug the auto-assigner now actively cleans up.

Unity resolves the reference by **GUID**, not by path — so folder structure
and filenames here are **organizational only**, but they drive the
auto-assigner *by name*, so do keep `<spellKey>.png` in sync with the asset's
`spellKey`.

Three ways to wire a HUD icon into a spell:

1. **Auto-assigner (recommended for batches).** Run
   `Valkur > Spells > Assign Icons (Dry Run)` to preview, then
   `Valkur > Spells > Assign Icons` to apply. The tool:
   - Walks every `SpellDefinition` under
     `Assets/_Project/Data/Catalogs/Spells/`.
   - Matches `spellKey` to `<spellKey>.png` anywhere under
     `Assets/_Project/Art/UI/spells/` and writes it to `iconSprite`.
   - **Clears `sprite`** if it points to a PNG under that same folder
     (auto-fixes the historical bug where HUD icons leaked into the in-world
     sprite field).
   - Source:
     `Assets/_Project/Scripts/Editor/Spells/SpellIconAutoAssigner.cs`.
2. **Inspector.** Open the `<spellKey>.asset` and drag the PNG into the
   `Icon Sprite` field under `Visual` (NOT `Sprite`).
3. **In-game editor (F4).** Pick the spell and assign from the icon picker.

Until one of those runs, the spell bar slot will render empty (icon disabled)
even though the PNG exists on disk.

## Status

| Category    | Folder         | Files | Status                |
| ----------- | -------------- | ----- | --------------------- |
| Projectiles | `projectiles/` | 21/21 | Generated + assigned  |
| Melee       | `melee/`       | 5/5   | Generated + assigned  |
| Mobility    | `mobility/`    | 5/5   | Generated + assigned  |
| Area        | `area/`        | 21/21 | Generated + assigned  |
| Defense     | `defense/`     | 8/8   | Generated + assigned  |
| Utility     | `utility/`     | 4/4   | Generated + assigned  |
| Summoning   | `summoning/`   | 2/2   | Generated + assigned  |
| Charges     | `charges/`     | 7/7   | Generated + assigned  |

Total: **73 files**, in three batches — 30 prompt-generated, the 27 of the spell
expansion (*Wave 7*), and the 16 that closed the last gaps (*Wave 8*). **Every
player-castable spell now has an icon of its own**, including `summon_barbol`,
which had been pending since this file was written.

The 22 catalog entries still without an `iconSprite` are all `AnimationProbe`
spells. That is correct and permanent: a probe exists so an animation can be
selected in the F4 editor, it is never shown in the player action bar, and
`SpellCastFlourishFX.AppliesTo` refuses it outright.

Hostile / NPC-only spells (`hostile_slash*`, `hostile_dash`, `boss_barbol_slash`)
are intentionally excluded — they never appear in the player action bar. If a
bestiary or "damage taken" tooltip is added later, regenerate them using the
templates below.

## Conventions

- **Source resolution:** native — 1024×1024 for the prompt-generated set,
  320×320 for the sheet-cut wave-7 set. Never upscaled to match a neighbour.
- **Background:** fully transparent. The HUD slot already paints frame +
  cooldown overlay + hotkey label + mana cost.
- **No border, no text, no frame** baked into the icon.
- **Filename = `<spellKey>.png`** for one-to-one traceability with the catalog
  asset (recommended even though Unity binds by GUID).
- **Folder = category** (purely organizational; the loader does not resolve by
  folder).
- **Style:** pixel art, 16/32-bit roguelike, vibrant saturated palette, strong
  silhouette, soft inner glow.
- **Import settings:** standard UI sprite (PPU is irrelevant for HUD sprites
  rendered through `Image`; let the postprocessor apply the default UI atlas
  policy).

## Folder layout

```text
unity/Valkur/Assets/_Project/Art/UI/spells/
├── projectiles/   fireball, iceball, lightball, darkball,
│                  lightning, chain_lightning, laser_beam, lightning_beam,
│                  ice_lance, void_lance, curse_of_frailty, raise_thrall,
│                  seeking_shard, scatter_volley, charged_bolt,
│                  laser_beam_{red,blue,green,yellow,white,black}
├── melee/         slash, slash_cleave, slash_stab, slash_combo,
│                  slash_regular
├── mobility/      dash, teleport, glacial_step, shadow_step, leap_slam
├── area/          meteor_shower, flame_breath, arcane_flame, boomerang,
│                  vortex_pull, vortex_push, root_whip, puddle_lava,
│                  wall_ice, mine_basic, firework_launch,
│                  frost_nova, blizzard, thorn_burst, entangle, spore_cloud,
│                  radiant_burst, thunderclap, static_field, cinder_trail,
│                  arcane_barrier
├── defense/       healing_aura, healing_totem, sphere_magic_shield,
│                  frozen_ward, barkskin, blessing, sanctuary, guardian_light
├── utility/       smoke, smoke_emitter, war_cry, weapon_toggle
├── charges/       charge_ki_{spirit,azure,verdant,solar,
│                  crimson,violet,void}
└── summoning/     summon_wolf, summon_barbol
```

The folder is the spell's own `SpellType`, which is why `curse_of_frailty` and
`raise_thrall` sit under `projectiles/` — both are `SpellType.Projectile`, cast
at a target. Nothing resolves by folder, so this only has to stay honest, not
stay pretty.

## Prompt style guide

Every prompt below was authored against the same base style. To regenerate or
add new icons, paste the **style prefix** before the per-spell subject so the
palette and finish stay consistent:

```text
Pixel art spell icon, 256x256, perfectly centered, transparent background,
strong readable silhouette, vibrant saturated palette, soft inner glow, subtle
dithering, retro 16/32-bit fantasy roguelike HUD style (Hyper Light Drifter /
Moonlighter UI), no text, no border, no frame, no watermark, crisp pixels,
clean rim light. Subject:
```

Recommended **negative prompt** (when the generator supports one):

```text
text, letters, watermark, signature, frame, border, UI panel, background, blur,
photo, 3d render, realistic
```

For batch consistency: feed the first three generated icons (`fireball`,
`iceball`, `slash`) as a style reference (Midjourney `--sref`, DALL·E "in the
same style as", Stable Diffusion IP-Adapter, etc.) when re-rolling later icons.

## Wave 7 — the 27 spell-expansion icons

The icons for the 27 spells of `.github/SPELL_EXPANSION_27_ROADMAP.md` have a
different provenance from everything above: they were delivered as **one 1659×948
sheet**, four rows by seven columns with the last cell empty, not as 27 separate
generations. So there are no per-spell prompt blocks for them — the reproducible
recipe is the cutter, not a prompt.

```text
staging/spells/last_spells_added.png     the source sheet (gitignored)
tools/atlas/wave7/build_spell_icons.py   segment, trim, place, name
tools/atlas/generated/
  spell_icons_manifest_wave7.json        what was written, and from where
  spell_icons_wave7_contact.png          --contact-sheet output, for eyeballing
Valkur > Spells > Assign Icons           wire iconSprite
```

```bash
python tools/atlas/wave7/build_spell_icons.py --dry-run --contact-sheet
python tools/atlas/wave7/build_spell_icons.py
```

The sheet reads left-to-right, top-to-bottom (C1…C7 across, R1…R4 down). This
layout is duplicated in the cutter's `SPELLS` table, which is the authority:

- **R1** — `frost_nova`, `ice_lance`, `glacial_step`, `frozen_ward`,
  `blizzard`, `thorn_burst`, `entangle`
- **R2** — `barkskin`, `spore_cloud`, `summon_wolf`, `shadow_step`,
  `void_lance`, `curse_of_frailty`, `raise_thrall`
- **R3** — `radiant_burst`, `blessing`, `sanctuary`, `guardian_light`,
  `seeking_shard`, `thunderclap`, `static_field`
- **R4** — `scatter_volley`, `war_cry`, `leap_slam`, `charged_bolt`,
  `cinder_trail`, `arcane_barrier`, *(empty)*

Two things about that cutter are worth knowing before re-running it.

**It cannot cut on a grid, and band projection fails outright.** Every icon on
the sheet carries a wide soft glow, and rows 1–3 bleed into each other hard
enough that projecting the alpha onto the vertical axis reports *one* band 645 px
tall covering three rows — the only gap the projection finds is above row 4. So
the tool thresholds high to isolate the solid cores, clusters those onto the 4×7
grid, and then hands every remaining glow pixel to whichever core is nearest.
Where two glows overlap the boundary lands in the dim valley between them, which
is where a human would cut it too. `minerals/build_mineral_icons.py` cuts by band
projection and is the right tool for a sheet whose cells do not touch; this is
not that sheet.

**The table is declared by hand and has to be.** Hue says a cell is violet and
does not say whether that violet is a void lance, a curse or a raised thrall —
this sheet holds all three side by side in the same violet. The grid check is
what catches a mis-declaration: the tool refuses to write anything unless it
segments exactly 7/7/7/6 cells, so a sheet that gains or loses an icon fails loud
instead of silently shifting every name one cell to the left.

## Wave 8 — closing the shared-icon gaps

Sixteen more, delivered the same way (one 1448×1086 sheet, 4×4) and cut by the
same tool — `--only wave8`. What made these a batch is not an element or a
school: they were the spells that had **no icon of their own**. Six lasers and
`slash_regular` were borrowing another spell's PNG, and the seven ki charges,
`weapon_toggle` and `summon_barbol` had nothing at all.

- **R1** — `laser_beam_red`, `laser_beam_blue`, `laser_beam_green`,
  `laser_beam_yellow`
- **R2** — `laser_beam_white`, `laser_beam_black`, `slash_regular`,
  `weapon_toggle`
- **R3** — `charge_ki_spirit`, `charge_ki_azure`, `charge_ki_verdant`,
  `charge_ki_solar`
- **R4** — `charge_ki_crimson`, `charge_ki_violet`, `charge_ki_void`,
  `summon_barbol`

Three things shaped the art and are worth keeping.

**The laser family should look like a family.** All seven are `SpellType.Beam`
at `scale 2.0` and `range 6`, identical in every field but `particleColor` — so
one silhouette in seven hues is the honest icon set, not a failure of
imagination. `laser_beam_black` is the exception that needed a decision: its
authored colour is `#0D0D12`, and a black beam on the HUD's dark slot is
invisible, so it is drawn as a band of void **outlined** in magenta. Darkness
that reads by its contour, never by its fill.

**On the ki ladder, `scale` is intensity and not size.** It runs 0.15 to 1.00
across the seven, and what it moves at runtime is density and behaviour — 6 to
15 flame tongues, 35 to 130 sparks a second, no ground debris below 0.32, no
lightning below 0.60. So the seven icons are all the same height and escalate in
violence instead: `spirit` is a calm near-colourless column, `void` is saturated
with orbiting debris, constant lightning and a shock ring. Drawing them at
different sizes would have said the opposite of what the spell does.

**This sheet's background was not knocked out cleanly.** Only 0.2% of it is true
alpha 0, against 31% of the wave-7 sheet, and the residue is mottled colour
rather than black (mean RGB 97,87,98 over the 10% of it sitting at alpha 9–31).
It composites invisibly — floors of 8 and 48 are indistinguishable on mid-grey —
but it does mean the low-alpha band bridges neighbouring icons, which is why
`SEED_ALPHA` matters: at a threshold of 8 the tool segments **6** icons out of
this sheet, and 16 at anything from 48 up. It is also why the cutter now
alpha-bleeds every icon's edge colour outward, so nothing downstream that
averages neighbouring pixels can drag that junk back in.

## Per-spell prompts

The blocks below cover the 30 prompt-generated icons only; the 43 wave-7 and
wave-8 icons are cut from sheets and are documented above. Each block already
includes the full style preamble — copy and paste it as-is.

Note the preamble below asks for **pixel art**, which describes the original 30
and *not* the sheet-cut waves — those are painted, additive-glow, Diablo/PoE-style
icons. Match whichever set you are extending, and say which in the prompt.

### Projectiles

#### `fireball.png` — Bola de Fuego

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A glowing red-orange fireball with a bright yellow
molten core, swirling embers and curling flame wisps trailing behind, hot rim
light, magma cracks across the sphere, no text, no frame.
```

#### `iceball.png` — Bola de Hielo

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A crystalline ice sphere in deep cyan and pale
blue, jagged frost shards radiating outward, frozen mist trail, cold inner
glow, sharp icicle highlights, no text, no frame.
```

#### `lightball.png` — Bola de Luz

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A radiant golden-white holy orb, soft halo rays,
divine yellow bloom, sparkling motes, warm cream highlights, no text, no frame.
```

#### `darkball.png` — Bola de Oscuridad

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A pulsing void-purple sphere wreathed in swirling
black mist, violet cracks across its surface, eerie dark-energy tendrils, deep
magenta glow, no text, no frame.
```

#### `lightning.png` — Lightning

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A jagged neon blue-white lightning bolt striking
diagonally, electric sparks bursting outward, faint dark stormy aura behind,
high contrast, no text, no frame.
```

#### `chain_lightning.png` — Chain Lightning

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. Three small enemy targets connected by branching
electric blue arcs jumping between them, high-voltage glow, crackling sparks,
no text, no frame.
```

#### `laser_beam.png` — Laser

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A concentrated cyan-magenta laser beam firing
forward in a horizontal line, lens-flare burst at the muzzle, energy crackle,
neon glow, no text, no frame.
```

#### `lightning_beam.png` — Lightning Beam

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A continuous blue-white plasma beam with chained
electric arcs running along its length, crackling at both ends, deep blue
afterglow, no text, no frame.
```

### Melee

#### `slash.png` — Slash

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A curved silver sword arc slicing diagonally with
a clean white motion-line trail, polished steel gleam, faint cyan slipstream,
no text, no frame.
```

#### `slash_cleave.png` — Slash (Cleave)

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A wide horizontal sweeping two-handed slash, broad
steel arc with a red afterimage, heavy weight, dust kicked up at the bottom,
no text, no frame.
```

#### `slash_stab.png` — Slash (Stab)

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A forward sword thrust with a sharp pointed
motion arrow, white speedline, piercing thrust effect, focused tip glow, no
text, no frame.
```

#### `slash_combo.png` — Slash (Combo)

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. Three overlapping silver slash arcs forming a
dynamic X pattern, small glowing "x3" combo flair in a corner, energetic
motion, no text, no frame.
```

### Mobility

#### `dash.png` — Dash

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A stylized running humanoid silhouette with cyan
motion-blur streaks pointing forward, speed lines, trailing afterimages,
dynamic dash pose, no text, no frame.
```

#### `teleport.png` — Teleport

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A swirling magenta-violet portal with arcane
runes, a small humanoid silhouette stepping into it, purple sparkles, vortex
spiral, no text, no frame.
```

### Area / zone control

#### `meteor_shower.png` — Meteor Shower

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A large fiery meteor falling diagonally trailing
flame and smoke, two smaller meteors in the background, orange-red sky tint,
ember sparks, no text, no frame.
```

#### `flame_breath.png` — Flame Breath

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A wide cone of orange-yellow flames erupting
forward, dragon-breath shape, swirling smoke wisps at the edges, hot core, no
text, no frame.
```

#### `arcane_flame.png` — Arcane Flame

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A purple-violet ethereal flame with floating
magical runes orbiting it, ghostly indigo wisps, mystical glow, no text, no
frame.
```

#### `boomerang.png` — Boomerang

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A wooden curved boomerang spinning, circular
motion-arrow surrounding it, brown grain with engraved tribal runes, motion
blur, no text, no frame.
```

#### `vortex_pull.png` — Vortex Pull

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. An inward spiral with small curved arrows bending
toward the center, swirling purple-blue galaxy effect, gravitational
distortion, no text, no frame.
```

#### `vortex_push.png` — Vortex Push

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. An outward spiral with curved arrows blasting
away from the center, orange-red explosive shock-ring, kinetic burst, no text,
no frame.
```

#### `root_whip.png` — Root Whip

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A twisted thorny brown vine cracking like a whip,
small green leaves along the length, earthy palette, motion line, no text, no
frame.
```

#### `puddle_lava.png` — Lava Puddle

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A pool of bubbling molten lava seen from a slight
angle, glowing cracks across the surface, ember sparks rising, dark crusted
rim, no text, no frame.
```

#### `wall_ice.png` — Ice Wall

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A vertical row of jagged frost-blue ice spikes
forming a wall, cold mist swirling at the base, sharp crystalline highlights,
no text, no frame.
```

#### `mine_basic.png` — Basic Mine

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A spiked round metallic naval mine with a single
red blinking eye/light, fuse wires, yellow danger stripes, dark metal body,
no text, no frame.
```

#### `firework_launch.png` — Firework Launch

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A red rocket firework shooting upward with a
sparkling colorful trail of stars in red, gold, and cyan, festive burst, no
text, no frame.
```

### Defense / support

#### `healing_aura.png` — Aura de Curación

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A bright green plus/cross symbol surrounded by
soft radiating pulses of warm green light, leafy wreath around the cross,
gentle glow, no text, no frame.
```

#### `healing_totem.png` — Healing Totem

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A short carved wooden totem with a glowing green
crystal at the top emitting healing light, mossy stone base, tribal carvings,
no text, no frame.
```

#### `sphere_magic_shield.png` — Sphere Magic Shield

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A translucent blue energy bubble with hexagonal
shield pattern, soft cyan glow, faint runes etched on the surface, no text, no
frame.
```

### Utility

#### `smoke.png` — Smoke

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A swirling gray smoke cloud puff with darker
shaded core and lighter wisps curling outward, soft round edges, no text, no
frame.
```

#### `smoke_emitter.png` — Smoke Emitter

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A small cylindrical metallic canister venting
thick gray smoke clouds upward, brass body with rivets, glowing red activation
button, no text, no frame.
```

### Summoning

#### `summon_barbol.png` — Summon Barbol

```text
Pixel art spell icon, 256x256, centered, transparent background, retro 16-bit
fantasy roguelike HUD style. A treant/tree-creature silhouette ("Barbol") with
glowing yellow eyes and a gnarled wooden body emerging from a glowing arcane
summoning circle on the ground, mossy bark, no text, no frame.
```

## Hostile / NPC-only icons — aliasing

NPC-only spells (`hostile_slash*`, `hostile_dash`, `boss_barbol_slash`) don't
ship dedicated PNGs but still appear in the F4 Spells Editor table/picker.
The auto-assigner falls back to a hard-coded **alias map**
(`SpellIconAutoAssigner.cs`, `ICON_ALIASES`) so they re-use the player
equivalent's icon:

| Alias source                   | Icon used   |
| ------------------------------ | ----------- |
| `hostile_slash`                | `slash`     |
| `hostile_slash_red`            | `slash`     |
| `hostile_slash_cyan`           | `slash`     |
| `hostile_slash_dark`           | `slash`     |
| `hostile_slash_purple`         | `slash`     |
| `hostile_slash_gray`           | `slash`     |
| `hostile_slash_giant`          | `slash`     |
| `boss_barbol_slash`            | `slash`     |
| `hostile_dash`                 | `dash`      |

Aliases are only consulted when the spell does NOT have a PNG of its own —
drop a `<spellKey>.png` under any subfolder of `Art/UI/spells/` at any point
to take over from the alias automatically (no code change).

After wave 8 these nine are the **only** spells left sharing an icon, and they
share deliberately: eight of them draw `slash.png` and one draws `dash.png`,
none of them appear in the player action bar, and all nine are NPC or Boss
audience. The six `laser_beam_*` colour variants used to share `laser_beam.png`
too — they were never in the alias table, just assigned by hand — and wave 8
gave each its own.

If you ever want bespoke versions, derive them from the player templates:

- **`hostile_slash_<color>`** — same Slash prompt, but recolor the arc and
  afterimage to the variant: `red`, `cyan`, `dark` (black/violet), `gray`,
  `purple`. Replace the cyan slipstream with the variant hue.
- **`hostile_slash_giant`** — same Slash Cleave prompt at ×2 scale with a
  thicker, darker steel arc.
- **`hostile_dash`** — same Dash prompt with a red enemy silhouette and dark
  crimson motion streaks instead of cyan.
- **`boss_barbol_slash`** — same Slash Cleave prompt, but the arc is jagged
  green-brown wooden splinters and bark fragments instead of steel, with a
  heavy boss-tier glow.

## Adding a new spell icon

1. Generate a 256×256 transparent PNG with the style prefix above.
2. Save it as `<spellKey>.png` in the matching category folder. If the spell
   doesn't fit any existing category, create a new folder (no Unity wiring
   change needed — the loader resolves by GUID, not folder).
3. Run `Valkur > Spells > Assign Icons`. To do it by hand instead, open
   `Assets/_Project/Data/Catalogs/Spells/<spellKey>.asset` and drop the sprite on
   **`Icon Sprite`** under `Visual` — *not* `Sprite`, which is the in-world
   projectile and would make the HUD icon fly across the screen. (Or use **F4**
   in-game.)
4. Add the prompt block to this README under the matching category so the
   regeneration recipe stays in source control.
