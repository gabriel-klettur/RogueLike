---
name: asset-pipeline
description: "Manage the Valkur Unity asset import pipeline — sprite import policies, atlas building, PPU configuration, pivot normalization, audio import. Covers ValkurAssetPostprocessor, CharacterAtlasBuilder, TileAtlasBuilder, TilePaletteBuilder."
argument-hint: "Describe the asset category or pipeline task"
---

# Asset Pipeline

## When to Use
- Importing new sprites into Unity
- Building or rebuilding sprite atlases
- Configuring import settings (PPU, pivot, filter)
- Audio file import and configuration
- Validating asset import quality
- Atlas Phase 2 work (consolidation, naming, SpriteAtlas groups)

## Asset Categories & Import Rules

| Category | PPU | FilterMode | Compression | Pivot | Atlas Group |
|----------|-----|------------|-------------|-------|-------------|
| Tiles | 32 | Point | None | Center | env-tiles |
| Characters | 16 | Point | None | Bottom-center | characters |
| NPC | 16 | Point | None | Bottom-center | npc |
| Projectiles | 16 | Point | None | Center | spells |
| Spell VFX | 16 | Point | None | Center | spells |
| Explosions | 16 | Point | None | Center | vfx |
| Items | 16 | Point | None | Center | items |
| UI | 100 | Bilinear | None | Center | ui |
| Particles | 16 | Point | None | Center | vfx |
| Buildings | 32 | Point | None | Bottom-center | buildings |
| Audio SFX | — | — | DecompressOnLoad / PCM | — | — |
| Audio Music | — | — | Streaming / Vorbis 0.7 | — | — |

## Where assets live

Top-level folders are `PascalCase` (Unity convention); everything inside is `snake_case`.

```
Assets/_Project/Art/
├── Buildings/
│   ├── backgrounds/
│   ├── castles/
│   ├── houses/
│   └── ...
├── Characters/
│   ├── barbarian/
│   ├── dwarf/
│   ├── elven/
│   └── ...
├── Items/
│   ├── alchemy/
│   ├── cook/
│   ├── mining/
│   └── ...
├── NPC/                  (enemies + neutral NPCs)
├── Spells/
├── Tiles/
│   ├── dungeon/
│   ├── palettes/
│   └── tile_assets/
├── UI/
│   ├── editors/          (per-editor icon subfolders)
│   ├── hud/
│   ├── intro/
│   └── shared/
├── VFX/
│   └── Vendor/           (asset-store packs)
└── Misc/

Assets/_Project/Audio/
├── Music/
│   ├── biomes/
│   ├── zones/
│   ├── bosses/
│   ├── events/
│   └── stingers/
└── SFX/
    ├── ambient/
    ├── clash/
    ├── inventory/
    ├── menu/
    ├── npc/
    ├── player/
    └── spells/

Assets/_Project/SpriteAtlases/    ← single home for *.spriteatlas
Assets/_Project/Resources/        ← only files actually loaded via Resources.Load
Assets/_Project/Data/Catalogs/    ← all ScriptableObject catalogs
```

**Vendor / asset-store packs** live under `<Layer>/Vendor/<PackName>/` (e.g. `Art/VFX/Vendor/SlashVFX/`). Never at `Assets/` root.

**`Resources/`** is loaded **whole** into the build. Keep it minimal: only put assets that are actually loaded via `Resources.Load<T>` (the canonical example is `AudioCatalog.asset`, `SpellCatalog.asset`, input action assets). Everything else → direct references or Addressables.

## Postprocessor

[ValkurAssetPostprocessor.cs](unity/Valkur/Assets/_Project/Scripts/Editor/Asset/ValkurAssetPostprocessor.cs) auto-applies import settings on every reimport based on path:
- PPU by folder
- `FilterMode.Point` for pixel art
- No mipmaps
- No compression
- Correct pivot
- Audio: music = Streaming/Vorbis, SFX = DecompressOnLoad/PCM

Drop a new sprite into the appropriate folder and Unity handles the rest.

## Atlas builders

| Tool | Purpose |
|---|---|
| `CharacterAtlasBuilder.cs` | Pack character sprite sheets |
| `TileAtlasBuilder.cs` | Pack tile sprites |
| `TilePaletteBuilder.cs` | Generate Unity TilePalette assets |
| `SpriteAtlasBuilder.cs` | Generate `SpriteAtlas` for runtime packing |
| `BulkReimportTool.cs` | Force-reimport a folder applying current postprocessor rules |

