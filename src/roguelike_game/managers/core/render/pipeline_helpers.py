from __future__ import annotations

import logging
import pygame

import roguelike_engine.config.config as config


def log_tile_editor_debug(manager, camera) -> None:
    """Log tile editor state with throttling via cached key on manager."""
    try:
        es = getattr(manager.tiles_editor, "editor_state", None)
        tc = es.toolbar_state if es else None
        key = (
            bool(es and es.active),
            bool(getattr(tc, "show_collisions", False)),
            bool(getattr(tc, "show_collisions_overlay", False)),
            getattr(es, "current_tool", None),
            round(float(getattr(camera, "zoom", 1.0)), 2),
        )
        if key != manager._last_render_debug_key:
            logger = logging.getLogger(__name__)
            if key[0]:
                logger.debug(
                    "[Render] TileEditor active: collisions=%s overlay=%s tool=%s zoom=%.2f",
                    key[1], key[2], key[3], key[4]
                )
            else:
                logger.debug("[Render] TileEditor inactive; zoom=%.2f", key[4])
            manager._last_render_debug_key = key
    except Exception:
        pass


def render_ecs_trail(manager, screen: pygame.Surface, camera) -> None:
    """Render trail snapshots stored by ECS trail component, respecting toolbar toggle."""
    try:
        toggles = getattr(getattr(manager, 'diagnostics_overlay', None), 'model', None)
        toggles = getattr(toggles, 'toolbar_toggles', {}) or {}
    except Exception:
        toggles = {}
    if toggles and not toggles.get('trail', True):
        return
    for _eid, trail in manager.ecs.ecs_world.components.get("TrailComponent", {}).items():
        for snap in trail.snapshots:
            orig = snap.image
            zoom = camera.zoom
            if zoom != 1.0:
                w, h = orig.get_size()
                image_scaled = pygame.transform.scale(orig, (int(w * zoom), int(h * zoom)))
            else:
                image_scaled = orig
            screen.blit(image_scaled, camera.apply(snap.pos))


def should_render_minimap(manager, state, menu) -> bool:
    """Return True when the minimap should be drawn (no blocking editors/UI)."""
    spawner_editor_active = False
    try:
        w = manager.ecs.ecs_world
        spawner_editor_active = bool(getattr(getattr(w, "state", None), "spawner_editor_active", False))
    except Exception:
        spawner_editor_active = False
    return (
        not manager.tiles_editor.editor_state.active
        and not manager.buildings_editor.editor_state.active
        and not manager.map_editor.editor_state.active
        and not (hasattr(state, "entities_editor_state") and state.entities_editor_state.visible)
        and not (hasattr(state, "inventory_editor_state") and getattr(state.inventory_editor_state, "visible", False))
        and not (hasattr(state, "item_editor_state") and getattr(state.item_editor_state, "visible", False))
        and not getattr(state, "spells_editor_visible", False)
        and not getattr(state, "fsm_editor_visible", False)
        and not getattr(state, "class_selector_visible", False)
        and not (hasattr(state, "particles_editor_state") and getattr(state.particles_editor_state, "visible", False))
        and not (menu and getattr(menu, "show_menu", False))
        and not spawner_editor_active
    )


def render_spell_debug_overlays(manager, screen, camera, perf_log=None) -> None:
    """Lazy-create and render optional debug overlay systems if DEBUG is enabled."""
    if not getattr(config, "DEBUG", False):
        return
    # Read toolbar toggles (default True for all)
    try:
        toggles = getattr(getattr(manager, 'diagnostics_overlay', None), 'model', None)
        toggles = getattr(toggles, 'toolbar_toggles', {}) or {}
    except Exception:
        toggles = {}
    try:
        # Create systems lazily only when first needed by toggles
        if toggles.get('hitbox', True):
            if manager._hitbox_debug_system is None:
                from roguelike_game.ecs.systems.rendering.hitbox_debug_system import HitboxDebugSystem
                manager._hitbox_debug_system = HitboxDebugSystem(perf_log=perf_log)
        if toggles.get('spell_collision', True):
            if manager._spell_debug_system is None:
                from roguelike_game.ecs.systems.rendering.spell_collision_debug.spell_collision_debug_system import SpellCollisionDebugSystem
                manager._spell_debug_system = SpellCollisionDebugSystem(perf_log=perf_log)
        if toggles.get('patrol', True):
            if manager._patrol_debug_system is None:
                from roguelike_game.ecs.systems.rendering.patrol_debug_system import PatrolDebugSystem
                manager._patrol_debug_system = PatrolDebugSystem(perf_log=perf_log)
        if toggles.get('defend_area', True):
            if manager._defend_debug_system is None:
                from roguelike_game.ecs.systems.rendering.defend_area_debug_system import DefendAreaDebugSystem
                manager._defend_debug_system = DefendAreaDebugSystem(perf_log=perf_log)
        if toggles.get('npc_attack', True):
            if manager._npc_attack_debug_system is None:
                from roguelike_game.ecs.systems.rendering.npc_attack_debug_system import NpcAttackDebugSystem
                manager._npc_attack_debug_system = NpcAttackDebugSystem(perf_log=perf_log)
        world = manager.ecs.ecs_world
        if toggles.get('hitbox', True) and manager._hitbox_debug_system is not None:
            manager._hitbox_debug_system.update(world, screen, camera)
        if toggles.get('spell_collision', True) and manager._spell_debug_system is not None:
            manager._spell_debug_system.update(world, screen, camera)
        if toggles.get('npc_attack', True) and manager._npc_attack_debug_system is not None:
            manager._npc_attack_debug_system.update(world, screen, camera)
        if toggles.get('patrol', True) and manager._patrol_debug_system is not None:
            manager._patrol_debug_system.update(world, screen, camera)
        if toggles.get('defend_area', True) and manager._defend_debug_system is not None:
            manager._defend_debug_system.update(world, screen, camera)
    except Exception:
        # Never break main render due to optional debug overlays
        pass


def render_attack_telegraphs(manager, screen, camera) -> None:
    """Render semi-transparent cones for upcoming NPC attacks (TelegraphArc)."""
    # Respect toolbar toggle (default True)
    try:
        toggles = getattr(getattr(manager, 'diagnostics_overlay', None), 'model', None)
        toggles = getattr(toggles, 'toolbar_toggles', {}) or {}
    except Exception:
        toggles = {}
    if not toggles.get('telegraph', True):
        return
    try:
        if getattr(manager, "_telegraph_render_system", None) is None:
            from roguelike_game.ecs.systems.rendering.telegraph_render_system import TelegraphRenderSystem
            manager._telegraph_render_system = TelegraphRenderSystem(perf_log=None)
        world = manager.ecs.ecs_world
        manager._telegraph_render_system.update(world, screen, camera)
    except Exception:
        # Do not disrupt main render if telegraph rendering fails
        pass
