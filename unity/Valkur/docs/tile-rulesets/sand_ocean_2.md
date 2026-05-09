# sand_ocean_2 — Análisis de tiles

> Carpeta: `Resources/Tiles/sand_ocean_2/`
> Ruleset: `Resources/Tiles/sand_ocean_2/ruleset.asset`
> **Variante alternativa (legacy o estética distinta)** de la transición sand↔ocean. La canónica es `sand_ocean`.

## Identificación

- **Tiles totales:** 16 (todos en raíz, sin subcarpeta `_slices`).
- **Terreno primario propuesto:** `sand`
- **Terreno secundario propuesto:** `ocean`
- **Prioridad:** **−10** (subordinada a `sand_ocean` que tiene priority 10).

## Naming pattern

`tileset_test_<N>` con números: `81-85`, `101-105`, `121-125`, `142`. 16 tiles total.

El numbering 81/101/121 parece otro `<row><col>` codificado: filas 8, 10, 12, 14 (todas pares), 5 columnas por fila (1-5). Es decir 4 filas × 4 cols + 1 extra (142) = 17, pero solo hay 16 tiles, así que una posición está vacía.

## Lo que vi en las muestras

| Archivo leído | Lo que veo |
|---|---|
| `tileset_test_101` | sand a la derecha, ocean azul a la izquierda — borde vertical |
| `tileset_test_142` | sand puro con motas pequeñas, sin ocean visible — candidato a Center |

Coincide con folder name: **tan sand** y **azul ocean** distinguibles.

## Slots propuestos

Con 16 tiles exactos podría encajar perfectamente en un Blob16 (1 tile por slot) si el orden numérico se corresponde con el orden de slots. Pero sin haberlos leído todos no lo confirmo.

| Slot Blob16 | Tile sugerido | Confianza |
|---|---|---|
| `Center` | `tileset_test_142` | Media — vi sand puro sin ocean |

## Recomendación

1. Por su prioridad −10, este ruleset NO se usará por defecto cuando `sand_ocean` esté disponible.
2. Si se prefiere su estilo, **subir su priority** a 10 (y bajar `sand_ocean` a 0).
3. En el wizard, mapear los 16 slots viendo cada tile.

## Dudas

- ¿Es una variante estilística válida o es legacy puro?
- El usuario dijo "una sola variante (las otras son legacy)". Esto sugiere que `sand_ocean_2` debería marcarse como legacy completo. **A confirmar:** ¿borrar la carpeta o mantener con priority bajo?
