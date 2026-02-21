# HUD Unificado: Action Grid, Minimap y Barras

Este documento define el sistema de HUD unificado: un Grid de Acciones 10×3 con paginación que muestra todas las habilidades y sus teclas/ratón asignados por modo, y la orquestación del minimapa, barra de experiencia, panel de objetivo, estadísticas HP/MP, reloj y toasts. El objetivo es una UI coherente, desacoplada y fácil de extender, sin romper los sistemas existentes.

---

## 1) Estado actual (inventario y puntos de integración)

- **Minimap (MVC+Events, engine)**
  - `src/roguelike_engine/minimap/minimap_model.py` (modelo)
  - `src/roguelike_engine/minimap/minimap_view.py` (vista)
  - `src/roguelike_engine/minimap/minimap_controller.py` (controlador)
  - `src/roguelike_engine/minimap/minimap_events.py` (eventos)
  - Config: `src/roguelike_engine/config/config_minimap.py`
  - En pipeline: `pipeline_helpers.should_render_minimap`, `pipeline_runner._step_minimap`

- **Barras y HUDs de juego (game/ecs)**
  - XP: `src/roguelike_game/ecs/systems/rendering/experience_render_system.py`
  - HP/MP: `src/roguelike_game/ecs/systems/rendering/hud_stats_render_system.py`
  - Target HUD (objetivo): `src/roguelike_game/ecs/systems/rendering/target_hud_render_system.py`
  - Toasts (mensajes): `src/roguelike_game/ecs/systems/rendering/toast_render_system.py`
  - Reloj: `src/roguelike_game/managers/core/render/pipeline_helpers.py::render_game_clock`

- **Render Pipeline (orquestación actual)**
  - `src/roguelike_game/managers/core/render/pipeline_runner.py` (pasos 3.8 minimap, 3.85 clock)
  - Z UI: `src/roguelike_engine/config/config_z_layer.py` (‘ui’ = 10)

- **Input y bindings (base para Action Grid)**
  - `src/roguelike_game/config/input_config.py` y `_input_config_impl.py`
  - Tri-slot A/B de teclado (`kb_<action>_a/b`) y bindings de ratón (`mouse_<action>`)
  - Consumido por `ecs/systems/input/input_system.py` (con supresiones por modo/editor)

Conclusión: ya existen piezas de HUD robustas y reglas de visibilidad. Falta una capa de **orquestación** y el nuevo **Action Grid** con paginación y perfiles por modo.

---

## 2) Objetivos funcionales

- **Grid de Acciones 10×3** con paginación visual y navegación por teclado/ratón.
- Mostrar para cada casilla: icono/etiqueta de habilidad, teclas asignadas (A/B) y botón de ratón si aplica.
- **Perfiles por modo**: gameplay, editores (tiles, buildings, map), inventario, spells editor, etc.
- **Sincronizado** con `InputConfig`: refleja cambios de bindings en caliente.
- **Orquestación** central de HUD: evita solapes, respeta reglas de visibilidad y z-order UI.

No objetivos (v1): reconfiguración de teclas desde el HUD (llegará en una iteración posterior).

---

## 3) Arquitectura propuesta

### 3.1 Action Grid (MVC + Events)

- **Modelo (`ActionGridModel`)**
  - Estado: `page`, `items` (lista de acciones a mostrar), `rows=3`, `cols=10`, `pages=N`.
  - Cache de layout (rects por celda) y de texto para minimizar renders.
  - Flags de interacción (hover, selección, pulsado) y mapa `action -> bindings resueltos`.

- **Vista (`ActionGridView`)**
  - Dibuja una rejilla 10×3 anclada (por defecto, parte inferior centrada), con estilos desde `config_hud.py`.
  - Cada celda muestra: nombre/ícono, `K_*` A/B y `M_*` si existen; estado “pressed” (si la acción está activa este frame).

- **Controlador (`ActionGridController`)**
  - Sincroniza modelo con `InputConfig` y el perfil activo (véase 3.2).
  - Resuelve teclas presionadas actuales para resaltar.
  - Gestiona paginación (teclas, rueda de ratón sobre el grid, botones UI prev/next).

- **Eventos (`ActionGridEvents`)**
  - `handle_event(pygame.event.Event)`: hover/paginación/clicks sobre el grid.

### 3.2 Proveedor de perfiles de acciones (por modo)

