"""
Centraliza la lógica de captura y despacho de eventos extraída de Game.
"""

import pygame
import os
import roguelike_engine.config.config as config
from roguelike_engine.input.events import handle_events as engine_handle_events
from roguelike_editors.buildings.utils.save_buildings_to_json import save_buildings_split
from roguelike_engine.config.config import BUILDINGS_TEMPLATES_PATH, BUILDINGS_INSTANCES_PATH
from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.fsm.fsm_editor_events import FsmEditorEventHandler
from roguelike_ui.ui_blocker import is_blocked
from roguelike_game.ecs.systems.chat.chat_input_controller import ChatInputController
from roguelike_game.ecs.systems.chat.chat_ui_system import handle_chat_ui_events
from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble

import logging
logger = logging.getLogger(__name__)

# --- Centralized editor visibility management ---------------------------------
def _close_all_editors(game) -> None:
    """Close all editors, running their respective close side-effects.

    Ensures mutual exclusivity by turning OFF visibility/active flags everywhere.
    """
    # Spawner Editor
    try:
        sp = getattr(game, 'spawner_editor', None)
        if sp and getattr(getattr(sp, 'model', None), 'visible', False):
            sp.controller.toggle_visible()
    except Exception:
        pass
    try:
        # Mirror global debug flag
        config.DEBUG_SPAWNER = False
    except Exception:
        pass
    # Spells Editor
    try:
        if getattr(getattr(game, 'spells_editor', None), 'model', None) and getattr(game.spells_editor.model, 'visible', False):
            game.spells_editor.model.visible = False
    except Exception:
        pass
    # Particles Editor
    try:
        if getattr(getattr(game, 'particles_editor', None), 'model', None) and getattr(game.particles_editor.model, 'visible', False):
            game.particles_editor.model.visible = False
    except Exception:
        pass
    # Entities Editor
    try:
        if getattr(getattr(game, 'entities_editor', None), 'model', None) and getattr(game.entities_editor.model, 'visible', False):
            game.entities_editor.model.visible = False
    except Exception:
        pass
    # Inventory Editor
    try:
        if getattr(getattr(game, 'inventory_editor', None), 'model', None) and getattr(game.inventory_editor.model, 'visible', False):
            game.inventory_editor.model.visible = False
            # Audio: sfx abrir/cerrar inventario (usar mismo sonido)
            try:
                aq = game.ecs.ecs_world.components.setdefault('AudioEventQueue', [])
                aq.append({'type': 'play_sfx', 'sfx_id': 'inv_open', 'group': 'ui'})
            except Exception:
                pass
    except Exception:
        pass
    # Items Editor
    try:
        it = getattr(game, 'item_editor', None)
        if it and getattr(getattr(it, 'model', None), 'visible', False):
            it.hide()
    except Exception:
        pass
    # FSM Editor (Entities Spy)
    try:
        config.DEBUG_ENTITIES = False
    except Exception:
        pass
    # Tiles Editor
    try:
        te = getattr(game, 'tiles_editor', None)
        if te and getattr(getattr(te, 'editor_state', None), 'active', False):
            # Reset key flags similar to ESC/F8 close behavior
            te.editor_state.active = False
            try:
                te.editor_state.picker_state.open = False
                te.editor_state.selected_tile = None
                te.editor_state.brush_dragging = False
                te.editor_state.default_dragging = False
                te.editor_state.delete_dragging = False
            except Exception:
                pass
    except Exception:
        pass
    # Buildings Editor (persist on close + spatial index invalidation)
    try:
        be = getattr(game, 'buildings_editor', None)
        bm = getattr(game, 'buildings', None)
        if be and getattr(getattr(be, 'editor_state', None), 'active', False):
            # Persistir colisiones CG si el editor estaba activo
            try:
                if hasattr(be, 'colliders') and hasattr(be.colliders, 'events'):
                    be.colliders.events._save_collisions(bm.buildings, force=True)
            except Exception:
                pass
            # Guardar buildings con overrides CU
            try:
                # Always persist using split files
                save_buildings_split(
                    bm.buildings,
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
            be.editor_state.active = False
            # No forzar apertura del picker al activar: siempre inicia oculto
            try:
                be.editor_state.picker_active = False
            except Exception:
                pass
    except Exception:
        pass
    # Map Editor
    try:
        me = getattr(game, 'map_editor', None)
        if me and getattr(getattr(me, 'editor_state', None), 'active', False):
            me.editor_state.active = False
    except Exception:
        pass


def _open_editor_exclusive(game, target: str) -> None:
    """Close all editors, then open exactly one target editor.

    target in {'spawner','spells','particles','entities','inventory','items','fsm','tiles','buildings','map'}
    """
    _close_all_editors(game)
    if target == 'spawner':
        try:
            if not getattr(getattr(game.spawner_editor, 'model', None), 'visible', False):
                game.spawner_editor.controller.toggle_visible()
            config.DEBUG_SPAWNER = bool(getattr(game.spawner_editor.model, 'visible', False))
        except Exception:
            pass
    elif target == 'spells':
        try:
            game.spells_editor.model.visible = True
        except Exception:
            pass
    elif target == 'particles':
        try:
            game.particles_editor.model.visible = True
        except Exception:
            pass
    elif target == 'entities':
        try:
            game.entities_editor.model.visible = True
        except Exception:
            pass
    elif target == 'inventory':
        try:
            game.inventory_editor.model.visible = True
            # Initialize entities list as done on manual toggle open
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
            try:
                game.inventory_editor.debug_dump()
            except Exception:
                pass
            # Audio: sfx abrir/cerrar inventario (usar mismo sonido para ambos)
            try:
                aq = game.ecs.ecs_world.components.setdefault('AudioEventQueue', [])
                aq.append({'type': 'play_sfx', 'sfx_id': 'inv_open', 'group': 'ui'})
            except Exception:
                pass
        except Exception:
            pass
    elif target == 'items':
        try:
            game.item_editor.show()
        except Exception:
            pass
    elif target == 'fsm':
        try:
            config.DEBUG_ENTITIES = True
        except Exception:
            pass
    elif target == 'tiles':
        try:
            game.tiles_editor.editor_state.active = True
            # Mimic previous F8-on side effects: show view panel and size panel by default
            try:
                game.tiles_editor.editor_state.toolbar_state.view_active = True
            except Exception:
                pass
            try:
                game.tiles_editor.editor_state.size_panel_state.visible = True
            except Exception:
                pass
        except Exception:
            pass
    elif target == 'buildings':
        try:
            game.buildings_editor.editor_state.active = True
            # Picker inicia oculto
            game.buildings_editor.editor_state.picker_active = False
        except Exception:
            pass
    elif target == 'map':
        try:
            game.map_editor.editor_state.active = True
        except Exception:
            pass

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
        # Guardar buildings con overrides CU (siempre en archivos split)
        try:
            bm = getattr(game, 'buildings', None)
            if bm and hasattr(bm, 'buildings'):
                save_buildings_split(
                    bm.buildings,
                    z_state=getattr(game.state, 'z_state', None),
                    zone_offsets=getattr(global_map_settings, 'zone_offsets', None),
                )
        except Exception:
            pass
        game.state.running = False
        return

    # Capturar eventos
    events = pygame.event.get()

    # Si el chat está abierto, enrutar todos los eventos al controlador de chat
    try:
        world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
        state = getattr(world, 'state', None)
        if state is not None and bool(getattr(state, 'chat_open', False)):
            ctrl = getattr(world, '_chat_input_ctrl', None)
            if ctrl is None:
                ctrl = ChatInputController()
                setattr(world, '_chat_input_ctrl', ctrl)
            # Asegurar que el controlador esté activo y sincronizado con el buffer
            ctrl.ensure_open(world)
            # Enviar todos los eventos al chat y no propagar al gameplay
            try:
                ctrl.handle_events(world, events)
            except Exception:
                pass
            # Manejo de scroll, scrollbar y resize del panel de chat
            try:
                handle_chat_ui_events(world, events)
            except Exception:
                pass
            # Retornar temprano: impedir propagación al gameplay mientras el chat está abierto
            return
    except Exception:
        pass
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
    # Filtrar eventos consumidos por el minimapa (botones de capas)
    try:
        mm = getattr(game, 'minimap', None)
        if mm is not None:
            filtered = []
            for ev in events:
                try:
                    # Solo interesa hover/click del mouse para los botones
                    if ev.type in (pygame.MOUSEMOTION, pygame.MOUSEBUTTONDOWN):
                        if mm.handle_event(ev, game.screen):
                            # Consumido por minimapa: no propagar
                            continue
                except Exception:
                    pass
                filtered.append(ev)
            events = filtered
    except Exception:
        pass

    # Priorizar consola: si está ABIERTA, marcar estado global y no propagar al gameplay
    try:
        if getattr(game, 'console_state', None) is not None:
            # Propagar flag a world.state para que sistemas (p.ej. InputSystem) puedan suprimir inputs continuos
            try:
                world = getattr(game, 'ecs', None).ecs_world
                if world and hasattr(world, 'state'):
                    world.state.console_open = bool(game.console_state.is_open)
            except Exception:
                pass
            if bool(game.console_state.is_open):
                for event in events:
                    try:
                        game.console_events.process_event(event)
                    except Exception:
                        pass
                return
    except Exception:
        pass

    # Priorizar consola (modo compatibilidad): si algún evento es consumido por la consola, no propagar
    for event in events:
        if game.console_events.process_event(event):
            return
    # ESC: si el selector de clase está abierto, ciérralo; si no, comportamiento según modo de menú
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
            # Si estamos en el menú principal (start), no debemos entrar al juego con ESC
            try:
                mode = getattr(game.menu, 'mode', '')
            except Exception:
                mode = ''
            if mode == 'start':
                # Ignorar ESC en el menú principal
                return
            if mode == 'load_list':
                # En lista de partidas, ESC vuelve al menú principal
                try:
                    game.menu.set_mode('start')
                except Exception:
                    game.menu.mode = 'start'
                return
            # En otros modos (pausa), alternar visibilidad del menú
            game.menu.show_menu = not game.menu.show_menu
            return

    # Si el menú está abierto, solo procesar inputs de menú
    if game.menu.show_menu:
        for event in events:
            # En modo 'load_list' delegamos TODOS los eventos relevantes al MenuManager
            # para permitir hover, doble clic y scroll dentro de la lista y panel de detalles.
            mode = getattr(game.menu, 'mode', '')
            if mode == 'load_list':
                if event.type in (pygame.KEYDOWN, pygame.MOUSEMOTION, pygame.MOUSEBUTTONDOWN, pygame.MOUSEWHEEL):
                    game.menu.handle_input(event)
                # Continuar con el siguiente evento; evitamos el manejo genérico abajo
                continue
            if event.type == pygame.KEYDOWN:
                result = game.menu.handle_input(event)
                if result:
                    game.menu.execute_menu_option(result, game.state)
            elif event.type == pygame.MOUSEMOTION:
                # Hover como flechas: actualizar seleccionado según posición del ratón
                mx, my = event.pos
                # Si está activa la pantalla "Pulsa para comenzar", ignorar hover
                try:
                    if getattr(game.menu, '_press_start_active', False) and getattr(game.menu, 'mode', '') == 'start':
                        continue
                except Exception:
                    pass
                try:
                    options = game.menu.handler.get_options()
                except Exception:
                    options = []
                # Usar el rect real dibujado por el renderer si está disponible
                panel_rect = getattr(game.menu.renderer, 'last_menu_panel_rect', None)
                if panel_rect is None:
                    # Fallback conservador si aún no existe el rect
                    try:
                        width, height = game.menu.renderer._measure_menu(options)
                    except Exception:
                        width, height = 320, 200
                    screen_w, screen_h = game.screen.get_size()
                    width = min(width, int(screen_w * 0.9))
                    height = min(height, int(screen_h * 0.85))
                    x = (screen_w - width) // 2
                    y = (screen_h - height) // 2
                    panel_rect = pygame.Rect(x, y, width, height)
                if panel_rect.collidepoint(mx, my) and options:
                    pad_y = getattr(game.menu.renderer, 'padding_y', 24)
                    line_h = getattr(game.menu.renderer, 'line_height', 36)
                    gap = getattr(game.menu.renderer, 'item_gap', 12)
                    block_h = line_h + gap
                    inner_top = panel_rect.top + pad_y
                    inner_y = my - inner_top
                    rows_h = len(options) * line_h + max(0, (len(options) - 1)) * gap
                    if 0 <= inner_y <= rows_h:
                        idx = int(inner_y // block_h)
                        # Clamp defensivo
                        idx = max(0, min(idx, len(options) - 1))
                        if game.menu.handler.selected != idx:
                            game.menu.handler.selected = idx
                            if getattr(config, 'DEBUG', False):
                                logger.debug("[Menu Hover] pos=(%s,%s) -> idx=%s", mx, my, idx)
            elif event.type == pygame.MOUSEBUTTONDOWN:
                mx, my = event.pos
                # Si está activa la pantalla "Pulsa para comenzar", ignorar clicks
                try:
                    if getattr(game.menu, '_press_start_active', False) and getattr(game.menu, 'mode', '') == 'start':
                        continue
                except Exception:
                    pass
                # Calcular rect del panel usando el renderer y las opciones actuales
                try:
                    options = game.menu.handler.get_options()
                except Exception:
                    options = []
                # Usar el rect real dibujado por el renderer si está disponible
                panel_rect = getattr(game.menu.renderer, 'last_menu_panel_rect', None)
                if panel_rect is None:
                    # Fallback conservador
                    try:
                        width, height = game.menu.renderer._measure_menu(options)
                    except Exception:
                        width, height = 300, 200
                    screen_w, screen_h = game.screen.get_size()
                    width = min(width, int(screen_w * 0.9))
                    height = min(height, int(screen_h * 0.85))
                    x = (screen_w - width) // 2
                    y = (screen_h - height) // 2
                    panel_rect = pygame.Rect(x, y, width, height)
                if panel_rect.collidepoint(mx, my):
                    # Calcular índice clicado usando padding y alturas del renderer
                    pad_y = getattr(game.menu.renderer, 'padding_y', 24)
                    line_h = getattr(game.menu.renderer, 'line_height', 36)
                    gap = getattr(game.menu.renderer, 'item_gap', 12)
                    block_h = line_h + gap
                    inner_top = panel_rect.top + pad_y
                    inner_y = my - inner_top
                    total = len(options)
                    rows_h = (total or 1) * line_h + max(0, (total - 1)) * gap
                    if 0 <= inner_y <= rows_h:
                        idx = int(inner_y // block_h)
                        if getattr(config, 'DEBUG', False):
                            logger.debug("[Menu Click] pos=(%s,%s) panel=%s idx=%s total=%s", mx, my, panel_rect, idx, total)
                        if 0 <= idx < total:
                            game.menu.execute_menu_option(options[idx], game.state)
                        else:
                            if getattr(config, 'DEBUG', False):
                                logger.debug("[Menu Click] idx fuera de rango: %s", idx)
                    else:
                        if getattr(config, 'DEBUG', False):
                            logger.debug("[Menu Click] fuera del área de items: inner_y=%s rows_h=%s", inner_y, rows_h)
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
                # Inicializar nuevo juego solo tras elegir la clase
                try:
                    if hasattr(game, 'menu') and game.menu and hasattr(game.menu, 'finalize_new_game_with_class'):
                        game.menu.finalize_new_game_with_class(result)
                except Exception:
                    pass
                # Al elegir clase, detener música del menú
                try:
                    if hasattr(game, 'menu') and game.menu:
                        game.menu.stop_music(fade_ms=500)
                except Exception:
                    pass
        # Reflect current visibility after handling inputs (may have closed)
        try:
            game.state.class_selector_open = bool(getattr(game.class_selector, 'show', False))
        except Exception:
            pass
        # Si el selector se ha cerrado (por ESC u otra vía), detener música del menú
        if not getattr(game.class_selector, 'show', False):
            try:
                if hasattr(game, 'menu') and game.menu:
                    game.menu.stop_music(fade_ms=500)
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
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('toggle_spawner_editor'):
            # Spawner Editor (exclusive)
            try:
                is_vis = bool(getattr(getattr(game, 'spawner_editor', None), 'model', None) and getattr(game.spawner_editor.model, 'visible', False))
            except Exception:
                is_vis = False
            if is_vis:
                _close_all_editors(game)
            else:
                _open_editor_exclusive(game, 'spawner')
            return
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('toggle_spells_editor'):
            # Spells Editor (exclusive)
            try:
                is_vis = bool(getattr(getattr(game, 'spells_editor', None), 'model', None) and getattr(game.spells_editor.model, 'visible', False))
            except Exception:
                is_vis = False
            if is_vis:
                _close_all_editors(game)
            else:
                _open_editor_exclusive(game, 'spells')
            return
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('toggle_particles_editor'):
            # Particles Editor (exclusive) with Left Alt modifier
            try:
                lalt = bool(pygame.key.get_mods() & pygame.KMOD_LALT)
            except Exception:
                lalt = False
            if lalt:
                try:
                    is_vis = bool(getattr(getattr(game, 'particles_editor', None), 'model', None) and getattr(game.particles_editor.model, 'visible', False))
                except Exception:
                    is_vis = False
                if is_vis:
                    _close_all_editors(game)
                else:
                    _open_editor_exclusive(game, 'particles')
                return
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('toggle_entities_editor'):
            # Entities Editor (exclusive)
            try:
                is_vis = bool(getattr(getattr(game, 'entities_editor', None), 'model', None) and getattr(game.entities_editor.model, 'visible', False))
            except Exception:
                is_vis = False
            if is_vis:
                _close_all_editors(game)
            else:
                _open_editor_exclusive(game, 'entities')
            return
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('toggle_inventory_editor'):
            # Inventory Editor (exclusive)
            try:
                is_vis = bool(getattr(getattr(game, 'inventory_editor', None), 'model', None) and getattr(game.inventory_editor.model, 'visible', False))
            except Exception:
                is_vis = False
            if is_vis:
                _close_all_editors(game)
            else:
                _open_editor_exclusive(game, 'inventory')
            return
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('toggle_item_editor'):
            # Items Editor (exclusive)
            try:
                is_vis = bool(getattr(getattr(game, 'item_editor', None), 'model', None) and getattr(game.item_editor.model, 'visible', False))
            except Exception:
                is_vis = False
            if is_vis:
                _close_all_editors(game)
            else:
                _open_editor_exclusive(game, 'items')
            return
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('toggle_debug_overlay'):
            config.DEBUG = not config.DEBUG
            logger.debug(f"🧪 DEBUG {'activado' if config.DEBUG else 'desactivado'}")
            return
        # FSM Editor (Entities Spy): exclusive toggle
        # Delegation of other events occurs in the FSM-visible block further below.
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('toggle_fsm_editor'):
            try:
                import roguelike_engine.config.config as cfg
                is_vis = bool(getattr(cfg, 'DEBUG_ENTITIES', False))
            except Exception:
                is_vis = False
            if is_vis:
                _close_all_editors(game)
            else:
                _open_editor_exclusive(game, 'fsm')
            return
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('toggle_tile_editor'):
            try:
                is_active = bool(getattr(getattr(game, 'tiles_editor', None), 'editor_state', None) and getattr(game.tiles_editor.editor_state, 'active', False))
            except Exception:
                is_active = False
            if is_active:
                _close_all_editors(game)
            else:
                _open_editor_exclusive(game, 'tiles')
            return
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('toggle_building_editor'):
            try:
                is_active = bool(getattr(getattr(game, 'buildings_editor', None), 'editor_state', None) and getattr(game.buildings_editor.editor_state, 'active', False))
            except Exception:
                is_active = False
            if is_active:
                _close_all_editors(game)
            else:
                _open_editor_exclusive(game, 'buildings')
            return
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('toggle_map_editor'):
            try:
                is_active = bool(getattr(getattr(game, 'map_editor', None), 'editor_state', None) and getattr(game.map_editor.editor_state, 'active', False))
            except Exception:
                is_active = False
            if is_active:
                _close_all_editors(game)
            else:
                _open_editor_exclusive(game, 'map')
            return
        # Abrir chat por proximidad/general con la acción 'interact' (ENTER)
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('interact'):
            try:
                world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
                state = getattr(world, 'state', None)
                if world and state and not bool(getattr(state, 'chat_open', False)):
                    comps = getattr(world, 'components', {})
                    pos_map = comps.get('Position', {}) or {}
                    chat_map = comps.get('ChatComponent', {}) or {}
                    player_eid = getattr(world, 'player_entity', None)
                    player_pos = pos_map.get(player_eid)
                    target_eid = None
                    if player_pos and chat_map:
                        # Buscar NPC más cercano dentro de su chat_range
                        try:
                            px = float(getattr(player_pos, 'x', 0.0))
                            py = float(getattr(player_pos, 'y', 0.0))
                        except Exception:
                            px = py = 0.0
                        best_d2 = None
                        for eid, chat in list(chat_map.items()):
                            npc_pos = pos_map.get(eid)
                            if not npc_pos:
                                continue
                            try:
                                dx = float(getattr(npc_pos, 'x', 0.0)) - px
                                dy = float(getattr(npc_pos, 'y', 0.0)) - py
                                d2 = dx*dx + dy*dy
                                rng = float(getattr(chat, 'chat_range', 0.0) or 0.0)
                                if d2 <= (rng * rng):
                                    if best_d2 is None or d2 < best_d2:
                                        best_d2 = d2
                                        target_eid = eid
                            except Exception:
                                continue
                    # Abrir chat con target o general
                    state.chat_open = True
                    state.chat_input_buffer = ""
                    state.chat_target_eid = target_eid
                    if target_eid is not None:
                        greeting = getattr(chat_map.get(target_eid, None), 'greeting', None)
                        if greeting:
                            state.chat_add_message('NPC', str(greeting))
                            try:
                                push_bubble(world, target_eid, str(greeting), color=(255, 235, 180), ttl_ms=2600)
                            except Exception:
                                pass
                    # Consumir para no propagar al motor
                    return
            except Exception:
                pass

    # Si el editor de ítems está activo, permitir MMB pan (pase al engine) y delegar sus eventos
    if game.item_editor.model.visible:
        # Delegar al editor
        for event in events:
            game.item_editor.handle_event(event)
        # Forward solo MMB-down/up y motion con MMB pulsado al engine para panning
        mmb_events = []
        for ev in events:
            if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and getattr(ev, 'button', None) == 2:
                mmb_events.append(ev)
            elif ev.type == pygame.MOUSEMOTION:
                buttons = getattr(ev, 'buttons', None)
                try:
                    mmb_held = bool(buttons and len(buttons) >= 3 and buttons[1]) or bool(pygame.mouse.get_pressed(3)[1])
                except Exception:
                    mmb_held = False
                if mmb_held:
                    mmb_events.append(ev)
        if mmb_events:
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
                game.spawner_editor,
                mmb_events,
                diagnostics_overlay=overlay,
                spells_editor=getattr(game, 'spells_editor', None),
                item_editor=getattr(game, 'item_editor', None),
                fsm_visible=getattr(__import__('roguelike_engine.config.config', fromlist=['config']), 'DEBUG_ENTITIES', False),
            )
        return

    # Si el editor de partículas está activo, delegar sus eventos sin detener el juego
    if hasattr(game, 'particles_editor') and getattr(getattr(game.particles_editor, 'model', None), 'visible', False):
        for event in events:
            try:
                game.particles_editor.handle_event(event)
            except Exception:
                pass

    # Si el editor de inventario está activo, capturar solo sus eventos
    if hasattr(game, 'inventory_editor') and game.inventory_editor.model.visible:
        for event in events:
            game.inventory_editor.handle_event(event)
        return

    # Si el editor de hechizos está activo, permitir MMB pan (pase al engine) y delegar sus eventos
    if hasattr(game, 'spells_editor') and game.spells_editor.model.visible:
        for event in events:
            game.spells_editor.handle_event(event)
        mmb_events = []
        for ev in events:
            if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and getattr(ev, 'button', None) == 2:
                mmb_events.append(ev)
            elif ev.type == pygame.MOUSEMOTION:
                buttons = getattr(ev, 'buttons', None)
                try:
                    mmb_held = bool(buttons and len(buttons) >= 3 and buttons[1]) or bool(pygame.mouse.get_pressed(3)[1])
                except Exception:
                    mmb_held = False
                if mmb_held:
                    mmb_events.append(ev)
        if mmb_events:
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
                game.spawner_editor,
                mmb_events,
                diagnostics_overlay=overlay,
                spells_editor=getattr(game, 'spells_editor', None),
                item_editor=getattr(game, 'item_editor', None),
                fsm_visible=getattr(__import__('roguelike_engine.config.config', fromlist=['config']), 'DEBUG_ENTITIES', False),
            )
        return

    # Si el editor FSM (Entities Spy) está visible, delegar al editor y pasar MMB al motor para pan
    try:
        import roguelike_engine.config.config as cfg
        fsm_vis = bool(getattr(cfg, 'DEBUG_ENTITIES', False))
    except Exception:
        fsm_vis = False
    if fsm_vis:
        # Delegar todos los eventos al FSM editor
        for event in events:
            try:
                # No forzar retorno global; permitir seguir procesando MMB abajo
                FsmEditorEventHandler.handle_event(event)
            except Exception:
                pass
        # Forward solo MMB-down/up y motion con MMB pulsado al engine para panning
        mmb_events = []
        for ev in events:
            if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and getattr(ev, 'button', None) == 2:
                mmb_events.append(ev)
            elif ev.type == pygame.MOUSEMOTION:
                buttons = getattr(ev, 'buttons', None)
                try:
                    mmb_held = bool(buttons and len(buttons) >= 3 and buttons[1]) or bool(pygame.mouse.get_pressed(3)[1])
                except Exception:
                    mmb_held = False
                if mmb_held:
                    mmb_events.append(ev)
        if mmb_events:
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
                game.spawner_editor,
                mmb_events,
                diagnostics_overlay=overlay,
                spells_editor=getattr(game, 'spells_editor', None),
                item_editor=getattr(game, 'item_editor', None),
                fsm_visible=True,
            )
        return

    # Si el editor de entidades está activo
    if hasattr(game, 'entities_editor') and game.entities_editor.model.visible:
        for event in events:
            game.entities_editor.handle_event(event)
        return

    # Si el editor de spawner está activo, permitir que consuma eventos específicos (RMB sobre spawner),
    # pero NO consumir MMB ni su motion para que el motor pueda panear la cámara.
    if hasattr(game, 'spawner_editor') and getattr(game.spawner_editor.model, 'visible', False):
        for i, event in enumerate(events):
            try:
                handled = game.spawner_editor.handle_event(event)
                if handled:
                    # No consumir MMB down/up ni motion con MMB pulsado: dejar que pase al motor en la fase final
                    if event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and getattr(event, 'button', None) == 2:
                        continue
                    if event.type == pygame.MOUSEMOTION:
                        buttons = getattr(event, 'buttons', None)
                        mmb_held = False
                        try:
                            if buttons and len(buttons) >= 3:
                                mmb_held = bool(buttons[1])
                            else:
                                mmb_held = bool(pygame.mouse.get_pressed(3)[1])
                        except Exception:
                            mmb_held = False
                        if mmb_held:
                            continue
                    # Consumir todo lo demás (por ejemplo RMB para mover anchors)
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

    # Si un editor de mapa está activo, delegar antes de bloquear eventos por UI
    if hasattr(game, 'map_editor') and getattr(game.map_editor.editor_state, 'active', False):
        game.map_editor.handle(game.camera, game.map, events)
        return

    # Detección de clic en halo de NPC para abrir chat y consumir el evento (antes del motor)
    try:
        world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
        state = getattr(world, 'state', None) if world else None
        camera = getattr(game, 'camera', None)
        if world and state and camera and not bool(getattr(state, 'chat_open', False)):
            comps = getattr(world, 'components', {})
            pos_map = comps.get('Position', {}) or {}
            chat_map = comps.get('ChatComponent', {}) or {}
            sprite_map = comps.get('Sprite', {}) or {}
            scale_map = comps.get('Scale', {}) or {}
            multi_map = comps.get('MultiCollider', {}) or {}
            player_eid = getattr(world, 'player_entity', None)
            player_pos = pos_map.get(player_eid)
            if player_pos and chat_map:
                # Procesar solo MOUSEBUTTONDOWN de botón izquierdo
                for i, ev in enumerate(events):
                    if ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, 'button', None) == 1:
                        mx, my = getattr(ev, 'pos', pygame.mouse.get_pos())
                        # Ignorar si está sobre una UI bloqueante
                        try:
                            if is_blocked(mx, my):
                                continue
                        except Exception:
                            pass
                        # Buscar un NPC cuyo halo incluya el clic y que esté en rango de chat
                        for eid, chat in list(chat_map.items()):
                            npc_pos = pos_map.get(eid)
                            if not npc_pos:
                                continue
                            # Comprobar rango jugador-NPC
                            try:
                                dx = float(getattr(npc_pos, 'x', 0.0)) - float(getattr(player_pos, 'x', 0.0))
                                dy = float(getattr(npc_pos, 'y', 0.0)) - float(getattr(player_pos, 'y', 0.0))
                                dist = (dx*dx + dy*dy) ** 0.5
                                rng = float(getattr(chat, 'chat_range', 0.0) or 0.0)
                                if dist > rng:
                                    continue
                            except Exception:
                                continue
                            # Centro del halo: preferir centro del sprite con escala
                            try:
                                wx = float(getattr(npc_pos, 'x', 0.0))
                                wy = float(getattr(npc_pos, 'y', 0.0))
                            except Exception:
                                continue
                            spr = sprite_map.get(eid)
                            scl_comp = scale_map.get(eid)
                            scl = float(getattr(scl_comp, 'scale', 1.0) or 1.0)
                            world_cx = world_cy = None
                            base_size = None
                            if spr and hasattr(spr, 'image') and spr.image:
                                try:
                                    sw, sh = spr.image.get_size()
                                    world_cx = wx + (sw * scl) / 2.0
                                    world_cy = wy + (sh * scl) / 2.0
                                    base_size = min(sw, sh) * scl
                                except Exception:
                                    world_cx = world_cy = None
                                    base_size = None
                            # Fallback a collider de pies
                            feet_r = None
                            if world_cx is None or world_cy is None:
                                try:
                                    mc = multi_map.get(eid)
                                    if mc and hasattr(mc, 'colliders'):
                                        feet = mc.colliders.get('feet')
                                        if feet is not None:
                                            if hasattr(feet, 'offset_x') and hasattr(feet, 'offset_y'):
                                                world_cx = wx + float(feet.offset_x)
                                                world_cy = wy + float(feet.offset_y)
                                            if hasattr(feet, 'radius'):
                                                feet_r = float(getattr(feet, 'radius', 0.0) or 0.0)
                                except Exception:
                                    pass
                            if world_cx is None or world_cy is None:
                                world_cx, world_cy = wx, wy
                            # Radio base del halo
                            halo_r_world = None
                            if base_size is not None:
                                try:
                                    halo_r_world = max(12.0, float(base_size) * 0.25)
                                except Exception:
                                    halo_r_world = None
                            if halo_r_world is None and feet_r is not None:
                                halo_r_world = feet_r
                            if halo_r_world is None:
                                halo_r_world = 18.0
                            # Algo más grande (10%) como en el render
                            halo_r_screen = int(max(6.0, halo_r_world * 1.1) * (getattr(camera, 'zoom', 1.0) or 1.0))
                            # Convertir a pantalla y probar hit
                            try:
                                cx, cy = camera.apply((world_cx, world_cy))
                            except Exception:
                                continue
                            dxs = float(mx - cx)
                            dys = float(my - cy)
                            if (dxs*dxs + dys*dys) <= float(halo_r_screen * halo_r_screen):
                                # Abrir chat con este NPC y consumir evento
                                try:
                                    state.chat_open = True
                                    state.chat_target_eid = eid
                                    state.chat_input_buffer = ""
                                    greeting = getattr(chat, 'greeting', None)
                                    if greeting:
                                        state.chat_add_message('NPC', str(greeting))
                                        try:
                                            push_bubble(world, eid, str(greeting), color=(255, 235, 180), ttl_ms=2600)
                                        except Exception:
                                            pass
                                except Exception:
                                    pass
                                consumed_idx.add(i)
                                # No seguir buscando más NPCs por este clic
                                break
                        # Si ya fue consumido, saltar a siguiente evento
                        if i in consumed_idx:
                            continue
    except Exception:
        # No romper el flujo de eventos por errores en detección de halo
        pass

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
            # Permitir MMB sobre UI cuando ciertos editores están visibles (Spawner/Spells/Items/FSM)
            sp_vis = bool(getattr(getattr(game, 'spawner_editor', None), 'model', None) and getattr(game.spawner_editor.model, 'visible', False))
            spells_vis = bool(getattr(getattr(game, 'spells_editor', None), 'model', None) and getattr(game.spells_editor.model, 'visible', False))
            particles_vis = bool(getattr(getattr(game, 'particles_editor', None), 'model', None) and getattr(game.particles_editor.model, 'visible', False))
            items_vis = bool(getattr(getattr(game, 'item_editor', None), 'model', None) and getattr(game.item_editor.model, 'visible', False))
            try:
                import roguelike_engine.config.config as cfg
                fsm_vis = bool(getattr(cfg, 'DEBUG_ENTITIES', False))
            except Exception:
                fsm_vis = False
            allow_mmb_ui = sp_vis or spells_vis or particles_vis or items_vis or fsm_vis
            if btn == 2 and allow_mmb_ui:
                if getattr(config, 'DEBUG', False):
                    logger.debug("[Events] Allowing MMB event=%s over UI passthrough (down/up) [editor visible]", ev.type)
                continue
            mx, my = getattr(ev, 'pos', (None, None))
            if mx is not None and is_blocked(mx, my):
                if getattr(config, 'DEBUG', False):
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
            sp_vis = bool(getattr(getattr(game, 'spawner_editor', None), 'model', None) and getattr(game.spawner_editor.model, 'visible', False))
            spells_vis = bool(getattr(getattr(game, 'spells_editor', None), 'model', None) and getattr(game.spells_editor.model, 'visible', False))
            items_vis = bool(getattr(getattr(game, 'item_editor', None), 'model', None) and getattr(game.item_editor.model, 'visible', False))
            try:
                import roguelike_engine.config.config as cfg
                fsm_vis = bool(getattr(cfg, 'DEBUG_ENTITIES', False))
            except Exception:
                fsm_vis = False
            allow_mmb_ui = sp_vis or spells_vis or items_vis or fsm_vis
            if is_blocked(mx, my) and not (mmb_held and allow_mmb_ui):
                blocked_idx.add(i)
            elif is_blocked(mx, my) and mmb_held and allow_mmb_ui:
                logger.debug("[Events] Allowing MOUSEMOTION with MMB held over UI [editor visible]")
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
        game.spawner_editor,
        remaining_events,
        diagnostics_overlay=overlay,
        spells_editor=getattr(game, 'spells_editor', None),
        item_editor=getattr(game, 'item_editor', None),
        fsm_visible=getattr(__import__('roguelike_engine.config.config', fromlist=['config']), 'DEBUG_ENTITIES', False),
    )
