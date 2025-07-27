# 🗺️ 23. Minimap System – Generación, Renderizado y Sincronización

Este documento describe el sistema de minimapa del juego, tal como está implementado actualmente, junto con consideraciones para su expansión futura.

---

## 🎯 Objetivo

Proporcionar al jugador una representación visual compacta del entorno circundante, actualizada en tiempo real y útil para navegación en mapas grandes.

---

## ✅ Implementación Actual

### 📄 Archivo principal:
```text
core/game/render/minimap.py
```

### 🧠 Lógica Básica

- El minimapa se representa como una `pygame.Surface`.
- Cada tile es reducido a una escala pequeña (`tile_size = 4`).
- El jugador se posiciona en el centro del minimapa.
- Solo se dibujan los tiles visibles en la zona adyacente al jugador.
- El minimapa tiene opacidad parcial (`set_alpha(180)`).

### 🎨 Esquema de colores actual

| Tipo lógico | Color en minimapa  |
|-------------|--------------------|
| `.` Suelo   | Gris oscuro (60, 60, 60) |
| `#` Muro    | Rojo oscuro (150, 50, 50) |
| Jugador     | Verde brillante (0, 255, 0) |

### 🧾 Código clave
```python
tile_size = 4
center_x = minimap_width // 2
center_y = minimap_height // 2

for tile in state.tiles:
    dx = (tile_x - player_tile_x) * tile_size
    dy = (tile_y - player_tile_y) * tile_size
    draw_x = center_x + dx
    draw_y = center_y + dy

    pygame.draw.rect(minimap_surface, color, (draw_x, draw_y, tile_size, tile_size))
```

---

## 🧪 Sincronización

- El minimapa se renderiza **cada frame**.
- Se basa en `state.tiles`, que debe contener objetos con:
  - `x`, `y`
  - `tile_type` (ej. `"."` o `"#"`)

---

## 📍 Posición en pantalla

- Se dibuja en la **esquina superior derecha**.
- Margen derecho: `20px`
- Margen superior: `20px`
- Tamaño fijo: `200x150 px`

---

## 📦 Dependencias

- `map_loader.TILE_SIZE` es usado para normalizar coordenadas.
- Requiere `state.screen`, `state.player`, y `state.tiles`.

---

## 🧭 Consideraciones futuras (planeado)

| Mejora                           | Descripción |
|----------------------------------|-------------|
| 🎨 Mapeo completo de colores     | Añadir más tipos (`~`, `^`, `+`) con colores propios. |
| 🧠 Fog of War                     | Mostrar solo zonas exploradas. |
| 📍 Marcadores interactivos       | Pines, rutas, objetivos. |
| 🌐 Compatibilidad multijugador   | Mostrar jugadores remotos en distintos colores. |
| 🔍 Zoom dinámico                 | Ajustar visibilidad por input del jugador. |
| 📥 Carga desde mapa extendido    | Usar capa visual para reflejar detalles ricos. |

---