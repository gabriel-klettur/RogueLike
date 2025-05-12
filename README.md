# RogueLike

**Roguelike** en vista top-down desarrollado en Python y Pygame. Proporciona un motor modular (`roguelike_engine`) y la lógica de juego (`roguelike_game`) con generación procedural de mazmorras, sistema de cámara, entrada, efectos de partículas y más.

---

## 📁 Estructura del Proyecto

```text
RogueLike/
├── launcher.py               # Script de arranque (entry-point)
├── setup.py                  # Configuración del paquete (editable)
├── requirements.txt          # Dependencias externas
└── src/
    ├── roguelike_engine/     # Módulo del motor (cámara, utilidades, input, etc.)
    └── roguelike_game/       # Lógica de juego (entidades, mapas, main loop)
```

Dentro de `src/`: los paquetes Python instalables con imports absolutos:

```python
from roguelike_engine.camera.camera import Camera
from roguelike_game.game.game import Game
```

---

## 🚀 Instalación

1. Clona el repositorio:

   ```bash
   git clone <URL_REPO>
   cd RogueLike
   ```
2. Instala tu paquete en modo editable:

   ```bash
   pip install -e .
   ```
3. Instala dependencias externas:

   ```bash
   pip install -r requirements.txt
   ```

---

## ▶️ Uso

* **Desde desarrollo**:

  ```bash
  python launcher.py
  ```
* **Vía entry-point** (tras instalación):

  ```bash
  roguelike
  ```

---

## 🛠️ Desarrollo

* Layout `src/` con imports absolutos para mantener el código limpio y evitar hacks de `sys.path`.
* Un `setup.py` define el paquete y el entry-point `roguelike`.
* Evitar imports relativos excesivos: usar relativos solo dentro de un mismo subpaquete.
* Uso de `benchmark` para perfilar `handle_events`, `update` y `render` en modo DEBUG.

---

## 🏗️ Empaquetado con PyInstaller

Ejemplo de spec (`roguelike.spec`):

```python
# Incluir datos estáticos:
# datas=[('assets/**/*','assets'),('data/**/*','data')]
a = Analysis(
    ['src/roguelike_game/main.py'],
    pathex=['src'],
    datas=[
        ('assets/**/*', 'assets'),
        ('data/**/*',   'data'),
    ],
    ...
)
```

Luego:

```bash
pyinstaller roguelike.spec --onefile
```

---

## 📦 Dependencias

Listado en `requirements.txt`:

```text
pygame
tcod
pyyaml
miniupnpc>=2.2
websocket-client>=1.5
websockets>=10.4
aiortc>=1.9.0
pyinstaller
```

---

## 📝 Licencia

Este proyecto está bajo la licencia que figura en el archivo `LICENSE`.
