# sand_rock — Análisis de tiles

> Carpeta: `Resources/Tiles/sand_rock/`
> Ruleset: `Resources/Tiles/sand_rock/ruleset.asset`

## Identificación

- **Tiles totales:** 31 (15 en raíz + 16 en `tileset7_slices/`).
- **Subcarpeta legacy detectada:** `tileset7_slices/` → marcar como `hiddenLegacy`.
- **Terreno primario propuesto:** `sand`
- **Terreno secundario propuesto:** `rock`
- **Prioridad:** 0

## Naming pattern

### Set primario (15 tiles, raíz)

`tileset7_r{R}_c{C}`. Solo 15 tiles — falta uno respecto al Blob16 completo (16). Esto es problemático: **el ruleset NO podrá considerarse completo** hasta que se complete el slot faltante (probablemente arrastrando uno desde `tileset7_slices/`).

### Set legacy (16 tiles, subcarpeta `tileset7_slices/`)

`tileset7_{X}_{Y}` con pixel offsets. 16 tiles — encaja con Blob16. Posiblemente este set ES el correcto y el "primario" rR_cC fue una selección incompleta.

## Lo que vi en las muestras

| Archivo leído | Lo que veo |
|---|---|
| `tileset7_r0_c0` | sand color tan con detalle gris (rock) |
| `tileset7_r2_c2` | sand puro con un toque de oscuridad — posible borde de rock |

Coincide con folder name: **tan sand** y **gris rock** visibles.

## Slots propuestos

Con solo 15 tiles en el set primario, hay un slot huérfano. Hipótesis: el slot faltante se compensaría con uno de `tileset7_slices/`.

## Recomendación

1. **Comparar visualmente los 15 tiles `r/c` con los 16 tiles del slices.** Probablemente uno de los 16 slices fue excluido al renombrar.
2. En el wizard, asignar los 15 tiles `r/c` y luego rescatar 1 tile faltante de `tileset7_slices/`.
3. Mover el resto de `tileset7_slices/` a `hiddenLegacy`.

## Dudas

- ¿Por qué solo 15 tiles en el set primario? Posiblemente un tile fue eliminado por error durante la curación.
- Sin haber comparado los 31 tiles uno a uno no sé cuál es el "huérfano".
