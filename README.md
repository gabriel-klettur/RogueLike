# RogueLike

**Roguelike** en vista *top-down* desarrollado en **Python** y **Pygame**.
Proporciona un motor modular (`roguelike_engine`) y la lógica de juego (`roguelike_game`) con generación procedural de mazmorras, sistema de cámara, entrada, efectos de partículas y más.

---

## 📁 Estructura del Proyecto

```
RogueLike/
├── launcher.py               # Script de arranque (entry-point principal)
├── setup.py                  # Configuración del paquete (editable)
├── requirements.txt          # Dependencias externas
└── src/
    ├── roguelike_engine/     # Módulo del motor (cámara, utilidades, input, etc.)
    └── roguelike_game/       # Lógica de juego (entidades, mapas, main loop)
```

Dentro de `src/`, se usan **imports absolutos**:

```python
from roguelike_engine.camera.camera import Camera
from roguelike_game.game.game import Game
```

---

## 🚀 Instalación

> **⚠️ Requisitos previos:**
>
> * Python 3.8+
> * pip actualizado (`python -m pip install --upgrade pip`)

1. **Clona el repositorio:**

   ```bash
   git clone <URL_REPO>
   cd RogueLike
   ```

2. **Crea y activa un entorno virtual:**

   **Windows (PowerShell):**

   ```powershell
   python -m venv venv
   .\venv\Scripts\Activate
   ```

   **Linux/macOS:**

   ```bash
   python3 -m venv venv
   source venv/bin/activate
   ```

3. **Instala tu paquete en modo editable:**

   ```bash
   pip install -e .
   ```

4. **Instala las dependencias externas:**

   ```bash
   pip install -r requirements.txt
   ```

5. **(Opcional) Verifica la instalación:**

   ```bash
   pip list
   ```

---

## ▶️ Uso

Puedes lanzar el juego de dos maneras:
**Desde desarrollo:**

```bash
python launcher.py
```

**Vía entry-point instalado:**

```bash
roguelike
```

---

## 🛠️ Desarrollo

* **Layout `src/`**: mantiene el código limpio y evita hacks de `sys.path`.
* `setup.py` define el paquete y el **entry-point** `roguelike`.
* **Imports absolutos** entre paquetes (`from roguelike_game...`).
* Evita imports relativos excesivos.
* Uso de `benchmark` para perfilar `handle_events`, `update` y `render` en modo DEBUG.

---

## 🧪 Pruebas

Las pruebas usan `pytest` y configuran Pygame en modo headless mediante `tests/conftest.py` (drivers `SDL_VIDEODRIVER`/`SDL_AUDIODRIVER` en `dummy`).

Pasos recomendados:

1. Instalar el paquete en editable y dependencias (si no lo hiciste):

   ```bash
   pip install -e .
   pip install -r requirements.txt
   ```

2. Ejecutar las pruebas del editor de tiles (toolbar):

   ```bash
   pytest -q tests/roguelike_editors/tiles/test_tiles_toolbar_panel.py
   ```

Esto cubre las herramientas de `tiles_toolbar_panel` (`select`, `brush`, `eyedropper`, `view`, `view_layers`, `view_collisions`, `delete`, `default`), incluyendo toggles de UI y flujos de batch para `delete/default`.

---

## 🎲 Sistema de Drops

El sistema `MapLoadDropsSystem` carga ítems dropeados desde `data/inventory_map.json` asignándoles los componentes:

- `Position`: posición en el mundo.
- `PhysicalItemComponent`: metadatos `item_id` y cantidad.
- `ZLayer`: capa de renderizado.
- `Sprite`: componente de imagen.
- `Scale`: factor de escala.

La renderización de drops se realiza con el `RenderSystem` junto al jugador y otras entidades, ordenando por `ZLayer` y posición Y. El antiguo `DropRenderSystem` ha sido eliminado.

---

## 👾 FSM para NPCs

Esta versión incluye un sistema de **Máquina de Estados Finita (FSM)** para controlar comportamiento de NPCs:

1. Define estados personalizados heredando de `State` en:

   ```
   src/roguelike_game/ecs/fsm/states/
   ```
2. Crea la FSM con un estado inicial:

   ```python
   fsm = FiniteStateMachine(IdleState())
   ```
3. Asocia la FSM a la entidad:

   ```python
   world.components['NPCState'][eid] = NPCState(fsm, 'Idle')
   ```
4. Registra `FSMSystem()` en `world.py` tras `DeathSystem`.
5. Implementa estados: Idle, Patrol, Aggro, Attack, Flee, Death.
6. Pruebas de integración en:

   ```
   tests/test_fsm_integration.py
   ```
7. Ajusta parámetros en `AIConfig` y perfila con `benchmark`.

---

## 🏗️ Empaquetado con PyInstaller

Puedes generar un ejecutable:

1. **Ejemplo de spec (`roguelike.spec`):**

   ```python
   a = Analysis(
       ['src/roguelike_game/main.py'],
       pathex=['src'],
       datas=[
           ('assets/**/*', 'assets'),
           ('data/**/*', 'data'),
       ],
       ...
   )
   ```

2. **Compila con:**

   ```bash
   pyinstaller roguelike.spec --onefile
   ```

---

## 📦 Dependencias

Listado en `requirements.txt`:

```
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

## 📝 Pasos rápidos de instalación la próxima vez

Cuando clones o descargues el proyecto:

1. Crea un entorno virtual y actívalo.
2. Instala dependencias y tu paquete en modo editable:

   ```bash
   pip install -e .
   pip install -r requirements.txt
   ```
3. Lanza el juego:

   ```bash
   python launcher.py
   ```

   o

   ```bash
   roguelike
   ```

Con esto, evitarás problemas de importación y tendrás todo listo en minutos.

---

## 📝 Licencia

Este proyecto está bajo la licencia que figura en el archivo `LICENSE`.
