# rock_water — Análisis de tiles

> Carpeta: `Resources/Tiles/rock_water/`
> Ruleset: `Resources/Tiles/rock_water/ruleset.asset`

## Identificación

- **Tiles totales:** 32 (todos en raíz, sin subcarpeta `_slices`).
- **Terreno primario propuesto:** `rock`
- **Terreno secundario propuesto:** `water`
- **Prioridad:** 0

## Naming pattern

`tileset8_r{R}_c{C}` con un layout grid. Total 32 tiles sugiere 2 tilesets de 16 cada uno (o uno de 8×4).

## Lo que vi en las muestras

| Archivo leído | Lo que veo |
|---|---|
| `tileset8_r0_c0` | tile gris-rocoso con elementos azules — borde del agua sobre roca |
| `tileset8_r2_c2` | tile mayormente gris-roca con detalles azules de agua |

Hay clara presencia de **gris/marrón rock** y **azul water**. Coincide con el folder name.

## Slots propuestos

32 tiles encajaría con 2× variantes del Blob16. Sin haberlos leído todos, no puedo proponer mapeos específicos. La estructura `r{R}_c{C}` permite suponer un grid; en el wizard se podrá ver el orden completo.

## Recomendación

1. En el wizard, listar todos los 32 tiles agrupados por `r/c`.
2. Identificar visualmente cuál es el "Center" (tile de roca pura sin agua) y cuál es la transición.
3. Si hay 2 sets de 16, mantener uno como principal y el otro como variantes adicionales (no legacy — son alternativas estéticas si se ven distintos).

## Dudas

- No tengo evidencia de qué `r/c` corresponde a cada slot.
- No verifiqué si los 32 son 2 sets distintos o 1 set extendido.
