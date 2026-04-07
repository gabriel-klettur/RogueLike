---
name: asset-pipeline
description: "Manage asset migration pipeline from Python to Unity. Use when working with sprite import, atlas building, PPU configuration, pivot normalization, asset_map.csv, audio import. Covers ValkurAssetPostprocessor, CharacterAtlasBuilder, TileAtlasBuilder."
argument-hint: "Describe the asset category or pipeline task"
---

# Asset Pipeline Migration

## When to Use
- Importing sprites from Python to Unity
- Building sprite atlases
- Configuring import settings (PPU, pivot, filter)
- Populating or updating asset_map.csv
- Audio file import and configuration
- Validating asset import quality

## Asset Categories & Import Rules

| Category | Source PPU | Unity PPU | FilterMode | Compression | Pivot | Atlas Group |
|----------|-----------|-----------|------------|-------------|-------|-------------|
| Tiles | 128px → 64px rendered | 16 | Point | None | Center | env-tiles |
| Characters | 128px | 16 | Point | None | Bottom-center | characters |
| NPC | 128px | 16 | Point | None | Bottom-center | npc |
| Projectiles | varies | 16 | Point | None | Center | spells |
| Spell VFX | varies | 16 | Point | None | Center | spells |
| Explosions | varies | 16 | Point | None | Center | vfx |
| Items | varies | 16 | Point | None | Center | items |
| UI | varies | 100 | Bilinear | None | Center | ui |
| Particles | varies | 16 | Point | None | Center | vfx |
| Audio SFX | — | — | — | DecompressOnLoad | — | — |
| Audio Music | — | — | — | Streaming/Vorbis | — | — |

## Procedure: Full Asset Migration

### Step 1: Inventory (Phase 2, Step 13 ✅)
```
python/assets/
├── tiles/          → count, dimensions, naming pattern
├── characters/     → sprite sheets, directions, frame counts
├── npc/            → same as characters
├── projectiles/    → individual sprites
├── spells/         → VFX frames
├── explosions/     → frame sequences
├── items/          → icons
├── objects/        → environment props
├── particles_sprites/ → particle textures
├── ui/             → HUD elements
└── audio/          → WAV/OGG files
```

### Step 2: Create asset_map.csv (Phase 2, Step 14 ☐)
Create `unity/docs/Migration_python_to_unity/02_assets/asset_map.csv` with columns:
```csv
asset_id,source_path_python,target_path_unity,asset_type,pixels_per_unit,pivot,filter_mode,compression,atlas_group,addressable_key,owner_system,migration_status
```

### Step 3: Copy assets to Unity (Phase 2, Step 20-22 ☐)
Target structure:
```
Assets/_Project/Art/
├── Tiles/
├── Characters/
├── Spells/
├── Items/
├── Buildings/
├── NPC/
├── Misc/
├── UI/
└── VFX/
```

### Step 4: Verify postprocessor
`ValkurAssetPostprocessor.cs` should auto-apply:
- PPU by folder path
- FilterMode.Point for pixel art
- No mipmaps
- No compression
- Correct pivot

### Step 5: Build sprite atlases
Use existing editor tools:
- `CharacterAtlasBuilder.cs` for character sprites
- `TileAtlasBuilder.cs` for tile sprites
- `TilePaletteBuilder.cs` for Unity TilePalette

### Step 6: Validate
- Visual inspection: sprites not blurry, correct scale
- Atlas packing: no oversized atlases (target <2048x2048)
- Memory: profile texture memory usage
- References: all ScriptableObjects reference valid sprites

## Naming Convention

```
[category]_[entity]_[variant]_[state]_[direction]_[frame].png

Examples:
  char_warrior_idle_down_01.png
  tile_grass_01.png
  spell_fireball_projectile_01.png
  item_sword_iron.png
  ui_btn_primary.png
```

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| Sprites blurry | FilterMode set to Bilinear | Set to Point (no filter) |
| Sprites wrong size | PPU mismatch | Verify PPU matches category table |
| Sprites offset | Wrong pivot | Set pivot per category rules |
| Pink sprites | Missing URP shader | Apply Sprite-Lit-Default material |
| Memory spike | No atlas packing | Build SpriteAtlas per domain |