- **`InputProfileProvider`**
  - API: `get_actions_for_mode(mode: str) -> list[str]`
  - Modos previstos: `gameplay`, `tiles_editor`, `buildings_editor`, `map_editor`, `inventory`, `spells_editor`, `fsm_editor`, `particles_editor`.
  - Fuente: constantes (p. ej., `TRISLOT_BASES`) + acciones de juego clave (“move_*”, “attack”, “interact”, “spell_*”, “toggle_*”).
  - Persistencia: recuerda `page` por modo en `data/config/hud_prefs.json`.

### 3.3 Orquestador de HUD

- **`HudOrchestrator`** centraliza update/render de todos los widgets HUD:
  - Action Grid, Minimap (+ botones), Reloj, XP bar, HP/MP, Target HUD, Toasts.
  - Aplica reglas de visibilidad (reusa `should_render_minimap`) y layout (anclas/márgenes).
  - Expone métodos: `update(world, screen, camera)`, `render(screen)`, `handle_event(evt)`.

### 3.4 Reglas de visibilidad (fuente única)

- Reutilizar y generalizar `pipeline_helpers.should_render_minimap(state, menu)` a `should_render_hud_widget(widget_id, state, menu)`.
- Condiciones conocidas: editores activos, inventario/selección clase, menús; `world.suppress_hud`.

### 3.5 HUD dentro del ECS: evaluación crítica

- **Ventajas**
  - Unifica el ciclo `update/render` con el resto del juego y aprovecha composición por componentes.
  - Permite que clics/inputs del HUD generen los mismos eventos/Componentes que el teclado (misma semántica).
  - Facilita pruebas aisladas (inyectar `World` con componentes) y respeto de supresiones (`block_reason`).
  - Ya existen varios HUDs como sistemas ECS de render (XP, Target HUD, HP/MP, Toasts), por lo que el salto es pequeño.

- **Inconvenientes**
  - El minimapa actual vive en `engine/` con caches y configuración propias; migrarlo 1:1 al ECS puede añadir complejidad o duplicación.
  - Routing de eventos de UI globales (pygame) a entidades UI requiere una capa de distribución (hit-tests, foco, z‑order UI).
  - Riesgo de orden de sistemas: hay que garantizar que el enrutado de UI ocurra antes del despacho de acciones y que respete supresiones.

- **Decisión recomendada (híbrido ECS‑first)**
  - Mantener los HUDs de juego como sistemas ECS (XP, HP/MP, Target, Toasts) y añadir el **Action Grid** como sistema ECS (render + input).
  - Mantener el minimapa en `engine/` a corto plazo, exponiendo un `MinimapEcsProxySystem` opcional si fuese necesario para uniformidad.
  - Unificar la ejecución de acciones (teclado/ratón/UI) mediante una única ruta de despacho en el ECS.

### 3.6 Flujo de eventos click → acción (ECS)

1) `pygame.event` → `UiEventRouterSystem` (ECS):
   - Traduce eventos de mouse a un stream ECS y resuelve hit-tests sobre entidades UI (rects del grid).
   - Emite un evento/flag por celda: `UiClick(action_id)` o marca `UiPressed/UiHover` en componentes.

2) `ActionGridSystem` (ECS):
   - Conoce layout y bindings (vía `InputConfig`). Si hay `UiClick(action_id)`, invoca el mismo camino que el teclado para esa acción.

3) `ActionDispatch` (servicio/sistema):
   - Punto único de verdad para ejecutar acciones de gameplay. Convierte una intención en componentes del mundo:
     - Ej.: `action_id='spell_fireball'` → set de componentes `WantsToCastSpell` o toggles correspondientes.
   - Reutilizable por `InputSystem` (teclado) y `ActionGridSystem` (UI), evitando duplicación.

4) Sistemas de gameplay consumen esos componentes como hoy (ataque, hechizos, toggles).

5) Supresión: `block_reason(world)` se evalúa antes de despachar; si suprime, no se emiten componentes.

### 3.7 Componentes y sistemas ECS propuestos

- Componentes UI:
  - `UiRect(anchor, rect)` — área interactiva; anchor relativo a pantalla.
  - `UiState(hover: bool, pressed: bool)` — estado efímero por frame.
  - `ActionGridComponent(page, items, bindings_cache)` — datos del grid.
  - `ActionCommandQueue(list[str])` — cola por jugador con `action_id` a despachar.

- Sistemas UI:
  - `UiEventRouterSystem` — recoge eventos pygame y marca `UiState` según `UiRect`.
  - `ActionGridRenderSystem` — dibuja el grid (usa `config_hud.py`) y muestra estado `pressed`.
  - `ActionGridInputSystem` — si `pressed` sobre celda: encola `action_id` en `ActionCommandQueue` del jugador.
  - `ActionDispatchSystem` (opcional si no se reutiliza `InputSystem`) — transforma cola en componentes de gameplay.

