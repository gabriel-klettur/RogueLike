# Consola (quake-like) del motor

Este paquete implementa una consola superpuesta al juego para ejecutar comandos en tiempo real.

## Arquitectura

- `console_model.py`
  - `ConsoleState`: estado (historial, buffer, cursor, scrollback).
  - `CommandRegistry`: registro de comandos con metadatos (`usage`, `help`, `category`, `aliases`, `completer`) y parser `shlex`.
- `console_controller.py`
  - Lógica de edición (historial, autocompletar, movimientos del cursor, borrar palabra/char, scroll del historial, TEXTINPUT on/off).
- `console_events.py`
  - Router de eventos: `KEYDOWN` (Enter/Tab/Up/Down/PageUp/PageDown/Backspace/Delete/←/→/Home/End) y `TEXTINPUT`.
- `console_view.py`
  - Render del overlay con clipping, scrollback y scrollbar.
- `parser.py`
  - `ConsoleParser` (shlex) y `ParseContext` para autocompletar contextual.
- `command_sets/`
  - Conjuntos de comandos por dominio: `core.py`, `inventory.py`, agregador `__init__.py`.
- `contexts/`
  - Contextos por dominio (acceso a subsistemas del juego), p. ej. `inventory.py`.

API pública desde `roguelike_engine.console`:

```python
from roguelike_engine.console import (
    ConsoleState, CommandRegistry, ConsoleController,
    ConsoleView, ConsoleEvents, register_commands,
)
```

## Inicialización (ejemplo)

```python
state = ConsoleState()
registry = CommandRegistry()
register_commands(registry, game)
controller = ConsoleController(state, registry)
events = ConsoleEvents(controller)
rect = pygame.Rect(0, screen_h - screen_h//3, screen_w, screen_h//3)
view = ConsoleView(state, rect)
```

## Extender con nuevos comandos

1. Crear un contexto si hace falta (acceso al juego):
   - `src/roguelike_engine/console/contexts/mi_contexto.py`
2. Crear un command set:
   - `src/roguelike_engine/console/command_sets/mi_contexto.py`
   - Definir `register_mi_contexto_commands(registry, game)` e invocarlo desde `command_sets/__init__.py`.
3. Registrar comandos con metadatos:

```python
registry.register(
    'mi_cmd', mi_handler,
    usage='mi_cmd <arg1> [arg2]',
    help='Descripción de mi_cmd.',
    category='mi_contexto',
    aliases=['mc'],
    completer=lambda args: ['sugerencia1', 'sugerencia2'] if len(args) == 1 else []
)
```

## Controles y atajos

- Apertura/cierre: tecla backquote (`~`/`` ` ``)
- Ejecutar: Enter
- Autocompletar: Tab
- Historial: Flechas Arriba/Abajo
- Scroll de historial: PageUp/PageDown
- Edición:
  - Backspace / Delete
  - Ctrl+Backspace / Ctrl+Delete (borrar palabra)
  - ← → Home End (mover cursor)
- Entrada de texto: `TEXTINPUT` habilitado al abrir la consola (mejor soporte Unicode/pegado)

## Límites y seguridad

- El historial tiene un límite configurable (`max_lines`).
- El render usa clipping y scrollbar para evitar overdraw y mantener el rendimiento.
- El parser usa `shlex` y es tolerante a comillas sin cerrar (fallback al split clásico).

## Pruebas

- Parser con comillas y casos límite.
- Registro con metadatos y autocompletado por nombre/argumentos.
- Comandos de inventario con un `game` simulado (mock sencillo).
