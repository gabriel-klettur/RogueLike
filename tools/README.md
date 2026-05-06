# `tools/` — Standalone utilities for the Valkur Unity project

Small Python scripts that operate on the **Unity** project assets directly.
They have no dependency on the legacy `python/` roguelike (which has been
archived). Each script is self-contained and only needs Python + the
dependencies listed in its docstring.

## Layout

```
tools/
├── audio/
│   ├── analyze_music.py            # Extract BPM + musical key from .mp3 (librosa)
│   └── patch_audio_catalog_bpm.py  # Patch AudioCatalog.asset YAML with the analysis
├── atlas/
│   ├── audit_tile_sizes.py         # Audit + fix tile dimensions in Unity Resources/Tiles
│   ├── normalize_tiles.py          # Bulk normalize Unity tiles to 32×32 RGBA (Pillow)
│   ├── unity_asset_audit.py        # Scan Unity Art/ → cache/atlas/unity_asset_audit.json
│   └── generate_atlas_doc.py       # Generate Fase 2_v1_Atlas.md from the audit JSON
├── world/
│   └── generate_empty_overlays.py  # Bootstrap floor_2 overlays for zones missing one
└── cache/                          # Gitignored. Tool outputs land here.
```

## Running

All scripts run from the **repo root** with a virtualenv active. Each
prints a usage block when invoked with `--help`.

Example — refresh BPM/key for the whole music library:

```bash
pip install librosa numpy
python tools/audio/analyze_music.py
python tools/audio/patch_audio_catalog_bpm.py
```

The first call writes `tools/cache/audio/music_analysis.json`; the second
patches every `AudioCatalog.asset` in the Unity project in place.

## Why a separate folder

These tools used to live in `python/scripts/`, but the rest of `python/`
is the archived original Pygame implementation of Valkur (no longer the
source of truth — the Unity project is). When `python/` is deleted from
`main`, these utilities still need to exist somewhere; that's `tools/`.
