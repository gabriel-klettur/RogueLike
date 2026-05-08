# Spell HUD Icons

Square icons for the player spell action bar (`SpellBarHUD`, WoW-style grid). The
HUD slot is 40 px in-game; assets are generated at **256×256 with a transparent
background** so they downscale cleanly and stay sharp in tooltips/zoom.

## How they wire up

`SpellDefinition` carries **two separate sprite fields** under the `Visual`
header (`Assets/_Project/Scripts/Data/Spells/SpellDefinition.cs`):

- **`sprite`** — visual of the in-world projectile / area / mine / boomerang /
  summon / wall. Read by `ProjectileExecutor`, `BoomerangExecutor`,
  `MineExecutor`, `PuddleExecutor`, `SummonExecutor`, `WallExecutor`. Leave
  `null` to let the procedural visual (`FireballVisual`,
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
| Projectiles | `projectiles/` | 8/8   | Generated + assigned  |
| Melee       | `melee/`       | 4/4   | Generated + assigned  |
| Mobility    | `mobility/`    | 2/2   | Generated + assigned  |
| Area        | `area/`        | 11/11 | Generated + assigned  |
| Defense     | `defense/`     | 3/3   | Generated + assigned  |
| Utility     | `utility/`     | 2/2   | Generated + assigned  |
| Summoning   | `summoning/`   | 0/1   | Pending PNG           |

Total: **30 / 31** player-castable spells generated and wired. The remaining
slot is `summon_barbol.png` — once generated, drop it in `summoning/` and rerun
`Valkur > Spells > Assign Icons`.

Hostile / NPC-only spells (`hostile_slash*`, `hostile_dash`, `boss_barbol_slash`)
are intentionally excluded — they never appear in the player action bar. If a
bestiary or "damage taken" tooltip is added later, regenerate them using the
templates below.

## Conventions

- **Source resolution:** 256×256 PNG (Unity downscales to 40 px in HUD).
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
│                  lightning, chain_lightning, laser_beam, lightning_beam
├── melee/         slash, slash_cleave, slash_stab, slash_combo
├── mobility/      dash, teleport
├── area/          meteor_shower, flame_breath, arcane_flame, boomerang,
│                  vortex_pull, vortex_push, root_whip, puddle_lava,
│                  wall_ice, mine_basic, firework_launch
├── defense/       healing_aura, healing_totem, sphere_magic_shield
├── utility/       smoke, smoke_emitter
└── summoning/     summon_barbol
```

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

## Per-spell prompts

The blocks below already include the full style preamble — copy and paste each
one as-is.

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
3. Open `Assets/_Project/Data/Catalogs/Spells/<spellKey>.asset` and assign the
   sprite to the `Sprite` field under `Visual`. (Or use **F4** in-game.)
4. Add the prompt block to this README under the matching category so the
   regeneration recipe stays in source control.