Orden sugerido (update): `UiEventRouterSystem` → `ActionGridInputSystem` → `InputSystem`/`ActionDispatchSystem` → gameplay.

---

## 4) Integración en el pipeline

Ubicación sugerida en `pipeline_runner.py`:

1. 3.8 Minimap (igual que ahora).
2. 3.85 Clock (igual que ahora).
3. 3.87 HUD Orchestrator (nuevo):
   - `orchestrator.render(screen)` dibuja: Action Grid, XP bar, HP/MP, Target HUD, Toasts.

El `UpdateManager` ya actualiza el minimapa (2.5). Añadiremos `orchestrator.update(...)` en el mismo bloque 2.x, tras cámara y entidades, para obtener estados “pressed”.

---

## 5) Diseño de layout y posicionamiento

- **Anclas por defecto** (configurable):
  - Minimap: esquina superior derecha (existente).
  - Reloj: pegado bajo el minimapa (existente).
  - Target HUD: superior centrado.
  - HP/MP: inferior izquierda.
  - XP: inferior centrado (encima del borde inferior).
  - Action Grid: inferior centrado, encima de la barra de XP (separación configurable).
  - Toasts: inferior derecha.

- **Anti-solape**: el Orchestrator calcula rects de cada widget y aplica offsets si hay colisiones suaves (p. ej., empujar el grid un poco arriba si la XP bar es más alta). Mantener determinismo y coste O(W), W = widgets HUD.

---

## 6) Configuración

Archivo nuevo: `src/roguelike_engine/config/config_hud.py`

- Dimensiones, márgenes y paddings (grid cell, bordes, tipografías).
- Colores y estados (normal/hover/pressed, disabled/unbound).
- Teclas de paginación por defecto (p. ej., `K_PAGEUP`, `K_PAGEDOWN`) y mouse wheel.
- Opacidad de fondos y bordes.

---

## 7) API y contratos (alto nivel)

```python
# HudOrchestrator (bosquejo)
class HudOrchestrator:
    def __init__(self, *, input_config: InputConfig, profiles: InputProfileProvider, minimap, systems):
        ...  # inyectar view/controllers existentes (XP, HP/MP, TargetHUD, Toasts)

    def update(self, world, screen, camera) -> None:
        # actualizar ActionGrid (bindings, pressed), calcular layout y visibilidad
        ...

    def render(self, screen) -> None:
        # dibujar en z UI respetando rects y orden recomendado
        ...

    def handle_event(self, event) -> bool:
        # delega a ActionGridEvents y MinimapEvents
        ...

# InputProfileProvider (bosquejo)
class InputProfileProvider:
    def get_mode(self, world, state) -> str: ...  # detecta modo actual
    def get_actions_for_mode(self, mode: str) -> list[str]: ...
```

Notas:
- El Orchestrator no reimplementa XP/HP/Target/Toasts; solo decide si/ dónde se dibujan y llama a sus `update()`/`render()`.
- Action Grid es un nuevo módulo auto-contenido con su propio MVC+Events.

---

## 8) Estructura de carpetas y archivos (a crear)

```
src/
  roguelike_engine/
    config/
      config_hud.py                # constantes de HUD (colores, tamaños, márgenes, fuentes)
  roguelike_ui/
    hud/
      orchestrator/
        hud_orchestrator.py       # clase principal de orquestación
      action_grid/
        action_grid_model.py
        action_grid_view.py
        action_grid_controller.py
        action_grid_events.py
  roguelike_game/
    managers/
      core/
        hud/
          __init__.py             # contenedor para inyección en managers
    ecs/
      components/
        ui/
          action_grid_component.py    # datos del grid y bindings cache
          ui_rect.py                  # área interactiva por entidad UI
          ui_state.py                 # hover/pressed
          action_command_queue.py     # cola de acciones por jugador
      systems/
        ui/
          ui_event_router_system.py   # traduce pygame.events a estados UI ECS
          action_grid_render_system.py
          action_grid_input_system.py # click -> encolar acción
        input/
          action_dispatch_system.py   # única transformación acción -> componentes gameplay (si no se reutiliza InputSystem)
```

Integraciones menores en:
- `pipeline_runner.py` (añadir paso 3.87 HUD Orchestrator).
- `update_manager.py` (añadir `orchestrator.update(...)`).

