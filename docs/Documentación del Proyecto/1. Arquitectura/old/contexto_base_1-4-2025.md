
## 🎮 **Contexto del Proyecto - Roguelike con vista Top-Down**

Estás desarrollando un **videojuego Roguelike 2D con vista Top-Down**, utilizando **Python + Pygame**, y estructurado de manera profesional, modular y escalable. El juego combina mecánicas tradicionales del género con funciones modernas como multijugador y generación procedural.

---

### 🧩 **Estructura del Proyecto**

| Carpeta                     | Propósito                                                                 |
|----------------------------|--------------------------------------------------------------------------|
| `core/`                    | Núcleo del motor del juego (eventos, renderizado, cámara, multiplayer).   |
| `entities/`                | Entidades del juego: jugador, obstáculos, jugadores remotos.              |
| `map/`                     | Generación procedural de mapas, lobby y sistema de carga.                 |
| `network/`                 | Cliente WebSocket para modo multijugador online.                         |
| `ui/`                      | Interfaces de usuario dentro del juego (menú, HUD).                      |
| `utils/`                   | Utilidades reutilizables, como el cargador de imágenes.                  |
| `assets/`                  | Recursos gráficos organizados por tipo (personajes, objetos, tiles, UI). |
| `main.py / launcher.py`    | Punto de entrada del juego y launcher para distribución.                  |

---

### 🕹️ **Características Principales del Juego**

#### ✅ Sistema de Juego
- **Vista Top-Down tradicional del género roguelike.**
- **Player modular** con stats, movimiento, renderer e interacciones.
- Colisiones con obstáculos (`Obstacle`) y **tiles sólidos (`#`)**.
- Movimiento fluido con teclado y detección de dirección.

#### 🗺️ Mapas
- **Mapa procedural** generado dinámicamente.
- Integración de un mapa estático tipo **lobby** mediante fusión de mapas (`merge_handmade_with_generated`).
- Cada tile tiene un sprite, colisión, y tipo (`.` o `#`).
- Sistema de minimapa dinámico que se actualiza en tiempo real.

#### 👤 Jugador
- Dos personajes seleccionables: `first_hero` y `valkyria`.
- Cada uno tiene **sprites por dirección** (8 direcciones) y **stats únicos**:
  - Vida, maná y energía.
- Interfaz HUD con icono de restauración y **sistema de cooldown (tecla Q)**.
- Barras flotantes escaladas dinámicamente según el zoom.

#### 🎮 Interfaz de Usuario (Menu)
- Accesible con `ESC`.
- Navegable con teclas (`↑ ↓ Enter`).
- Opciones:
  - Cambiar personaje
  - Cambiar entre **modo local / multijugador**
  - Salir del juego

#### 🔍 Cámara
- Enfocada en el jugador con seguimiento suave.
- Zoom ajustable con la rueda del mouse (`0.5x` a `2x`).
- Aplica transformaciones a sprites y rects.

#### 🌐 Multijugador Online (WebSocket)
- Implementación de cliente WebSocket (`WebSocketClient`).
- Cada jugador tiene un ID único (`UUID`).
- Se sincronizan en tiempo real: posición, dirección y stats.
- Renderizado de **jugadores remotos** con barras e identificación (`pid[:6]`).
- Alternancia entre modo local y online desde el menú.

---

### 🔄 **Ciclo del Juego**

El loop principal (`main.py`) sigue la estructura estándar de Pygame:

```python
while game.state.running:
    game.handle_events()    # 🎮 Captura entradas de usuario
    game.update()           # 📷 Actualiza cámara y lógica
    game.render()           # 🖼️ Dibuja el mundo, entidades, minimapa y HUD
    game.state.clock.tick(60)  # 🕒 Limita a 60 FPS
```

---

### 🧪 **Extras Técnicos**
- Debug visual de **hitboxes y colisiones** si `DEBUG = True` (`config.py`).
- Minimapa importado desde módulo separado y renderizado en pantalla.
- Sistema de carga de imágenes (`load_image`) con rutas relativas a partir de `__file__`.

---

### 📦 **Distribución & Requisitos**

#### `requirements.txt`
Incluye:
- `pygame`
- `tcod` (para futuras integraciones de FOV, pathfinding, etc.)
- `websocket-client`
- `websockets`
- `pyinstaller` (para crear ejecutables)

#### `.spec` files
- Estás preparando archivos `.spec` para distribuir el juego con **PyInstaller** (`main.spec`, `launcher.spec`).

---

### 📝 **Documentación**
Ubicada en `docs/`, incluye:
- Especificaciones del juego.
- Guías de tiles.
- Enlaces útiles.
- Prompts previos utilizados para desarrollo con LLMs.

---
