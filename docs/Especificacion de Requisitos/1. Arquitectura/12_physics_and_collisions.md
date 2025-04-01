# 🧱 12. Physics and Collisions – Manejo de Colisiones en el Juego

Este documento detalla cómo se gestionan las colisiones físicas y lógicas dentro del juego Roguelike Top-Down.

---

## 🎯 Objetivo

- Detectar interacciones físicas entre entidades y el entorno.
- Definir zonas sólidas y transitables en el mapa.
- Permitir colisiones entre jugador, obstáculos y otros NPCs.
- Asegurar consistencia física y evitar superposición de sprites.

---

## 🧩 Tipos de Colisión

### 1. Colisiones Estáticas (mapa)

- Se basan en los caracteres del mapa procedural:

| Carácter | Significado          | Transitable | Ejemplo visual     |
|----------|----------------------|-------------|---------------------|
| `#`      | Muro / pared          | ❌ No        | Piedra, muralla     |
| `.`      | Piso transitable      | ✅ Sí        | Suelo, camino       |

- Cada tile se traduce a una celda de **128x128 px** en el mundo real.
- Se crea una **máscara de colisión** (2D array booleana o superficie) para representar estas zonas.
- Los personajes consultan esta máscara antes de moverse.

> 📝 El mapa se genera con `generate_dungeon_map()` y se puede fusionar con zonas hechas a mano (`merge_handmade_with_generated()`), pero esto se explica en el documento `22_tilemap_system.md`.

---

### 2. Colisiones Dinámicas (entre entidades)

- Involucran `pygame.Rect` o máscaras por píxel:
  - Jugador vs obstáculos.
  - Jugador vs enemigos.
  - Jugador vs objetos recogibles.
  - Proyectiles vs NPCs o jugador.

- Se usa el método `colliderect()` para detectar intersecciones.
- En el futuro se puede migrar a detección más precisa por **pixel-perfect masks** si es necesario.

---

## 📐 Ejemplo de flujo de colisión

```python
if not collision_mask[player.y // 128][player.x // 128]:
    player.move(dx, dy)
```

O usando un sistema de `Rect`:

```python
for obstacle in obstacles:
    if player.rect.colliderect(obstacle.rect):
        # Cancelar o ajustar movimiento
```

---

## 🧪 Debug Visual

- Si `DEBUG = True`, se renderizan:
  - Hitboxes de colisión.
  - Colores para áreas sólidas/transitables.

---

## 🔮 Consideraciones Futuras

- Colisiones redondeadas o por píxeles en proyectiles.
- Más tipos de superficie (`lava`, `agua`, `puertas`, etc.).
- Zonas con física especial (resbaladiza, ralentización).
- Integración con sistema de físicas avanzadas opcional (p. ej. pymunk).

---
