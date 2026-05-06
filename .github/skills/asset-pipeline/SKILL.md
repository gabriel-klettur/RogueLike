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

Assets/_Project/Audio/
├── Music/
│   ├── Biomes/
│   ├── Zones/
│   ├── Bosses/
│   ├── Events/
│   └── Stingers/
└── SFX/
```

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

```
[category]_[entity]_[variant]_[state]_[direction]_[frame].png

Examples:
  char_warrior_idle_down_01.png
  tile_grass_01.png
  spell_fireball_projectile_01.png
  item_sword_iron.png
  ui_btn_primary.png
```

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
