"""
Centraliza la lógica de captura y despacho de eventos extraída de Game.
"""

import pygame
import roguelike_engine.config.config as config
from roguelike_engine.input.events import handle_events as engine_handle_events

import logging
logger = logging.getLogger(__name__)

def handle_events(game):
    # Procesar QUIT antes que nada
    if pygame.event.peek(pygame.QUIT):
        pygame.event.get(pygame.QUIT)
        game.state.running = False
        return

    # Capturar eventos
    events = pygame.event.get()
    # Priorizar consola
    for event in events:
        if game.console_events.process_event(event):
            return
    # Siempre permitir toggle de menú con ESC
    for event in events:
        if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
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
        for event in events:
            result = game.class_selector.handle_input(event)
            if result:
                game.player_manager.change_class(result)
        return

    # Dispatch mouse events a DebugOverlay
    for ev in events:
        if ev.type in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN):
            game.renderer.debug_overlay.handle_event(ev)

    for event in events:
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('select_class'):
            game.class_selector.show = not game.class_selector.show
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
        if event.type == pygame.KEYDOWN and event.key == pygame.K_F12:
            config.DEBUG_ENTITIES = not config.DEBUG_ENTITIES
            logger.debug(f"🧪 ENTITIES DEBUG {'activado' if config.DEBUG_ENTITIES else 'desactivado'}")
            return
        if event.type == pygame.KEYDOWN and event.key == game.menu.input_config.get_key('toggle_tile_editor'):
            game.tiles_editor.toggle()
            return
        if event.type == pygame.KEYDOWN and event.key == game.menu.input_config.get_key('toggle_building_editor'):
            # Toggle building editor open/close
            new_active = not game.buildings_editor.editor_state.active
            game.buildings_editor.editor_state.active = new_active
            game.buildings_editor.editor_state.picker_active = new_active
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

    # Si un editor de tiles está activo
    if game.tiles_editor.editor_state.active:
        game.tiles_editor.handle(game.camera, game.map, events)
        return

    # Si un editor de edificios está activo
    if game.buildings_editor.editor_state.active:
        game.buildings_editor.handle(game.camera, game.buildings, events)
        return

    # Por defecto, delegar al handle de engine
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
        game.renderer.debug_overlay
    )