## Prop-sheet pipeline (multi-object sheets → buildings)

Art often arrives as one big sheet holding dozens of separate props. Four stages turn such
a sheet into placeable buildings; every stage is re-runnable and the intermediate contracts
are versioned in the repo.

| Stage | Tool | In → Out |
|---|---|---|
| 1. Slice | `tools/atlas/slice_prop_sheet.py` | sheet PNG → one crop per object + `*.slices.json` + numbered previews |
| 2. Classify | manual / agent pass | crops → `tools/atlas/generated/building_props_metadata.json` |
| 3. Stage | `tools/atlas/build_building_props.py` | crops + metadata → `Resources/Buildings/<category>/*.png` + `building_props_manifest.json` |
| 4. Import | `Valkur/Buildings/Import Prop Sprites (Apply)` | manifest → `BuildingTemplateData` assets + `BuildingCatalog` entries |

Notes that cost time to rediscover:

- **Slicing is an alpha problem, not a chroma-key problem.** These sheets ship a real alpha
  channel; the visible "background" is soft glow at alpha 20–200. Segmentation runs on
  `alpha >= 190` cores, then hands every soft pixel to its *nearest* core via a distance
  transform — that is what keeps a brazier's flame with its own bowl while refusing to let
  two neighbouring glows bridge into one blob.
- **An object made of disconnected parts disappears silently.** A clover sprig is four
  separate leaves, each under `min_core_area`. `slice_prop_sheet.py` therefore audits its
  own output and reports any solid mass no box covers; fix those with an explicit entry in
  `tools/atlas/prop_sheets.config.json` (which also holds `drop` / `merge` / `split`).
- **Always resample in premultiplied alpha** (`Image.convert("RGBa")`). Downscaling straight
  RGBA averages the zeroed RGB of transparent pixels into every edge and rings the sprite
  with a dark halo.
- **Scale is the whole point of stage 3.** Buildings render at 32 px per tile and the player
  is 2 tiles, so a 430 px sheet lamp would stand 13 tiles tall. Metadata carries
  `target_height_tiles`; the tool resamples to `tiles * 32`.
- **Stage 4 is idempotent**, keyed on `assetPath`: re-running updates templates in place and
  keeps their `templateId`, so placed world instances never break. Neither of its menu items
  opens a dialog, because both are driven from the MCP bridge as often as from the menu bar.
- `Tests/EditMode/Game/World/BuildingPropCatalogTests.cs` asserts the catalog still matches
  the manifest. The source sheets are gitignored (`downloads/`), so the manifest is the only
  versioned record of what was imported.

## Monster sheet pipeline (character sheets → MonsterDefinition)

Authoring a new monster used to be a full day of manual work — see
`.github/ENTITIES_FSM_PVM_AUDIT.md`, "Authoring monster #21 — the concrete cost today". The
bottleneck was steps 1, 2, 5 and 6: no generic slicer existed (only a one-off knight script),
mirrors had to be hand-baked, up to 56 sprite fields had to be wired by hand (knight_red carries
585 references across 757 lines), and `MonsterCatalog.UpsertDefinition` had zero callers so
registration was a manual drag into the Inspector. Four stages now turn a character sheet into a
registered `MonsterDefinition`, the same shape as the prop-sheet pipeline:

| Stage | Tool | In → Out |
|---|---|---|
| 1. Slice | `tools/atlas/slice_prop_sheet.py` (unchanged, reused as-is) | sheet PNG → crops + `*.slices.json` |
| 2. Align + mirror | `tools/atlas/build_monster_frames.py` | crops + `--config` → `Art/NPC/monsters/<key>/*.png` + `monster_frames_manifest*.json` |
| 3. Import | `Valkur/Monsters/Import Frame Sheets (Apply)` (`MonsterFramesImporter.cs`) | manifest → `MonsterDefinition` assets + `MonsterCatalog` |

Stage 2 generalises `tools/atlas/wave2/build_knight_frames.py` (built for exactly one monster)
into a manifest-driven tool, the way `build_building_props.py` generalised prop staging:

