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
| Empty folders | Don't keep folders with only `.meta` and no children | (deleted) |

The two-tier rule (`PascalCase` for top-level, `snake_case` for everything below) keeps Unity's standard project layout (`Resources/`, `StreamingAssets/`, `Scenes/`) intact while giving every domain folder a single, predictable convention.

## Forbidden patterns

The lint script `tools/atlas/audit_asset_conventions.py` enforces all of these (the EditMode test `AssetConventionsTests` runs the same checks inside Unity so CI catches violations).

| Pattern | Why it's forbidden |
|---|---|
| `*.PNG`, `*.JPG`, `*.OGG` (uppercase ext) | Case-sensitive filesystems break |
| Files in `Assets/` root that aren't `_Project/`, `Tests/`, `Settings/`, `StreamingAssets/`, `TextMesh Pro/`, `Scenes/`, `Screenshots/`, `Resources/`, or Unity-required SO | Flat root is unmanageable |
| `Assets/_Project/Resources/*.png` (loose at root) | `Resources/` is loaded whole at build → bloat |
| Folders named `_backups`, `Backups`, `backup`, `OLD`, `*_old` under `Assets/` (the only exception is `_Project/Data/Backups/`, which is the project-side tier-2 recovery for `buildings_instances.json` + `map_editor_zones.json` and is maintained by `BuildingsDataGuard` / `MapEditorDataGuard`) | Git is the backup |
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
