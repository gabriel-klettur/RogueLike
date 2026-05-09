# grass_rock — Análisis de tiles

> Carpeta: `Resources/Tiles/grass_rock/`
> Ruleset: `Resources/Tiles/grass_rock/ruleset.asset`

## Identificación

- **Tiles totales:** 98 (la cifra más alta de las 8 carpetas — sospechoso).
- **Subcarpeta legacy detectada:** `tileset4_slices/` → todos sus PNG se proponen como `hiddenLegacy`.
- **Terreno primario propuesto:** `grass`
- **Terreno secundario propuesto:** `rock`
- **Prioridad:** 0

## Naming pattern

### Set primario (raíz de la carpeta)

`tileset4_r{R}_c{C}` para los principales. Pero también vi otros prefijos `tileset5_`, `tileset6_` mencionados en la auditoría inicial — esto significa que la carpeta `grass_rock` agrupa **3 tilesets distintos**:

- `tileset4_*` (set principal)
- `tileset5_*`
- `tileset6_*`

El usuario indicó: "Una sola variante (las otras son legacy)". Se asume `tileset4_*` como primario; `tileset5_*` y `tileset6_*` deberían ir a `hiddenLegacy`.

### Set legacy explícito

`tileset4_slices/` con naming `tileset4_{X}_{Y}` (pixel offsets). Marcar todos como legacy.

## Lo que vi en las muestras

| Archivo leído | Lo que veo |
|---|---|
| `tileset4_r0_c0` | grass verde uniforme con un detalle gris/rocoso en una esquina |
| `tileset4_r2_c2` | grass verde uniforme con piedras grises pequeñas |

Los tiles muestran transición clara entre **verde grass** y **gris rock**.

## Slots propuestos

Aplica la misma hipótesis de layout 5×4 que `grass_dirt` (mismo formato `r/c`). Pero con el doble de tilesets duplicados, el riesgo de confusión es alto.

## Recomendación

1. En el wizard, **mostrar primero solo los `tileset4_r{R}_c{C}` del set principal** (16 tiles).
2. Marcar TODO el contenido de `tileset4_slices/` como legacy.
3. Marcar TODOS los `tileset5_*` y `tileset6_*` como legacy hasta que se confirmen como una variante usable.
4. Después de tener los 16 slots de `tileset4_r/c` mapeados, podemos rescatar variantes de `tileset5/6` como variantes adicionales del Center (decoración de "rocas más grandes", etc).

## Dudas

- No leí muestras de tileset5/6, así que no sé si son visualmente distintos del 4 o duplicados exactos. **Confirmar en el wizard.**
- El total de 98 tiles es ~6× más que las 16 variantes Blob16. Es esperable que la mayoría termine como legacy.