- **Slicing alone jitters a cycle.** `slice_prop_sheet.py` trims every crop tight to its own
  alpha, which is right for a prop and wrong for an animation — a cape or a sword moves the
  bounding box frame to frame, so a tight-trimmed walk cycle jitters and the feet leave the
  ground. `build_monster_frames.py` pastes every frame of a state onto one SHARED canvas instead,
  anchored on the grid CELL's centre (never the body — that would cancel the hip sway/lunge the
  animation is made of) and on the row's lowest BODY pixel (the ground line), taken from each
  frame's largest connected component so a torn-off cape tip below the boots cannot drag the line
  down.
- **A turnaround idle anchors differently.** A static pose carries no motion to preserve, so
  `build_monster_frames.py` anchors those on the body itself: feet on the canvas floor, hips on
  the canvas centre line. Config declares which raw slice indices are which compass direction
  (`idle.poses`) and which directions to fill by mirroring an authored one (`idle.mirrorFrom`).
- **Mirrors are baked in Python, never at runtime.** `DirectionalAnimator` never touches `flipX`
  (`DirectionalAnimator.SpriteSetBuilder.BuildEightDirectionalSet` slices a linear list into eight
  *contiguous* per-direction buckets — feeding it one side-facing cycle silently yields one static
  frame per direction) — CLAUDE.md documents this as a hard constraint. Baking real mirrored PNGs
  via `Image.transpose(FLIP_LEFT_RIGHT)` was chosen over any runtime-flip alternative specifically
  because it is lower-risk: it touches zero engine code and reuses the exact mechanism
  `build_knight_frames.py` already shipped. `DirectionalAnimator` was deliberately left untouched.
- **A 2-directional source still has to fill all eight compass buckets.** The default
  `directionMap` (overridable per monster) is exactly what `knight_red` ships with today, pinned
  by `KnightRedSpriteIntegrityTests.SideByDirection`: south/north/northWest/west/southWest draw
  from the west pose, southEast/east/northEast draw from the mirrored east pose. A monster whose
  source art suits a different split (e.g. it has a genuine front/back pose) overrides
  `directionMap` in its config entry instead of taking the default.
- **Resample in premultiplied alpha** (`Image.convert("RGBa")`), same reason as the prop
  pipeline: straight RGBA blends the zeroed RGB of transparent pixels into every edge.
