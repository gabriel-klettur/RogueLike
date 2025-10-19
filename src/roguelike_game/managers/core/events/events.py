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
from roguelike_engine.diagnostics.recorder import recorder
from roguelike_game.config.hot_reload import reload_all_game_data
from roguelike_editors.particles.services.instances_service import (
    append_instance as _particles_append_instance,
    remove_nearest_instance as _particles_remove_nearest,
    find_nearest_instance as _particles_find_nearest,
    update_instance_position as _particles_update_pos,
)
from roguelike_game.config.particles_config import get_preset as _get_particle_preset
from roguelike_game.ecs.components.transform.position import Position as _EcsPosition
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent as _EcsParticleComp
from roguelike_game.ecs.components.particles.particle_preset_component import ParticlePresetComponent as _EcsParticlePresetComp

import logging
logger = logging.getLogger(__name__)
from .utils import is_mmb_held as _is_mmb_held, allow_mmb_ui as _allow_mmb_ui
from .handlers.editors_common import close_all_editors as __close_all_editors, open_editor_exclusive as __open_editor_exclusive
from .handlers.quit import handle_quit as _handle_quit
from .handlers.chat import handle_chat_open as _handle_chat_open, handle_interact_open as _handle_interact_open, handle_class_selector as _handle_class_selector
from .handlers.active_editors import handle_active_editors as _handle_active_editors
from .handlers.overlay import overlay_consume as _overlay_consume
from .handlers.minimap import filter_minimap_events as _filter_minimap_events
from .handlers.console import handle_console as _handle_console
from .handlers.hot_reload import handle_hot_reload_anywhere as _handle_hot_reload_anywhere
from .handlers.menu import handle_menu as _handle_menu
from .handlers.npc_halo import consume_npc_halo_click as _consume_npc_halo_click
from .handlers.ui_filter import build_remaining_events as _build_remaining_events
from .handlers.particles_map import process_particles_map_input as _process_particles_map_input
from .handlers.toggles import handle_toggles as _handle_toggles

# --- Centralized editor visibility management ---------------------------------
def _close_all_editors(game) -> None:
    return __close_all_editors(game)


def _open_editor_exclusive(game, target: str) -> None:
    return __open_editor_exclusive(game, target)

def handle_events(game):
    # Procesar QUIT antes que nada
    if _handle_quit(game):
        return

    # Capturar eventos
    events = pygame.event.get()

    # Si el chat está abierto, enrutar todos los eventos al controlador de chat
    if _handle_chat_open(game, events):
        return
    # Pre-despacho: overlay y minimapa
    overlay, consumed_idx = _overlay_consume(game, events)
    events = _filter_minimap_events(game, events)

    # Priorizar consola: no propagar al gameplay si está abierta
    if _handle_console(game, events):
        return

    # Hot-reload (F1) debe funcionar SIEMPRE
    if _handle_hot_reload_anywhere(game, events):
        return

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
    if getattr(getattr(game, 'menu', None), 'show_menu', False):
        _handle_menu(game, events)
        return

    # Si el selector de clase está abierto
    if _handle_class_selector(game, events):
        return

    # Debug overlay ya pudo haber consumido algunos eventos arriba.

    # Toggles y acciones instantáneas
    if _handle_toggles(game, events, _close_all_editors, _open_editor_exclusive):
        return
    if _handle_interact_open(game, events):
        return

    # Edición delegada (items/particles/spells/FSM/entities/spawner/tiles/buildings/map)
    # Ya cubierta por _handle_active_editors más abajo

    # Si el editor de hechizos está activo, permitir MMB pan (pase al engine) y delegar sus eventos
    if hasattr(game, 'spells_editor') and game.spells_editor.model.visible:
        for event in events:
            game.spells_editor.handle_event(event)
        mmb_events = []
        for ev in events:
            if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and getattr(ev, 'button', None) == 2:
                mmb_events.append(ev)
            elif ev.type == pygame.MOUSEMOTION:
                try:
                    mmb_held = _is_mmb_held(ev)
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
                try:
                    mmb_held = _is_mmb_held(ev)
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
                        mmb_held = False
                        try:
                            mmb_held = _is_mmb_held(event)
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

    # Delegación a editores activos (incluye items/particles/spells/FSM/entities/spawner/tiles/buildings/map)
    if _handle_active_editors(game, events, overlay):
        return

    # Detección de clic en halo de NPC para abrir chat (consumir evento)
    consumed_idx = _consume_npc_halo_click(game, events, consumed_idx)

    # Filtrado UI para obtener remaining_events
    remaining_events = _build_remaining_events(game, events, consumed_idx)

    # Particles Editor: handle Add/Remove/Move on map
    pass_events = _process_particles_map_input(game, remaining_events, overlay)

    # Pass remaining (possibly filtered) events and diagnostics overlay to the engine input handler
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
        pass_events,
        diagnostics_overlay=overlay,
        particles_editor=getattr(game, 'particles_editor', None),
        spells_editor=getattr(game, 'spells_editor', None),
        item_editor=getattr(game, 'item_editor', None),
        fsm_visible=getattr(__import__('roguelike_engine.config.config', fromlist=['config']), 'DEBUG_ENTITIES', False),
    )
