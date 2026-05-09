# sand_grass — Análisis de tiles

> Carpeta: `Resources/Tiles/sand_grass/`
> Ruleset: `Resources/Tiles/sand_grass/ruleset.asset`

## Identificación

- **Tiles totales:** 64 (alto número — múltiples tilesets).
- **Subcarpeta legacy detectada:** `tileset1_slices/` → marcar como `hiddenLegacy`.
- **Terreno primario propuesto:** `sand`
- **Terreno secundario propuesto:** `grass`
- **Prioridad:** 0

## Naming pattern

### Set primario

`tileset1_r{R}_c{C}` (set principal) y posiblemente `tileset2_*` (set secundario). El usuario indicó: "una sola variante". Asumo `tileset1_*` como principal y `tileset2_*` legacy.

### Set legacy

`tileset1_slices/` con naming `tileset1_{X}_{Y}` (pixel offsets).

## Lo que vi en las muestras

| Archivo leído | Lo que veo |
|---|---|
| `tileset1_r0_c0` | sand color tan/beige con detalle de grass en una esquina |
| `tileset1_r2_c2` | grass verde y sand tan claramente diferenciados — transición pronunciada |

Los tiles tienen **sand tan/beige** y **grass verde** muy claros y distinguibles.

## Slots propuestos

Misma hipótesis de layout 5×4 (igual que `grass_dirt` y `grass_rock`). 16 slots Blob16 dentro de `tileset1_r/c`.

## Recomendación

1. Marcar todo `tileset1_slices/` como legacy.
2. Marcar todo `tileset2_*` como legacy salvo que el wizard muestre que son una variante deseada.
3. En el wizard, asignar los 16 slots usando los `tileset1_r{R}_c{C}` siguiendo la convención visual.

## Dudas

- Sin haber leído los 16 tiles del set principal, no puedo proponer slots específicos.
- No verifiqué si `tileset2` es visualmente distinto o duplicado.