---

## 9) Reglas de visibilidad (unificadas)

- Base: `pipeline_helpers.should_render_minimap` (editores, inventario, menús, etc.).
- Generalización: `should_render_hud_widget(id, state, menu)` con políticas:
  - `minimap` y `clock`: como hoy.
  - `grid`: oculto si hay editores de alto foco (map/tiles/buildings) o menús modales.
  - `xp`, `hpmp`, `target`, `toasts`: visibles si `not world.suppress_hud` y no hay overlays modales.

---

## 10) Rendimiento y calidad

- Caches en `ActionGridView`: superficies de texto, layout de rects por celda.
- Redibujo incremental: solo cuando cambia `page`, bindings o estado “pressed”.
- Reutilizar fuentes (evitar `pygame.font.SysFont` por frame; patrón ya aplicado en sistemas existentes).
- Telemetría opcional: tiempos `update/render` del Orchestrator y Grid (clave `3.87.*`).
- Complejidad: O(N) con N = celdas visibles (≤ 30); coste bajo comparado con escena.

---

## 11) Plan de despliegue incremental

1. Crear `config_hud.py` con constantes y estilos base.
2. Añadir componentes ECS: `ActionGridComponent`, `UiRect`, `UiState`, `ActionCommandQueue`.
3. Implementar sistemas ECS: `UiEventRouterSystem`, `ActionGridRenderSystem`, `ActionGridInputSystem`.
4. Implementar `InputProfileProvider` y lectura real de `InputConfig` en el Grid.
5. Unificar despacho de acciones: reutilizar `InputSystem` o introducir `ActionDispatchSystem` y extraer `ActionDispatch` como servicio común.
6. (Opcional) Orchestrator como facade de layout sólo si se requiere anti‑solape avanzado; en caso contrario, mantener puro ECS para el Grid.
7. Integrar en `update_manager` el orden de sistemas para respetar supresiones y edges.
8. QA manual: visibilidad por modo, clics ejecutando acciones reales, paginación, rendimiento.
9. Persistencia `hud_prefs.json` (última página por modo); telemetría opcional.

---

## 12) Aceptación y criterios de éxito

- Grid 10×3 visible en gameplay, con paginación y bindings correctos (teclado A/B y ratón).
- Cambio de modo actualiza dinámicamente el conjunto de acciones.
- No hay solapes visuales entre widgets HUD con layout por defecto.
- Reglas de visibilidad coherentes con los editores/menús existentes.
- Costo de render estable; sin exceptions en el loop principal aunque falle un widget.

---

## 13) Riesgos y mitigaciones

- Solapes en resoluciones bajas → orquestación con anti-solape y márgenes responsables.
- Desincronización de bindings → `ActionGridController` recarga `InputConfig._load()` con throttling.
- Múltiples fuentes de verdad de visibilidad → centralizar en Orchestrator + helpers de pipeline.
- Carga visual excesiva → estilos sobrios, opacidades moderadas y límites de texto.

---

## 14) Glosario rápido (términos)

- **HUD (Heads-Up Display)** — Superposición de información de juego — Mostrar estado sin pausar — Ej.: HP/MP, minimapa.
- **Orquestador (orchestrator)** — Módulo que coordina varios subsistemas — Evita solapes/duplicaciones — Ej.: `HudOrchestrator`.
- **Perfil de entrada (input profile)** — Conjunto de acciones visibles según contexto — Cambia por modo — Ej.: `gameplay` vs `map_editor`.
- **Paginación** — Dividir elementos en páginas navegables — Mejora legibilidad — Ej.: 30 slots por página.
- **Binding** — Mapeo acción→tecla/botón — Gestionado por `InputConfig` — Ej.: `kb_fireball_a = K_Q`.

---

## 15) Referencias de código

- Minimap: `src/roguelike_engine/minimap/*`, `config_minimap.py`, `should_render_minimap`.
- XP/HP/Target/Toasts: `src/roguelike_game/ecs/systems/rendering/*`.
- Reloj: `pipeline_helpers.render_game_clock`.
- Input: `src/roguelike_game/config/input_config.py`, `_input_config_impl.py`, `ecs/systems/input/input_system.py`.

---

## 16) Próximos pasos inmediatos

- Definir `config_hud.py` (constantes) y esqueleto de `ActionGrid`.
- Borrador de `HudOrchestrator` con solo Grid activo, sin mover sistemas existentes.
- Hook mínimo en `update_manager` y `pipeline_runner` detrás del reloj.

