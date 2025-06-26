# 🔁 Ciclo Principal del Juego (`main.py`) – RogueLike Top-down

Este documento explica el **ciclo principal del juego** implementado en `main.py`. Detalla cómo se inicia el entorno, se ejecuta el bucle de juego y se gestiona el cierre del programa.

---

## 🧩 Estructura General de `main.py`

```python
import pygame
from roguelike_project.core.game.logic.base import Game

def main():
    pygame.init()
    screen = pygame.display.set_mode((1200, 800))
    pygame.display.set_caption("Roguelike")

    game = Game(screen)

    while game.state.running:
        game.handle_events()
        game.update()
        game.render()
        game.state.clock.tick(60)

    pygame.quit()

if __name__ == "__main__":
    main()
```

---

## ⛓️ Paso a Paso del Ciclo de Juego

### 1. `pygame.init()`
- Inicializa todos los subsistemas de Pygame (video, sonido, input, etc.).

### 2. `screen = pygame.display.set_mode(...)`
- Crea la **ventana del juego**.
- El tamaño puede modificarse en `config.py`.

### 3. `game = Game(screen)`
- Se instancia el **núcleo del juego**, que contiene el estado, el jugador, el mapa, la red, etc.

---

## 🔄 Bucle Principal `while game.state.running:`

### 🔹 `game.handle_events()` → [Input Handler]
- Captura los eventos de teclado, mouse, joystick...
- Almacena el estado de las teclas presionadas.
- Ejemplo: `'W' presionado` → `input_state['move_up'] = True`

### 🔹 `game.update()` → [Lógica del Juego]
- Actualiza todos los sistemas lógicos:
  - Movimiento de jugador/NPCs
  - Colisiones físicas
  - Estado del HUD
  - Cooldowns y animaciones
  - Cámara
  - Sincronización de red (modo multijugador)

### 🔹 `game.render()` → [Renderizado Visual]
- Dibuja cada capa de juego:
  - Fondo y tiles
  - Entidades: jugador, NPCs, proyectiles
  - HUD inferior
  - Mini mapa
  - Menús (si están activos)

### 🔹 `game.state.clock.tick(60)` → [Control de FPS]
- Limita la ejecución del ciclo a **60 frames por segundo**.
- Mantiene el ritmo constante en distintas máquinas.

---

## 📉 Diagrama Resumido del Loop

```text
Inicialización
   ↓
 Instancia Game()
   ↓
While Game Running:
 ├─▶ handle_events()
 ├─▶ update()
 ├─▶ render()
 └─▶ clock.tick(60)
   ↓
 pygame.quit()
```

---

## 📝 Notas Técnicas

- Todo el juego ocurre dentro de `main()`.
- El objeto `game` es el orquestador principal.
- `main.py` puede empaquetarse con `PyInstaller`.
- Se recomienda mantener `main.py` limpio y delegar responsabilidades a `core/`.

---

Este archivo actúa como **punto de entrada** y es fundamental para comprender el flujo de ejecución del proyecto.

