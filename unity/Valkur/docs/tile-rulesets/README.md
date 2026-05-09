# Tileset Rulesets — Análisis de Fase 2

Análisis offline hecho por Claude para preparar el auto-tiling de Valkur.

## Qué hay aquí

8 archivos `.md`, uno por carpeta de tiles bajo `unity/Valkur/Assets/_Project/Resources/Tiles/`. Cada uno contiene:

- Inventario de archivos en la carpeta (primarios vs legacy).
- Naming pattern observado.
- Descripción visual de las muestras leídas.
- Hipótesis de slot mapping (cuando es razonable).
- Lista de dudas para resolver en el wizard de Fase 3.

## Estado de cada ruleset

| Carpeta | Confianza alta en | Pendiente para wizard |
|---|---|---|
| `ocean_grass` | 1 slot (Center = `tileset_test_62`) | 15 slots restantes + decisión sobre tiles "raros" |
| `grass_dirt` | hipótesis 5×4 layout | mapear 16 slots, marcar `tileset3_slices/` legacy |
| `grass_rock` | nombre del primario (`tileset4_*`) | mapear 16 slots, marcar `tileset5/6/slices/` legacy |
| `rock_water` | terreno (rock + water) | mapear 16 slots, decidir si los 32 son 2 sets |
| `sand_grass` | nombre del primario (`tileset1_*`) | mapear 16 slots, marcar `tileset2_*/slices/` legacy |
| `sand_ocean` | priority canónica (10) | mapear 16 slots de los 26 disponibles |
| `sand_ocean_2` | priority subordinada (−10) | decidir si se borra o se mantiene como variante |
| `sand_rock` | nombre del primario (`tileset7_*`) | resolver el slot huérfano (15 vs 16) |

## Limitaciones del análisis

- Las imágenes son 32×32 — la lectura visual a thumbnail tiene incertidumbre alta.
- Solo `ocean_grass` recibió lectura completa (los 17 tiles). Los demás folders solo tienen 2-3 muestras.
- **El wizard de Fase 3 es la herramienta correcta para mapear slots con precisión** — estos docs son contexto, no la verdad final.

## Cómo usar estos docs al corregir

1. Abre el wizard F8 cuando esté implementado (Fase 3).
2. Selecciona una carpeta.
3. Lee el `.md` correspondiente para ver qué interpretó Claude.
4. Corrige en el wizard arrastrando tiles a los slots.
5. Si Claude se equivocó completamente, comenta en el `.md` el por qué (eso ayuda a entrenar futuros pasos similares).
