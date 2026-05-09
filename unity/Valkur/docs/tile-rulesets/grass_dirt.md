# grass_dirt — Análisis de tiles

> Carpeta: `Resources/Tiles/grass_dirt/`
> Ruleset: `Resources/Tiles/grass_dirt/ruleset.asset`
> Análisis hecho por Claude. Lectura visual completa de 3 muestras + inventario por naming pattern.

## Identificación

- **Tiles totales:** 36 (16 en raíz + 20 en `tileset3_slices/`).
- **Terreno primario propuesto:** `grass`
- **Terreno secundario propuesto:** `dirt`
- **Prioridad:** 0
- **Modelo recomendado:** Blob16

## Naming pattern

### Set primario (16 tiles, raíz de la carpeta)

`tileset3_r{R}_c{C}` con `R ∈ {0,1,2,3}`, `C ∈ {0,1,2,3,4}`. Distribución observada:

```
r0: c0, c1, c2, c3, c4   (5 tiles)
r1: c0, c1, c2, c3, c4   (5 tiles)
r2: c0, c1, c2, c3, c4   (5 tiles)
r3: c1                    (1 tile)
```

Total 16 tiles en grid 5 cols × 4 filas (con celdas vacías en r3 c0/c2/c3/c4). Esto sugiere un layout no-estándar — el artista seleccionó 16 de los 20 tiles del tileset original.

### Set legacy (20 tiles, subcarpeta `tileset3_slices/`)

`tileset3_{X}_{Y}` con `X ∈ {0, 32, 64, 96, 128}` (5 cols), `Y ∈ {0, 32, 64, 96}` (4 filas). Son los SLICES PIXELARES del tilesheet original. **Recomendación: marcar todos como `hiddenLegacy`.**

## Lo que vi en las muestras

| Archivo leído | Lo que veo |
|---|---|
| `tileset3_r0_c0` | grass uniforme con textura suave, posible esquina superior-izquierda |
| `tileset3_r0_c4` | grass con borde marrón a la derecha — claramente un borde E |
| `tileset3_r2_c2` | grass uniforme con tinte marrón en una esquina |

Los tiles muestran claramente las dos texturas: **verde grass** vs **marrón dirt**.

## Slots propuestos

Basándome en la convención típica de tilesheets 4×4 (la columna 0 suele ser bordes oeste, la última fila bordes sur, etc), una **hipótesis para r0–r2 × c0–c4**:

```
        c0          c1          c2          c3          c4
r0:  ConnectES   ConnectS    ConnectS    (alt)       ConnectSW
     (NW corner) (N edge)    (N edge)                (NE corner)

r1:  ConnectE    Center      Center      (alt)       ConnectW
     (W edge)    (full)      (full)                  (E edge)

r2:  ConnectNE   ConnectN    ConnectN    (alt)       ConnectNW
     (SW corner) (S edge)    (S edge)                (SE corner)

r3:  ?           Isolated?   ?           ?           ?
```

⚠️ Esto es **una hipótesis basada en convenciones**, NO en lectura tile-por-tile. Hay que verificar todo en el wizard.

## Slots con alta confianza para asignar

Sin haber leído los 16 visualmente, no puedo dar ningún slot con alta confianza. La hipótesis arriba es un punto de partida.

## Dudas y notas

- Los 20 tiles en `tileset3_slices/` están en orden pixelar (5×4) — si el artista los slicó originalmente y luego renombró 16 de ellos a `r/c` formato, podemos correlacionar:
  - `tileset3_0_0` ↔ `tileset3_r0_c0` (ambos son el corner top-left)
  - `tileset3_32_0` ↔ `tileset3_r0_c1`
  - etc.
- Las celdas faltantes en r3 (c0, c2, c3, c4) sugieren que la fila 3 del tilesheet tenía solo 1 tile usado (en c1).

## Recomendación

1. En el wizard, marcar los 20 tiles de `tileset3_slices/` como legacy de un click.
2. Comparar el orden esperado del Blob16 con los 16 tiles del set primario. La hipótesis r/c de arriba es punto de partida.
