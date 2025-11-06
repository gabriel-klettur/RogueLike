from __future__ import annotations

import logging
import time
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


def render_game_clock(manager, screen: pygame.Surface) -> None:
    """Render a small clock HUD under the minimap showing real and game time.

    Draws only when the minimap is visible to respect layout.
    """
    try:
        if not should_render_minimap(manager, manager._last_state, None):
            return
        # Get minimap rect position
        try:
            mm_rect = manager.minimap.get_rect(screen)
        except Exception:
            return
        # Build strings: real time and game time
        try:
            real_str = time.strftime("%H:%M:%S", time.localtime())
        except Exception:
            real_str = "--:--:--"
        try:
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            dn = get_global_daynight()
            gh, gm, gs = dn.get_game_time_hms()
            phase = dn.get_phase()
            game_str = f"{gh:02d}:{gm:02d}:{gs:02d} ({phase})"
        except Exception:
            game_str = "--:--:--"
        # Compose surface
        pad = 8
        gap = 6
        try:
            font = pygame.font.SysFont("consolas", 16)
        except Exception:
            font = pygame.font.Font(None, 16)
        t1 = font.render(f"Real: {real_str}", True, (235, 235, 240))
        t2 = font.render(f"Game: {game_str}", True, (235, 235, 240))
        w = max(t1.get_width(), t2.get_width()) + pad * 2
        h = t1.get_height() + t2.get_height() + pad * 2 + gap
        box = pygame.Surface((w, h), pygame.SRCALPHA)
        box.fill((20, 20, 28, 210))
        # Border
        pygame.draw.rect(box, (180, 180, 200), box.get_rect(), width=1)
        # Blit texts
        box.blit(t1, (pad, pad))
        box.blit(t2, (pad, pad + t1.get_height() + gap))
        # Position under minimap
        sw, sh = screen.get_size()
        # Anchor to right edge of minimap, prefer below; clamp to screen; if overflow bottom, place above
        px = mm_rect.right - w
        py = mm_rect.bottom + 8
        # If would overflow bottom, move above
        if py + h > sh - 4:
            py = mm_rect.top - h - 8
        # Clamp to screen with small margins
        px = max(4, min(px, sw - w - 4))
        py = max(4, min(py, sh - h - 4))
        dest = (px, py)
        screen.blit(box, dest)
        # Register dirty rect
        try:
            manager._dirty_rects.append(pygame.Rect(dest, (w, h)))
        except Exception:
            pass
    except Exception:
        # Silent failure: HUD is optional
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
    try:
        world = manager.ecs.ecs_world
        # Telegraph cones
        if toggles.get('telegraph', True):
            if getattr(manager, "_telegraph_render_system", None) is None:
                from roguelike_game.ecs.systems.rendering.telegraph_render_system import TelegraphRenderSystem
                manager._telegraph_render_system = TelegraphRenderSystem(perf_log=None)
            manager._telegraph_render_system.update(world, screen, camera)
        # Wind-up collider outlines
        if toggles.get('windup_outline', True):
            if getattr(manager, "_windup_outline_render_system", None) is None:
                from roguelike_game.ecs.systems.rendering.windup_outline_render_system import WindupOutlineRenderSystem
                manager._windup_outline_render_system = WindupOutlineRenderSystem(perf_log=None)
            manager._windup_outline_render_system.update(world, screen, camera)
    except Exception:
        # Do not disrupt main render if telegraph rendering fails
        pass


def should_render_hud_widget(widget_id: str, manager, state, menu) -> bool:
    """Unified HUD visibility policy.

    Minimal implementation leveraging should_render_minimap for modal/editor checks,
    plus a world-level `suppress_hud` flag.
    """
    try:
        world = getattr(manager.ecs, 'ecs_world', None)
        if world is not None and bool(getattr(world, 'suppress_hud', False)):
            return False
    except Exception:
        pass
    wid = (widget_id or '').lower()
    if wid in ('minimap', 'clock'):
        return should_render_minimap(manager, state, menu)
    # For grid and other HUD widgets, reuse minimap rules as baseline
    if wid in ('grid', 'xp', 'hpmp', 'target', 'toasts'):
        return should_render_minimap(manager, state, menu)
    # Default: visible unless suppressed by minimap rules
    return should_render_minimap(manager, state, menu)
