"""
Centraliza la lógica de captura y despacho de eventos extraída de Game.
"""

import pygame
import roguelike_engine.config.config as config
from roguelike_engine.input.events import handle_events as engine_handle_events
from roguelike_editors.buildings.utils.save_buildings_to_json import save_buildings_to_json
from roguelike_engine.config.config import BUILDINGS_DATA_PATH
from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.fsm.fsm_editor_events import FsmEditorEventHandler
from roguelike_ui.ui_blocker import is_blocked

import logging
logger = logging.getLogger(__name__)

def handle_events(game):
    # Procesar QUIT antes que nada
    if pygame.event.peek(pygame.QUIT):
        pygame.event.get(pygame.QUIT)
        # Persistir colisiones globales antes de salir (si existe el editor)
        try:
            be = getattr(game, 'buildings_editor', None)
            bm = getattr(game, 'buildings', None)
            if be and bm and hasattr(be, 'colliders') and hasattr(be.colliders, 'events'):
                be.colliders.events._save_collisions(bm.buildings, force=True)
        except Exception:
            pass
        # Guardar buildings con overrides CU
        try:
            bm = getattr(game, 'buildings', None)
            if bm and hasattr(bm, 'buildings'):
                save_buildings_to_json(
                    bm.buildings,
                    BUILDINGS_DATA_PATH,
                    z_state=getattr(game.state, 'z_state', None),
                    zone_offsets=getattr(global_map_settings, 'zone_offsets', None),
                )
        except Exception:
            pass
        game.state.running = False
        return

    # Capturar eventos
    events = pygame.event.get()
    # Pre-despacho: permitir que el DiagnosticsOverlay consuma eventos de ratón siempre
    # incluso si luego retornamos temprano por menús/editores.
    # Use the Diagnostics overlay instance
    overlay = getattr(game.renderer, 'diagnostics_overlay', None)
    consumed_idx: set[int] = set()
    if overlay and overlay.panel_rect:
        for i, ev in enumerate(events):
            if ev.type in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN):
                pos = pygame.mouse.get_pos() if ev.type == pygame.MOUSEWHEEL else ev.pos
                if overlay.hit_test(pos):
                    if overlay.handle_event(ev):
                        consumed_idx.add(i)
    # Priorizar consola
    for event in events:
        if game.console_events.process_event(event):
            return
    # ESC: si el selector de clase está abierto, ciérralo; si no, toggle menú
    for event in events:
        if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
            if hasattr(game, 'class_selector') and getattr(game.class_selector, 'show', False):
                game.class_selector.show = False
                # Clear blocker flag when closing selector with ESC
                try:
                    game.state.class_selector_open = False
                except Exception:
                    pass
                return
            game.menu.show_menu = not game.menu.show_menu
            return

    # Si el menú está abierto, solo procesar inputs de menú
    if game.menu.show_menu:
        for event in events:
            if event.type == pygame.KEYDOWN:
                result = game.menu.handle_input(event)
                if result:
                    game.menu.execute_menu_option(result, game.state)
            elif event.type == pygame.MOUSEBUTTONDOWN:
                mx, my = event.pos
                # Calcular posición centrada
                width = game.menu.renderer.surface.get_width()
                height = game.menu.renderer.surface.get_height()
                screen_w, screen_h = game.screen.get_size()
                x = (screen_w - width) // 2
                y = (screen_h - height) // 2
                if x <= mx <= x + width and y <= my <= y + height:
                    rel_y = my - y
                    idx = (rel_y - 40) // 50
                    options = game.menu.handler.get_options()
                    if 0 <= idx < len(options):
                        game.menu.execute_menu_option(options[idx], game.state)
        return

    # Si el selector de clase está abierto
    if hasattr(game, 'class_selector') and game.class_selector.show:
        # While the selector is visible, mark input as blocked for gameplay
        try:
            game.state.class_selector_open = True
        except Exception:
            pass
        for event in events:
            result = game.class_selector.handle_input(event)
            if result:
                game.player_manager.change_class(result)
        # Reflect current visibility after handling inputs (may have closed)
        try:
            game.state.class_selector_open = bool(getattr(game.class_selector, 'show', False))
        except Exception:
            pass
        return

    # Debug overlay ya pudo haber consumido algunos eventos arriba.

    for event in events:
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('select_class'):
            game.class_selector.show = not game.class_selector.show
            # Sync blocker flag with visibility on toggle
            try:
                game.state.class_selector_open = bool(game.class_selector.show)
            except Exception:
                pass
            return
        if event.type == pygame.KEYDOWN and event.key == pygame.K_F3:
            # Toggle Spawner Editor visibility and mirror a global debug flag
            try:
                game.spawner_editor.controller.toggle_visible()
                config.DEBUG_SPAWNER = bool(getattr(game.spawner_editor.model, 'visible', False))
            except Exception:
                pass
            return
        if event.type == pygame.KEYDOWN and event.key == pygame.K_F4:
            game.spells_editor.model.visible = not game.spells_editor.model.visible
            return
        if event.type == pygame.KEYDOWN and event.key == pygame.K_F5:
            game.entities_editor.model.visible = not game.entities_editor.model.visible
            return
        if event.type == pygame.KEYDOWN and event.key == pygame.K_F6:
            logger.debug(f"[Controller] Toggling Inventory Editor. Old visible: {game.inventory_editor.model.visible}")
            new_vis = not game.inventory_editor.model.visible
            game.inventory_editor.model.visible = new_vis
            if new_vis:
                logger.debug(f"[Controller] Loading JSON entities for category {game.inventory_editor.model.current_category}...")
                data = game.inventory_editor.model.active_data.get(game.inventory_editor.model.current_category, {})
                entities = list(data.keys()) if isinstance(data, dict) else []
                game.inventory_editor.model.entities = entities
                logger.debug(f"[Controller] JSON Entities loaded: {entities}")
                prev = game.inventory_editor.model.selected_eid
                if prev in entities:
                    selected = prev
                else:
                    selected = entities[0] if entities else None
                game.inventory_editor.model.selected_eid = selected
                logger.debug(f"[Controller] Selected EID: {selected}")
                game.inventory_editor.debug_dump()
            return
        if event.type == pygame.KEYDOWN and event.key == pygame.K_F7:
            game.state.item_editor_state.visible = not game.state.item_editor_state.visible
            return
        if event.type == pygame.KEYDOWN and event.key == pygame.K_F9:
            config.DEBUG = not config.DEBUG
            logger.debug(f"🧪 DEBUG {'activado' if config.DEBUG else 'desactivado'}")
            return
        if FsmEditorEventHandler.handle_event(event):
            # Evento consumido por el FSM editor/spy (por ejemplo, F12)
            return
        if event.type == pygame.KEYDOWN and event.key == game.menu.input_config.get_key('toggle_tile_editor'):
            game.tiles_editor.toggle()
            return
        if event.type == pygame.KEYDOWN and event.key == game.menu.input_config.get_key('toggle_building_editor'):
            # Toggle building editor open/close
            new_active = not game.buildings_editor.editor_state.active
            # Si estamos cerrando el editor, persistir colisiones CG y refrescar índice espacial
            if not new_active:
                try:
                    be = game.buildings_editor
                    bm = game.buildings
                    if hasattr(be, 'colliders') and hasattr(be.colliders, 'events'):
                        be.colliders.events._save_collisions(bm.buildings, force=True)
                except Exception:
                    pass
                # Guardar buildings con overrides CU
                try:
                    save_buildings_to_json(
                        bm.buildings,
                        BUILDINGS_DATA_PATH,
                        z_state=getattr(game.state, 'z_state', None),
                        zone_offsets=getattr(global_map_settings, 'zone_offsets', None),
                    )
                except Exception:
                    pass
                # Invalidate spatial index para respetar colisiones inmediatamente en gameplay
                try:
                    game.ecs.ecs_world.invalidate_spatial_index()
                except Exception:
                    pass
            game.buildings_editor.editor_state.active = new_active
            # No forzar apertura del picker al activar: debe iniciar oculto
            game.buildings_editor.editor_state.picker_active = False
            return
        if event.type == pygame.KEYDOWN and event.key == game.menu.input_config.get_key('toggle_map_editor'):
            game.map_editor.toggle()
            return

    # Si el editor de ítems está activo, capturar solo sus eventos
    if game.item_editor.model.visible:
        for event in events:
            game.item_editor.handle_event(event)
        return

    # Si el editor de inventario está activo, capturar solo sus eventos
    if hasattr(game, 'inventory_editor') and game.inventory_editor.model.visible:
        for event in events:
            game.inventory_editor.handle_event(event)
        return

    # Si el editor de hechizos está activo
    if hasattr(game, 'spells_editor') and game.spells_editor.model.visible:
        for event in events:
            game.spells_editor.handle_event(event)
        return

    # Si el editor de entidades está activo
    if hasattr(game, 'entities_editor') and game.entities_editor.model.visible:
        for event in events:
            game.entities_editor.handle_event(event)
        return

    # Si el editor de spawner está activo, permitir que consuma eventos específicos (RMB sobre spawner),
    # pero no retornar: el resto de eventos deben seguir hacia el motor.
    if hasattr(game, 'spawner_editor') and getattr(game.spawner_editor.model, 'visible', False):
        for i, event in enumerate(events):
            try:
                if game.spawner_editor.handle_event(event):
                    consumed_idx.add(i)
            except Exception:
                pass

    # Si un editor de tiles está activo
    if game.tiles_editor.editor_state.active:
        game.tiles_editor.handle(game.camera, game.map, events)
        return

    # Si un editor de edificios está activo
    if game.buildings_editor.editor_state.active:
        game.buildings_editor.handle(game.camera, game.buildings, events)
        return

    # Si el editor de mapa está activo, delegar antes de bloquear eventos por UI
    if hasattr(game, 'map_editor') and getattr(game.map_editor.editor_state, 'active', False):
        game.map_editor.handle(game.camera, game.map, events)
        return

    # Por defecto, delegar al handle de engine
    # Pasar solo eventos no consumidos y no bloqueados por UI al motor.
    blocked_idx: set[int] = set()
    for i, ev in enumerate(events):
        # Rueda del ratón: usa posición actual del cursor
        if ev.type == pygame.MOUSEWHEEL:
            mx, my = pygame.mouse.get_pos()
            if is_blocked(mx, my):
                logger.debug("[Events] Blocked MOUSEWHEEL over UI at (%s,%s)", mx, my)
                blocked_idx.add(i)
        # Clicks dentro de paneles UI (excepto MMB que se usa para pan de cámara)
        elif ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            btn = getattr(ev, 'button', None)
            # Permitir MMB solo si el Spawner Editor está visible
            allow_mmb_ui = bool(getattr(getattr(game, 'spawner_editor', None), 'model', None) and getattr(game.spawner_editor.model, 'visible', False))
            if btn == 2 and allow_mmb_ui:
                logger.debug("[Events] Allowing MMB event=%s over UI passthrough (down/up) [SpawnerEditor visible]", ev.type)
                continue
            mx, my = getattr(ev, 'pos', (None, None))
            if mx is not None and is_blocked(mx, my):
                logger.debug("[Events] Blocked MOUSEBUTTON event=%s (button=%s) over UI at (%s,%s)", ev.type, btn, mx, my)
                blocked_idx.add(i)
        # Bloquear MOUSEMOTION sobre UI salvo cuando se arrastra con MMB
        elif ev.type == pygame.MOUSEMOTION:
            mx, my = getattr(ev, 'pos', (None, None))
            if mx is None:
                continue
            # Detectar si MMB está pulsado durante el movimiento
            mmb_held = False
            buttons = getattr(ev, 'buttons', None)
            try:
                if buttons and len(buttons) >= 3:
                    mmb_held = bool(buttons[1])
                else:
                    mmb_held = bool(pygame.mouse.get_pressed(3)[1])
            except Exception:
                mmb_held = False
            allow_mmb_ui = bool(getattr(getattr(game, 'spawner_editor', None), 'model', None) and getattr(game.spawner_editor.model, 'visible', False))
            if is_blocked(mx, my) and not (mmb_held and allow_mmb_ui):
                blocked_idx.add(i)
            elif is_blocked(mx, my) and mmb_held and allow_mmb_ui:
                logger.debug("[Events] Allowing MOUSEMOTION with MMB held over UI [SpawnerEditor visible]")
    remaining_events = [e for idx, e in enumerate(events) if idx not in consumed_idx and idx not in blocked_idx]
    # Pass remaining events and diagnostics overlay to the engine input handler
    engine_handle_events(
        game.state,
        game.camera,
        game.clock,
        game.menu,
        game.map,
        game.buildings,
        game.tiles_editor,
        game.buildings_editor,
        game.map_editor,
        remaining_events,
        diagnostics_overlay=overlay,
    )
