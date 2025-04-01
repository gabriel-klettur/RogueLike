# 🧠 Gestión de Estados (`GameState`, `PlayerState`, etc.)

Este documento explica cómo se gestiona el estado del juego y sus entidades (jugador, cámara, enemigos, etc.) de manera modular y escalable dentro del proyecto **RogueLike Top-down**.

---

## 🎮 1. `Game` como controlador maestro

La clase `Game` (en `core/game/game.py`) es la responsable de mantener el estado general del juego:

| Atributo         | Propósito                                                              |
|------------------|-------------------------------------------------------------------------|
| `self.running`   | Controla si el loop principal está activo.                             |
| `self.clock`     | Controla los FPS mediante `tick(60)`                                    |
| `self.show_menu` | Alterna entre juego activo y menú interactivo                          |
| `self.menu`      | Instancia del menú (interfaz de pausa y opciones)                      |
| `self.camera`    | Instancia de `Camera`, que sigue al jugador y aplica zoom               |
| `self.player`    | Instancia del jugador controlado por el usuario                         |
| `self.obstacles` | Obstáculos del mapa que afectan colisiones                             |

Este objeto representa el **GameState global**, aunque actualmente no está abstraído en una clase llamada `GameState`. En el futuro podría separarse.

---

## 👤 2. Estado del Jugador (`Player`)

El jugador está representado por una instancia de `Player` (`entities/player/base.py`) y gestiona su estado interno de forma modular:

### Componentes:
- `PlayerStats`: gestiona salud, energía, maná, cooldowns, restauraciones.
- `PlayerMovement`: gestiona movimiento y colisiones.
- `PlayerRenderer`: se encarga de renderizar al jugador y su HUD.

### Estado del jugador:
```python
self.x, self.y                # Posición
self.character_name           # Personaje activo
self.direction                # Dirección visual actual
self.sprite                   # Sprite actual
self.stats.health, .mana...  # Recursos internos
```

El jugador puede cambiar de personaje mediante `change_character()` y su estado se reinicia con el nuevo asset pero se mantiene la posición.

---

## 👾 3. Estado de Jugadores Remotos (Modo Online)

El estado de cada jugador remoto se representa con la clase `RemotePlayer` (`entities/remote_player/base.py`).

Cada uno tiene:
- Posición `x, y`
- ID único `pid`
- Sprites por dirección
- Estadísticas (vida, maná, energía)

Estos estados se actualizan en tiempo real por WebSocket (cuando se implemente completamente el sistema online).

---

## 🎥 4. Estado de la Cámara

La cámara mantiene su propio estado en `core/camera.py`:

| Atributo     | Propósito                                            |
|--------------|-----------------------------------------------------|
| `offset_x`   | Cuánto desplazar el mundo horizontalmente          |
| `offset_y`   | Cuánto desplazar el mundo verticalmente            |
| `zoom`       | Escala visual aplicada a sprites y posiciones      |

Se actualiza cada frame con `camera.update(player)` para seguir al jugador.

---

## 🔄 5. Estado del Menú

El menú se alterna con la tecla `ESC`, usando el booleano `self.show_menu` en `Game`.
Cuando está activo:
- Se bloquean inputs de juego.
- Se muestra `self.menu.draw()`.
- Se pueden ejecutar acciones como cambiar de personaje o salir del juego.

---

## 🧩 6. Estado de Colisiones y Obstáculos

Aunque no se abstrae como un `CollisionState`, el estado de colisión se determina por:
- `self.collision_mask`: imagen binaria del mapa de colisiones.
- `self.obstacles`: lista de entidades con hitbox.

El `PlayerMovement` accede a estas referencias para validar movimiento.

---

## 🕒 7. Tiempo y FPS

- El reloj (`self.clock`) del `Game` controla los FPS.
- Se usa en `clock.tick(60)` para limitar el frame rate.
- `clock.get_fps()` permite mostrar FPS en pantalla.

---

## 🔜 Futuras abstracciones sugeridas

- `GameState`: encapsular todos los estados en un objeto dedicado.
- `UIState`: manejar estados de menús, HUD, opciones, etc.
- `MultiplayerState`: manejar sincronización, conexiones, latencias.

