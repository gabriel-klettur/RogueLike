# ocean_grass — Análisis de tiles

> Carpeta: `Resources/Tiles/ocean_grass/`
> Ruleset: `Resources/Tiles/ocean_grass/ruleset.asset`
> Análisis hecho por Claude leyendo los 17 PNG visualmente. Imágenes de 32×32 — interpretación visual con incertidumbre.

## Identificación

- **Tiles totales:** 17 (todos en raíz de la carpeta, sin subcarpeta `_slices`).
- **Terreno primario propuesto:** `grass`
- **Terreno secundario propuesto:** `ocean`
- **Prioridad:** 0
- **Modelo recomendado:** Blob16 (4-bit cardinal)

## Naming pattern

`tileset_test_<N>` donde `<N>` no es secuencial. Las posiciones existentes son:

```
N ∈ {1, 2, 3, 4, 5,        ← grupo A
     21, 22, 23, 24, 25,   ← grupo B
     41, 42, 43, 44, 45,   ← grupo C
     61, 62}               ← grupo D
```

El patrón sugiere `<row><col>` en un grid de 4 filas × 5 columnas (con filas 1, 3, 5 vacías). Esto **no encaja** con el layout estándar Blob16 (que es 4×4). Posiblemente el tileset original tenía 20 tiles (4×5) con 3 sin pintar, y el grupo D (61, 62) es overflow.

## Lo que vi en cada archivo

| Archivo | Lo que veo (descripción literal) | Slot probable |
|---|---|---|
| `tileset_test_1`  | grass arriba-derecha, mancha oscura/marrón abajo-izquierda. Posible esquina/transición. | borde NW o ConnectES |
| `tileset_test_2`  | tile mayormente oscuro con vetas rojas/marrones, textura vertical (¿pared rocosa? ¿agua oscura?). Difícil de clasificar. | revisar — podría ser legacy |
| `tileset_test_3`  | grass uniforme con manchitas, posible textura de hierba pura. | candidato a Center |
| `tileset_test_4`  | grass con franja oscura vertical en el lado izquierdo. | borde W (ConnectNES) |
| `tileset_test_5`  | grass con franja oscura vertical en el lado izquierdo, similar a 4. | borde W variante o ConnectNES |
| `tileset_test_21` | grass arriba, agua oscura azul abajo. **Transición horizontal grass-arriba/water-abajo**. | borde S (ConnectNEW) |
| `tileset_test_22` | tile completamente oscuro azul-marino con ondulaciones (agua/océano puro). | "agua pura" — interior del océano (NO es slot de grass) |
| `tileset_test_23` | grass arriba, agua abajo (similar a 21 pero variante). | borde S variante |
| `tileset_test_24` | agua arriba-izquierda, oscuro abajo (¿cliff? ¿borde de roca?). Confuso. | revisar |
| `tileset_test_25` | oscuro a la izquierda (¿pared/cliff?). | revisar |
| `tileset_test_41` | grass arriba-izquierda, agua abajo-derecha (esquina diagonal). | esquina NW de grass (ConnectES) |
| `tileset_test_42` | grass con motas de agua arriba-derecha y abajo. | borde compuesto (¿ConnectW?) |
| `tileset_test_43` | grass con borde tenue. | borde ligero — revisar |
| `tileset_test_44` | oscuro tipo cliff/pared con vegetación abajo. | revisar — podría ser una "pared" de cliff vista de costado, no parte de Blob16 |
| `tileset_test_45` | similar a 44, oscuro con motas. | revisar |
| `tileset_test_61` | tile completamente oscuro azul-marino (idéntico o muy similar a 22). | duplicado del agua pura — candidato a `hiddenLegacy` |
| `tileset_test_62` | grass uniforme y limpio, claramente un tile de grass puro sin transición. | **Center** (alta confianza) |

## Slots propuestos (alta confianza)

| Slot Blob16 | Tile sugerido | Confianza |
|---|---|---|
| `Center` | `tileset_test_62` | Alta — tile de grass puro sin bordes |
| (no es slot) `agua interna` | `tileset_test_22` o `tileset_test_61` | Estos son el FILL del océano, no parte del ruleset grass→ocean. Para pintar océano puro habría que crear una base ruleset "ocean". |

## Slots por revisar en el wizard (todos los demás 15)

`Isolated`, `ConnectN`, `ConnectE`, `ConnectNE`, `ConnectS`, `ConnectNS`, `ConnectES`, `ConnectNES`, `ConnectW`, `ConnectNW`, `ConnectEW`, `ConnectNEW`, `ConnectSW`, `ConnectNSW`, `ConnectESW`.

## Dudas y notas

- **No estoy seguro** si `tileset_test_2`, `24`, `25`, `44`, `45` son tiles válidos para Blob16 o son extras tipo "cliff face" / "pared" para otro propósito.
- `tileset_test_22` y `tileset_test_61` parecen idénticos (agua pura) — uno podría ir a `hiddenLegacy`.
- El folder NO tiene un set legacy obvio (no hay subcarpeta `_slices`). Todos los 17 son del mismo nivel.
- Faltarían tiles para llenar los 16 slots Blob16 si los 5 "raros" (2, 24, 25, 44, 45) no son aprovechables.

## Recomendación

Abrir el wizard F8 cuando esté listo (Fase 3) y arrastrar `tileset_test_62` al slot Center (ya pre-confirmado). Para los otros slots, comparar visualmente con el diagrama de bordes del wizard.
