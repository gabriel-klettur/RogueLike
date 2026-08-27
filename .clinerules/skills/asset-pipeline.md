
# Asset Pipeline — Valkur

Full canonical reference:

**[.github/skills/asset-pipeline/SKILL.md](../../.github/skills/asset-pipeline/SKILL.md)**

## Quick reference

### Import policies (enforced by `ValkurAssetPostprocessor.cs`)

| Category | Unity PPU | FilterMode | Compression | Pivot | Atlas group |
|---|---|---|---|---|---|
| Tiles | 32 | Point | None | Center | env-tiles |
| Characters | 16 | Point | None | Bottom-center | characters |
| NPC | 16 | Point | None | Bottom-center | npc |
| Projectiles | 16 | Point | None | Center | spells |
| Spell VFX | 16 | Point | None | Center | spells |
| Explosions | 16 | Point | None | Center | vfx |
| Items | 16 | Point | None | Center | items |
| Particles | 16 | Point | None | Center | vfx |
| UI | 100 | Bilinear | None | Center | ui |
| Buildings | 32 | Point | None | Bottom-center | buildings |
| Audio SFX | — | — | DecompressOnLoad/PCM | — | — |
| Audio Music | — | — | Streaming/Vorbis | — | — |

### Asset locations

```
Assets/_Project/Art/
├── Tiles/        Characters/   NPC/
├── Spells/       Items/        Buildings/
├── VFX/          UI/           Misc/

Assets/_Project/Audio/
├── Music/{Biomes,Zones,Bosses,Events,Stingers}/
└── SFX/
```

### Existing tools

- `ValkurAssetPostprocessor.cs` — auto-applies import rules on every reimport.
- `CharacterAtlasBuilder.cs` / `TileAtlasBuilder.cs` / `SpriteAtlasBuilder.cs` — build atlases.
- `TilePaletteBuilder.cs` — generate Unity TilePalette from imported tiles.
- `BulkReimportTool.cs` — force-reimport a folder applying current postprocessor rules.
- `tools/atlas/` (Python) — standalone audit/normalize utilities (Pillow + stdlib).

## Procedure

1. **Drop** the asset into the appropriate `Assets/_Project/Art/<category>/` folder. The postprocessor applies the right PPU/pivot/filter automatically.
2. **Verify** the result via Inspector or `unityMCP__manage_asset(action="search", page_size=25, generate_preview=false)`.
3. **Build atlases** by domain via the menu items (`Valkur > Atlas > Build *`).
4. **For audio**, use `Valkur > Audio > Music Scanner` (music) or just drop SFX into `Audio/SFX/` (postprocessor handles import).

## Constraints

- Never deviate from PPU/pivot/filter without an explicit reason — the postprocessor is the contract.
- Always Point filtering for pixel art (Bilinear only for UI).
- Always verify Unity MCP console clean after postprocessor or atlas-builder edits.
