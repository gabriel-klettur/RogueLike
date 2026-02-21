# Sprite Sheet Conventions for Player Classes

This document describes the layout of player sprite sheets used by the game.

## dwarf.png (1×40 Strip)
- **Dimensions**: 1 row × 40 columns (each frame = 128×128 px).
- The sheet is divided into **8 directional blocks**, each of 5 columns: 1 idle + 4 walking frames.
- **Order of directions (blocks)**:
  1. South (cols 0–4)
  2. South-East (cols 5–9)
  3. East (cols 10–14)
  4. North-East (cols 15–19)
  5. North (cols 20–24)
  6. North-West (cols 25–29)
  7. West (cols 30–34)
  8. South-West (cols 35–39)

Within each block:
- Offset 0: idle frame
- Offsets 1–4: walking frames

<img src="dwarf.png" alt="dwarf strip layout" width="800"/>

## barbarian, elven, mague, valkyrie (1×40 Strip)
- **Dimensions**: 1 row × 40 columns (each frame = 128×128 px).
- The sheet is divided into **8 directional blocks**, each of 5 columns: 1 idle + 4 walking frames.
- **Order of directions (blocks)**:
  1. South (cols 0–4)
  2. South-East (cols 5–9)
  3. East (cols 10–14)
  4. North-East (cols 15–19)
  5. North (cols 20–24)
  6. North-West (cols 25–29)
  7. West (cols 30–34)
  8. South-West (cols 35–39)

Within each block:
- Offset 0: idle frame
- Offsets 1–4: walking frames

<img src="strip_example.png" alt="example strip layout" width="800"/>

## Usage Guidelines
1. Map each player class to its sprite sheet path in **data/entities/players.json** under `PLAYER_ASSETS`.
2. Loader should inspect JSON: if value is a **string**, treat as 4×5 grid (dwarf); if **object**, treat as strip and load based on block indices.
3. Implement logic in `PlayerAssets` to slice frames according to these rules.

---

