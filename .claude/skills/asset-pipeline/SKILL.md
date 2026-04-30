---
name: asset-pipeline
description: Valkur asset migration pipeline — Python `python/assets/` → Unity `_Project/Art/`. Covers PPU/pivot/filter/compression policies, atlas grouping, asset_map.csv schema, ValkurAssetPostprocessor, CharacterAtlasBuilder, TileAtlasBuilder, audio import. Load when migrating, organizing, or validating assets.
---

# Asset Pipeline — Valkur

Full canonical reference:

**[.github/skills/asset-pipeline/SKILL.md](../../../.github/skills/asset-pipeline/SKILL.md)**

## Quick reference

### Import policies (enforced by `ValkurAssetPostprocessor.cs`)

| Category | Unity PPU | FilterMode | Compression | Pivot | Atlas group |
|---|---|---|---|---|---|
| Tiles | 16 | Point | None | Center | env-tiles |
| Characters | 16 | Point | None | Bottom-center | characters |
| NPC | 16 | Point | None | Bottom-center | npc |
| Projectiles | 16 | Point | None | Center | spells |
| Spell VFX | 16 | Point | None | Center | spells |
| Explosions | 16 | Point | None | Center | vfx |
| Items | 16 | Point | None | Center | items |
| Particles | 16 | Point | None | Center | vfx |
| UI | 100 | Bilinear | None | Center | ui |
| Audio SFX | — | — | DecompressOnLoad/PCM | — | — |
| Audio Music | — | — | Streaming/Vorbis | — | — |

Buildings subsystem uses **PPU = 32** (separate convention).

### asset_map.csv schema

```csv
asset_id,source_path_python,target_path_unity,asset_type,pixels_per_unit,pivot,filter_mode,compression,atlas_group,addressable_key,owner_system,migration_status
```

### Source → target

| Python | Unity |
|---|---|
| `python/assets/tiles/` | `Assets/_Project/Art/Tiles/` |
| `python/assets/characters/` | `Assets/_Project/Art/Characters/` |
| `python/assets/npc/` | `Assets/_Project/Art/NPC/` |
| `python/assets/projectiles/` + `spells/` + `explosions/` | `Assets/_Project/Art/Spells/` and `VFX/` |
| `python/assets/items/` | `Assets/_Project/Art/Items/` |
| `python/assets/objects/` | `Assets/_Project/Art/Buildings/` and `Misc/` |
| `python/assets/particles_sprites/` | `Assets/_Project/Art/VFX/` |
| `python/assets/ui/` | `Assets/_Project/Art/UI/` |
| `python/assets/audio/` | `Assets/_Project/Audio/` |

### Existing tools

- `ValkurAssetPostprocessor.cs` — auto-applies import rules.
- `CharacterAtlasBuilder.cs` / `TileAtlasBuilder.cs` — build atlases.
- `TilePaletteBuilder.cs` — generate Unity TilePalette from imported tiles.
- `asset_inventory_raw.txt` — raw listing of all Python assets.

## Procedure

1. **Inventory** source: count, dimensions, naming patterns.
2. **Map** to target paths (asset_map.csv row per asset).
3. **Confirm** postprocessor handles the category (or extend it).
4. **Copy** assets into Unity folder.
5. **Verify** imported PPU + pivot + filter via Inspector or `mcp_unity_manage_asset(action="search", page_size=25, generate_preview=false)`.
6. **Build atlases** by domain via menu items.
7. **Update** `asset_map.csv` and migration status.

## Constraints

- Never modify Python asset files.
- Never deviate from PPU/pivot/filter without documenting the reason in the asset map.
- Always Point filtering for pixel art (Bilinear only for UI).
- Always verify Unity MCP console clean after postprocessor or atlas-builder edits.
