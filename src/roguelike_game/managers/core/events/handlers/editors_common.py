import logging
import pygame
import roguelike_engine.config.config as config
from roguelike_editors.buildings.utils.save_buildings_to_json import save_buildings_split
from roguelike_engine.config.map_config import global_map_settings

logger = logging.getLogger(__name__)


def close_all_editors(game) -> None:
    """Close all editors, running their respective close side-effects."""
    try:
        sp = getattr(game, 'spawner_editor', None)
        if sp and getattr(getattr(sp, 'model', None), 'visible', False):
            sp.controller.toggle_visible()
    except Exception:
        pass
    try:
        config.DEBUG_SPAWNER = False
    except Exception:
        pass
    try:
        if getattr(getattr(game, 'spells_editor', None), 'model', None) and getattr(game.spells_editor.model, 'visible', False):
            game.spells_editor.model.visible = False
    except Exception:
        pass
    try:
        if getattr(getattr(game, 'particles_editor', None), 'model', None) and getattr(game.particles_editor.model, 'visible', False):
            game.particles_editor.model.visible = False
    except Exception:
        pass
    try:
        if getattr(getattr(game, 'entities_editor', None), 'model', None) and getattr(game.entities_editor.model, 'visible', False):
            game.entities_editor.model.visible = False
    except Exception:
        pass
    try:
        if getattr(getattr(game, 'inventory_editor', None), 'model', None) and getattr(game.inventory_editor.model, 'visible', False):
            game.inventory_editor.model.visible = False
            try:
                aq = game.ecs.ecs_world.components.setdefault('AudioEventQueue', [])
                aq.append({'type': 'play_sfx', 'sfx_id': 'inv_open', 'group': 'ui'})
            except Exception:
                pass
    except Exception:
        pass
    try:
        it = getattr(game, 'item_editor', None)
        if it and getattr(getattr(it, 'model', None), 'visible', False):
            it.hide()
    except Exception:
        pass
    try:
        config.DEBUG_ENTITIES = False
    except Exception:
        pass
    try:
        te = getattr(game, 'tiles_editor', None)
        if te and getattr(getattr(te, 'editor_state', None), 'active', False):
            # USAR toggle() para ejecutar limpieza al cerrar
            te.toggle()
    except Exception:
        pass
    try:
        be = getattr(game, 'buildings_editor', None)
        bm = getattr(game, 'buildings', None)
        if be and getattr(getattr(be, 'editor_state', None), 'active', False):
            try:
                if hasattr(be, 'colliders') and hasattr(be.colliders, 'events'):
                    be.colliders.events._save_collisions(bm.buildings, force=True)
            except Exception:
                pass
            try:
                save_buildings_split(
                    bm.buildings,
                    z_state=getattr(game.state, 'z_state', None),
                    zone_offsets=getattr(global_map_settings, 'zone_offsets', None),
                )
            except Exception:
                pass
            try:
                game.ecs.ecs_world.invalidate_spatial_index()
            except Exception:
                pass
            be.editor_state.active = False
            # Clear pending collider rebuilds to prevent ECS updates after closing
            try:
                be.editor_state.colliders_dirty = False
                be.editor_state.last_colliders_rebuild_ms = pygame.time.get_ticks()
            except Exception:
                pass
            try:
                be.editor_state.picker_active = False
            except Exception:
                pass
    except Exception:
        pass
    try:
        me = getattr(game, 'map_editor', None)
        if me and getattr(getattr(me, 'editor_state', None), 'active', False):
            me.editor_state.active = False
    except Exception:
        pass


def open_editor_exclusive(game, target: str) -> None:
    """Close all editors, then open exactly one target editor.

    target in {'spawner','spells','particles','entities','inventory','items','fsm','tiles','buildings','map'}
    """
    close_all_editors(game)
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
            # USAR toggle() para ejecutar limpieza de caches y recarga de mapa
            if not game.tiles_editor.editor_state.active:
                game.tiles_editor.toggle()
        except Exception:
            pass
    elif target == 'buildings':
        try:
            game.buildings_editor.editor_state.active = True
            game.buildings_editor.editor_state.picker_active = False
        except Exception:
            pass
    elif target == 'map':
        try:
            game.map_editor.editor_state.active = True
        except Exception:
            pass
