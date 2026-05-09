# sand_ocean — Análisis de tiles

> Carpeta: `Resources/Tiles/sand_ocean/`
> Ruleset: `Resources/Tiles/sand_ocean/ruleset.asset`
> **Variante canónica de la transición sand↔ocean** (la otra es `sand_ocean_2`, marcada con priority menor).

## Identificación

- **Tiles totales:** 26 (todos en raíz, sin subcarpeta `_slices`).
- **Terreno primario propuesto:** `sand`
- **Terreno secundario propuesto:** `ocean`
- **Prioridad:** **10** (canónica — gana sobre `sand_ocean_2`).

## Naming pattern

`tileset_test_<N>` con números no secuenciales: `10`, `26-30`, `46+`, `109-110`, etc. Mismo problema que `ocean_grass`: el numbering parece codificar `<row><col>` con gaps.

## Lo que vi en las muestras

| Archivo leído | Lo que veo |
|---|---|
| `tileset_test_10` | tile mayormente azul-marino oscuro con ondulaciones (océano profundo). |
| `tileset_test_28` | sand a la derecha, océano azul a la izquierda — clara transición horizontal. |

Coincide con folder name: hay **tan/beige sand** y **azul-marino ocean**.

## Slots propuestos

Sin layout estándar conocido para este naming, no puedo proponer slots por posición. Los 26 tiles habría que verlos uno a uno en el wizard para mapear visualmente.

## Recomendación

1. Cargar la carpeta en el wizard.
2. Identificar visualmente:
   - El tile de **sand puro** → Center (el primario es sand).
   - El tile de **ocean puro** → ese sería el "infill" del océano (no es slot del ruleset sand→ocean en sí; pertenecería a un base ruleset "ocean").
3. Buscar bordes y esquinas comparando con el diagrama del wizard.

## Dudas

- 26 tiles es más que 16. Algunos podrían ser variantes decorativas, otros podrían ser tiles del modelo Blob47 (esquinas internas) que no se usan en v1 Blob16.
- El folder name es `sand_ocean` pero la prioridad arriba es la "canónica": confirma que esta es la versión a usar antes de que `sand_ocean_2` quede como respaldo.