- **The manifest is self-describing — no bucket math on the C# side.** Python already flattens
  each cyclic state into the exact 8-direction × N-frame order `DirectionalAnimator` expects
  (S, SE, E, NE, N, NW, W, SW, each block in frame order); the importer just loads sprites by path
  and assigns them. A turnaround idle is emitted as 8 `{direction, path}` pairs consumed straight
  into `EntityAssetConfig.idle` (the `DirectionalSprites` struct); a cyclic state is emitted as a
  flat sprite-path list consumed straight into the matching `*Sheets` field
  (`walkSheets`/`chaseSheets`/`castSheets`/`attackSheets`/`damageSheets`/`deathSheets`, or
  `idleSheets` if a monster's idle is itself a cycle rather than a static turnaround).
- **Only the states a manifest actually names are written.** `EntityAnimationBinder` already
  falls each empty state back to a neighbour (walk → idle, chase → walk, cast → walk,
  attack → cast, damage/death → idle), so a monster authored with only idle + walk is playable
  immediately and gains new animations without disturbing anything a designer set by hand later.
- **Never overwrite what a designer owns.** `MonsterFramesImporter` never touches
  `stats`/`fsmSet`/`autoCastList`/`xpReward`/`lootTable` — the pixel pipeline owns sprites only,
  same line the prop pipeline draws for `solid`/`splitRatio`/light vs. a building's gameplay role.
- **Stage 3 is idempotent**, keyed on `monsterKey`: re-running after re-slicing updates the same
  `MonsterDefinition` asset in place (`Data/Catalogs/Monsters/<monsterKey>.asset`) rather than
  creating a duplicate, and registers it via `MonsterCatalog.UpsertDefinition` — which had zero
  callers before this importer. Deliberately **not** `Undo.RecordObject`: see the
  `BuildingPropImporter` incident CLAUDE.md documents (a bulk import lands dozens of assets on the
  global editor undo stack; the first stray Ctrl+Z or runtime-editor undo test reverts every one
  of them in memory to its empty creation state while the correct data sits on disk).
  `EditorUtility.SetDirty` alone is correct for data an operator re-runs rather than undoes.
- A dry run (`Valkur/Monsters/Import Frame Sheets (Dry Run)`) resolves every sprite path and
  reports missing ones WITHOUT writing anything, same as the create/update accounting — so a
  broken manifest is caught before anyone applies it for real.
- Every generated `monster_frames_manifest*.json` under `tools/atlas/generated/` is read together
  (one per wave, never clobbering an earlier one), exactly like `building_props_manifest*.json`.
- `Tests/EditMode/Game/Data/MonsterFramesImporterTests.cs` exercises the importer end to end
  against a fully sandboxed manifest directory, an in-memory `MonsterCatalog` and a scratch asset
  folder — nothing shipped is ever at risk from running the suite. It pins: a manifest creates a
  definition and registers it; a re-run refreshes the same asset instead of duplicating it;
  `UpsertDefinition` never appends a second catalog entry; sprite slots are wired correctly;
  and a dry run writes nothing while still reporting a broken sprite path.

## Player character sheets (retouch round trip)

`Art/Characters/<class>/<class>_{idle,walking,casting}.png` are single-row strips of 128x128
frames (40 or 41 of them, so 5120 or 5248 px wide) at PPU 64, pivot (0.5, 0). Retouching a
pose inside a 5000 px strip is unpleasant, so `tools/atlas/player_sheet_frames.py` round-trips
one:

```bash
python tools/atlas/player_sheet_frames.py extract  --class valkyrie --out unity/downloads/edit
python tools/atlas/player_sheet_frames.py restitch --dir unity/downloads/edit/valkyrie_idle
```

`extract` writes one PNG per frame plus a `sheet.json` recording the target path and the
original geometry; `restitch` rebuilds the strip from those frames and writes it back over the
source. A frame nobody edits comes back bit-identical (plain crop, no resample, no mode
change) — verified across all 5 classes x 3 states.

- **The geometry is load-bearing.** `PlayerCharacterAssetBinder` re-slices on a fixed 128 px
  grid and derives each sprite GUID from `<texturePath>#<spriteName>`, which is what lets the
  ~284 sprite references inside `Data/Catalogs/Players/<class>.asset` survive a retouch. Keep
  the file name, the frame size and the frame count and they all rebind; widen the strip and
  the trailing frames vanish from the catalog silently. `restitch` refuses any frame that is
  not 128x128, any missing frame, and always writes the recorded width.
- **Edit in RGBA.** Flattening onto a background and re-keying leaves black under the
  transparent pixels, which the premultiplied resample then rings around every edge.
- Finish with `Valkur/Setup/Rebuild Player Character Assets` to re-slice and rebind.
- Extraction output belongs under `unity/downloads/` (gitignored) — the strips in
  `Art/Characters/` stay the only versioned copy.

## Naming Convention

### Files

```
[category]_[entity]_[variant]_[state]_[direction]_[frame].png

Examples:
  char_warrior_idle_down_01.png
  tile_grass_01.png
  spell_fireball_projectile_01.png
  item_sword_iron.png
  ui_btn_primary.png
```

Hard rules for **every** asset under `Assets/_Project/`:

1. **Case:** `snake_case` (lowercase + underscores). Never PascalCase, kebab-case, spaces, or accented characters.
2. **Extension:** lowercase only — `.png` (not `.PNG`), `.ogg`, `.wav`, `.asset`. Mixed case breaks Linux/macOS imports.
3. **Language:** English. No `vaciar_*`, `pintar_*`, `imagen_*`. Translate at import time.
4. **No timestamps / placeholders in filenames:** never commit `ChatGPT Image *.png`, `screenshot_2025_*.png`, `untitled.png`, `*_copy.png`, `*_old.png`, `*_new.png`, `*_final.png`. Rename before committing.
5. **No spaces, parentheses, commas, or apostrophes** in any asset filename or folder name.

### Folders

| Layer | Convention | Example |
|---|---|---|
| Top-level under `_Project/` | `PascalCase` | `Art/`, `Audio/`, `Data/`, `Prefabs/`, `Resources/`, `Scenes/`, `Scripts/`, `Settings/`, `Shaders/`, `SpriteAtlases/` |
| Domain subfolders | `snake_case` (lowercase) | `art/items/alchemy/`, `audio/sfx/inventory/`, `art/buildings/houses/` |
| Vendor / asset-store packs | `_Project/<Layer>/Vendor/<PackName>/` | `_Project/Art/VFX/Vendor/SlashVFX/` |

**Vendor packs keep their original internal structure intact** (the `Demo/`, `Materials/`, `Prefabs/` PascalCase subfolders that ship with the pack) so they can be updated by re-importing from the Asset Store without merge conflicts. The `snake_case` rule applies to folders we author, not to third-party drops.
| Backups | NEVER inside `Assets/` — git is the backup | (deleted) |
| Tier-2 recovery | `_Project/Data/Backups/` — **whitelisted exception** | maintained by `BuildingsDataGuard` + `MapEditorDataGuard` |
| Catalog buckets | `_Project/Data/Catalogs/<Name>/` keeps PascalCase — **whitelisted exception** | loaded by string name via `Resources.Load("Catalogs/<Name>")`; renaming forces matching path edits in many call sites |
| Empty folders | Don't keep folders with only `.meta` and no children | (deleted) |

The two-tier rule (`PascalCase` for top-level, `snake_case` for everything below) keeps Unity's standard project layout (`Resources/`, `StreamingAssets/`, `Scenes/`) intact while giving every domain folder a single, predictable convention.

## Forbidden patterns

The lint script `tools/atlas/audit_asset_conventions.py` enforces all of these (the EditMode test `AssetConventionsTests` runs the same checks inside Unity so CI catches violations).

| Pattern | Why it's forbidden |
|---|---|
| `*.PNG`, `*.JPG`, `*.OGG` (uppercase ext) | Case-sensitive filesystems break |
| Files in `Assets/` root that aren't `_Project/`, `Tests/`, `Settings/`, `StreamingAssets/`, `TextMesh Pro/`, `Scenes/`, `Screenshots/`, `Resources/`, or Unity-required SO | Flat root is unmanageable |
| `Assets/_Project/Resources/*.png` (loose at root) | `Resources/` is loaded whole at build → bloat |
| Folders named `_backups`, `Backups`, `backup`, `OLD`, `*_old` under `Assets/` (whitelisted exceptions: `_Project/Data/Backups/` — tier-2 recovery maintained by `BuildingsDataGuard` / `MapEditorDataGuard` — and any `_Project/Scripts/**/Backups/` C# code namespace such as the Map editor's `MapBackupBrowserUI`) | Git is the backup |
| Filenames containing `ChatGPT`, ` ` (space), `(`, `)`, `,`, `'` | Tooling-hostile |
| Filenames ending in `_old.png`, `_copy.png`, `_new.png`, `_final.png`, `_v2.png` | Indicates uncommitted iteration; rename or delete |
| `InitTestScene*.unity` committed (test runner artifact) | Already in `.gitignore`; older committed copies must be `git rm`'d |

## Validation tools

Standalone Python utilities under `tools/atlas/` (Pillow + stdlib only):

| Script | Use |
|---|---|
| `tools/atlas/audit_tile_sizes.py` | Audit/fix tile dimensions; verifies 32×32 PPU=32 invariant |
| `tools/atlas/normalize_tiles.py` | Bulk normalize tiles to 32×32 RGBA |
| `tools/atlas/unity_asset_audit.py` | Per-file report → `tools/cache/atlas/unity_asset_audit.json` |
| `tools/atlas/generate_atlas_doc.py` | Render the audit JSON as `unity/Fase 2_v1_Atlas.md` |

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| Sprites blurry | FilterMode set to Bilinear | Set to Point (no filter) |
| Sprites wrong size | PPU mismatch | Verify PPU matches category table |
| Sprites offset | Wrong pivot | Set pivot per category rules |
| Pink sprites | Missing URP shader | Apply Sprite-Lit-Default material |
| Memory spike | No atlas packing | Build SpriteAtlas per domain |
| Tile overlap / "sand patch" | Oversized source PNG | Run `tools/atlas/audit_tile_sizes.py --fix` |

## Open work — Atlas Phase 2

- Formal naming convention finalized + applied across all domains
- 9 planned `SpriteAtlas` groups built and validated
- `asset_map.csv` finalized as the manifest of every art asset (id, target path, atlas group, owner system)
