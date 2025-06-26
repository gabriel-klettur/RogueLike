# 🔄 Flujo de Eventos – RogueLike Top-down

Este documento explica el **flujo completo de eventos dentro del juego**, desde que el jugador presiona una tecla hasta que los efectos se ven reflejados en pantalla. Es una guía crítica para comprender **cómo se propagan las acciones** a través de los sistemas del juego.

---

## 🎮 1. Entrada del Usuario (Input Handling)

- **Módulo:** `core/logic/input_handler.py` (o directamente en `game.py`)
- **Descripción:**
  - Captura los eventos de teclado/mouse.
  - Interpreta teclas presionadas: movimiento, ataques, menú, etc.
  - Crea un diccionario de inputs activos (`input_state`), como:
    ```python
    input_state = {
        'move_up': True,
        'move_down': False,
        'use_spell': True,
        'pause': False
    }
    ```

---

## 🧠 2. Procesamiento del Input

- **Módulo:** `core/game/game_state.py`
- **Descripción:**
  - Usa `input_state` para modificar el estado del juego o del jugador.
  - Ejemplo: si `'move_up' == True`, se llama a `player.move(direction="up")`
  - Lógica condicional: cooldowns, validaciones, menú activo/inactivo.

---

## 🔁 3. Actualización de Estado (Update Phase)

- **Módulo:** `main.py` → `game.update()`
- **Incluye:**
  - Movimiento y rotación del jugador y NPCs.
  - Actualización de cámara.
  - Colisiones.
  - Timers (cooldowns, animaciones).
  - Sincronización con el servidor (modo online).

---

## 🖼️ 4. Renderizado en Pantalla (Draw Phase)

- **Módulo:** `main.py` → `game.render()`
- **Incluye:**
  - Fondo del mapa (`tilemap.render()`)
  - Obstáculos y entidades (`obstacle.draw()`, `player.draw()`...)
  - HUD inferior con iconos activos.
  - Barras flotantes sobre entidades.
  - Mini mapa.
  - Menú (si activo).

---

## ⌚ 5. Control del Tiempo y FPS

- **Módulo:** `main.py`
- **Línea clave:** `game.state.clock.tick(60)`
  - Establece el máximo de FPS.
  - Evita que el juego se acelere en hardware rápido.

---

## 📡 6. Red (opcional)

- **Módulo:** `network/websocket_client.py`
- Si está en modo multijugador:
  - Se envían datos del jugador (posición, inputs, stats).
  - Se reciben datos remotos (jugadores conectados).
  - `RemotePlayer.update()` replica sus acciones.

---

## 🧪 7. Debug o Consola (si está activa)

- Si `DEBUG = True`, algunos pasos extra:
  - Render de hitboxes.
  - Mostrar FPS, dirección, conexión, tile actual.
  - Mostrar inputs activos.

---

## 🧭 Diagrama Simplificado del Flujo

```text
Usuario presiona tecla ⟶ Input Capturado
                    ⟶ Interpreta input ⟶ Actualiza estado jugador/NPC
                                           ⟶ Actualiza cámara, colisiones, cooldowns
                                           ⟶ Sincroniza con servidor (si online)
                                           ⟶ Renderiza mapa, entidades, HUD, menú
                                           ⟶ Aplica FPS limit
```

Este ciclo se repite cada frame (idealmente 60 veces por segundo).

