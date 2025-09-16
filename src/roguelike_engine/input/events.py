import pygame

from .keyboard     import handle_keyboard
from .mouse        import handle_mouse


def handle_events(
    state,
    camera,
    clock,
    menu,
    map,
    entities,    
    tiles_editor,
    buildings_editor,
    map_editor,
    spawner_editor=None,
    events=None,
    *,
    diagnostics_overlay=None,
    spells_editor=None,
    item_editor=None,
    fsm_visible: bool = False,
):
    """
    Maneja eventos de pygame para input y editores.

    - Prioridad: si algún editor está activo, rutea al handler correspondiente
    - Si no, procesa eventos de juego (keyboard, mouse, continuous)

    Notas:
    - Si `events` viene pre-capturado aguas arriba, se reutiliza (evita dobles lecturas).
    - El overlay de diagnóstico se maneja aguas arriba (core events) por hit-test y consumo.
    - Los handlers de teclado/ratón retornan un booleano indicando si consumieron el evento.
    """
    # Optimized event handling
    active_tiles = tiles_editor.editor_state.active
    active_buildings = buildings_editor.editor_state.active
    active_map = False
    try:
        active_map = map_editor.editor_state.active
    except Exception:
        pass
    # Spawner editor is considered "active" for the purpose of enabling MMB camera panning
    active_spawner = False
    try:
        active_spawner = bool(getattr(getattr(spawner_editor, 'model', None), 'visible', False))
    except Exception:
        active_spawner = False
    # Other editors (visibility toggles) also enable MMB camera panning
    active_spells = False
    try:
        active_spells = bool(getattr(getattr(spells_editor, 'model', None), 'visible', False))
    except Exception:
        active_spells = False
    active_items = False
    try:
        active_items = bool(getattr(getattr(item_editor, 'model', None), 'visible', False))
    except Exception:
        active_items = False
    active_fsm = bool(fsm_visible)
    # Pre-handle editors
    if active_tiles:
        tiles_editor.handler.handle(camera, map)
    elif active_buildings:
        buildings_editor.handler.handle(camera, entities)
    elif active_map:
        # Pass through the same events list captured upstream (may be None)
        map_editor.handler.handle(camera, map, events)
    # Cache handlers and diagnostics panel
    
    kb = handle_keyboard
    ms = handle_mouse

    if events is None:
        events = pygame.event.get()
    consumed_any = False
    for ev in events:
        et = ev.type
        if et == pygame.QUIT:
            state.running = False
        elif et in (pygame.KEYDOWN, pygame.KEYUP):
            # Teclas genéricas del engine (zoom +/-). Atajos globales se manejan en core.events
            consumed_kb = kb(ev, state, camera, clock, menu, entities, tiles_editor, buildings_editor, map_editor, map)
            consumed_any = consumed_any or bool(consumed_kb)
        elif et in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION):
            # Diagnostics overlay is handled upstream in managers/core/events.py
            # Enable MMB camera panning while editors are active; disable in gameplay.
            # Include Spawner/Spells/Items editors visibility and FSM editor visible flag.
            mmb_pan_enabled = (
                active_tiles or active_buildings or active_map or
                active_spawner or active_spells or active_items or active_fsm
            )
            # Avoid double-processing of wheel when Map/Tiles editor already handled it
            if not (et == pygame.MOUSEWHEEL and (active_map or active_tiles)):
                consumed_ms = ms(ev, state, camera, clock, map, entities, mmb_pan_enabled=mmb_pan_enabled)
                consumed_any = consumed_any or bool(consumed_ms)