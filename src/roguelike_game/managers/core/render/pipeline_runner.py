from __future__ import annotations

import sys
from types import SimpleNamespace
import time

from roguelike_engine.utils.benchmark.benchmark_groups import BenchmarkGroup
from .pipeline_helpers import (
    log_tile_editor_debug,
    render_ecs_trail,
    should_render_minimap,
    should_render_hud_widget,
    render_spell_debug_overlays,
    render_game_clock,
)
try:
    from roguelike_ui.hud.orchestrator.hud_orchestrator import HudOrchestrator
except Exception:
    HudOrchestrator = None


def run_pipeline(manager, state, screen, camera, perf_log=None, menu=None, map=None, entities=None):
    """Execute the render pipeline steps with benchmarking and diagnostics overlay.

    Returns the manager's dirty rects list (unchanged behavior for callers).
    """
    # Sync latest references in case the game swapped map/entities (e.g., load/save)
    if map is not None:
        manager.map = map
    if entities is not None:
        manager.entities = entities

    manager._last_state = state

    # Sync Debug Tools -> building collision overlay flag
    try:
        import roguelike_engine.config.config as cfg
        toggles = getattr(getattr(manager, 'diagnostics_overlay', None), 'model', None)
        toggles = getattr(toggles, 'toolbar_toggles', {}) or {}
        cfg.DEBUG_BUILDING_COLLISION = bool(toggles.get('building_collision', True))
    except Exception:
        pass

    render_group = BenchmarkGroup(perf_log, "3")

    def _step_init_and_cleaning():
        screen.fill((0, 0, 0))
        manager._dirty_rects = []
        from roguelike_ui.ui_blocker import clear_blockers
        clear_blockers()

    def _step_map():
        log_tile_editor_debug(manager, camera)
        manager._render_map(camera, screen, map)

    def _step_ecs_trail():
        render_ecs_trail(manager, screen, camera)

    def _step_z_entities():
        # Skip entity rendering in collision-only mode
        if not (
            manager.tiles_editor.editor_state.active
            and manager.tiles_editor.editor_state.toolbar_state.show_collisions
            and not manager.tiles_editor.editor_state.toolbar_state.show_collisions_overlay
        ):
            manager._render_z_entities(state, camera, screen, entities)

    def _step_attack_telegraphs():
        from .pipeline_helpers import render_attack_telegraphs
        render_attack_telegraphs(manager, screen, camera)

    def _step_tile_editor():
        # Skip tile editor UI in collision-only mode
        if not (
            manager.tiles_editor.editor_state.active
            and manager.tiles_editor.editor_state.toolbar_state.show_collisions
            and not manager.tiles_editor.editor_state.toolbar_state.show_collisions_overlay
        ):
            manager._render_tile_editor_layer(state, screen, camera, map)

    def _step_spell_debug():
        render_spell_debug_overlays(manager, screen, camera, perf_log=perf_log)

    def _step_ambient_overlay():
        """Apply ambient day/night overlay before UI so HUD/menu are not dimmed."""
        try:
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            import pygame
            dn = get_global_daynight()
            if not dn.ambient_enabled():
                return
            t0 = time.perf_counter()
            overlay = dn.get_overlay_surface(screen.get_size())
            t1 = time.perf_counter()
            screen.blit(overlay, (0, 0), special_flags=pygame.BLEND_RGBA_MULT)
            t2 = time.perf_counter()
            try:
                if perf_log is not None:
                    perf_log.setdefault("3.565.a ambient_get_overlay", []).append(t1 - t0)
                    perf_log.setdefault("3.565.b ambient_blit", []).append(t2 - t1)
            except Exception:
                pass
        except Exception:
            # Do not disrupt main render if ambient overlay fails
            pass

    def _step_point_lights():
        """Compose low-res additive lightmap and apply it over the scene."""
        try:
            from roguelike_engine.rendering.lighting import get_global_lighting
            import pygame
            lm = get_global_lighting()
            if not lm.should_render():
                return
            sz = screen.get_size()
            t0 = time.perf_counter()
            lr = lm.compose_lightmap(sz, camera, map_manager=manager.map)
            t1 = time.perf_counter()
            if lr is None:
                return
            scaled = lm.get_scaled(sz)
            if scaled is None:
                return
            screen.blit(scaled, (0, 0), special_flags=pygame.BLEND_RGBA_ADD)
            t2 = time.perf_counter()
            try:
                if perf_log is not None:
                    perf_log.setdefault("3.57.a lights_compose", []).append(t1 - t0)
                    perf_log.setdefault("3.57.b lights_blit", []).append(t2 - t1)
            except Exception:
                pass
        except Exception:
            # Keep rendering robust even if lighting fails
            pass

    def _step_crosshair():
        from roguelike_engine.utils.mouse import draw_mouse_crosshair
        draw_mouse_crosshair(screen, camera)

    def _step_menu():
        manager._render_menu(screen, menu)

    def _step_minimap():
        try:
            orch = getattr(manager, 'hud_orchestrator', None)
            if orch is not None and hasattr(orch, 'render_minimap'):
                orch.render_minimap(manager, screen, state=state, menu=menu)
                return
        except Exception:
            pass
        # Fallback to legacy behavior
        if should_render_minimap(manager, state, menu):
            manager._render_minimap(screen)

    def _step_clock():
        # Draw a small clock HUD under the minimap
        try:
            orch = getattr(manager, 'hud_orchestrator', None)
            if orch is not None and hasattr(orch, 'render_clock'):
                orch.render_clock(manager, screen, state=state, menu=menu)
                return
        except Exception:
            pass
        # Fallback to legacy behavior
        try:
            render_game_clock(manager, screen)
        except Exception:
            pass

    def _step_hud():
        """Render HUD orchestrator widgets after the clock (UI layer)."""
        try:
            orch = getattr(manager, 'hud_orchestrator', None)
            if orch is None:
                if HudOrchestrator is not None:
                    try:
                        orch = HudOrchestrator(minimap=manager.minimap, systems=None)
                        manager.hud_orchestrator = orch
                        # Also expose via ecs for update_manager hook
                        try:
                            setattr(manager.ecs, 'hud_orchestrator', orch)
                        except Exception:
                            pass
                    except Exception:
                        orch = None
            if orch is not None and should_render_hud_widget('grid', manager, state, menu):
                orch.render(screen)
        except Exception:
            # Never disrupt main render due to optional HUD
            pass
    def _step_editors():
        manager._render_editors()

    steps = [
        ("0. init_and_cleaning", _step_init_and_cleaning),
        ("1. map", _step_map),
        ("5. ecs_trail", _step_ecs_trail),
        ("2. z_entities", _step_z_entities),
        ("35. attack_telegraphs", _step_attack_telegraphs),
        ("4. tile_editor", _step_tile_editor),
        ("55. spell_debug", _step_spell_debug),
        ("565. ambient_overlay", _step_ambient_overlay),
        ("57. point_lights", _step_point_lights),
        ("6. crosshair", _step_crosshair),
        ("7. menu", _step_menu),
        ("8. minimap", _step_minimap),
        ("85. clock", _step_clock),
        ("87. hud_orchestrator", _step_hud),
        ("11. editors", _step_editors),
    ]

    for name, fn in steps:
        @render_group.bench(name)
        def _run(sfn=fn):
            sfn()
        _run()

    # Diagnostics overlay
    debug_entities = SimpleNamespace(player=manager.ecs.ecs_world.player_position)
    try:
        rm_mod = sys.modules.get('roguelike_game.managers.core.render_manager')
        rd = getattr(rm_mod, 'render_diagnostics_overlay', None)
    except Exception:
        rd = None
    if callable(rd):
        rd(manager.diagnostics_overlay, screen, state, camera, manager.map, debug_entities, show_borders=True)
    else:
        from roguelike_engine.diagnostics import render_diagnostics_overlay as _rd
        _rd(manager.diagnostics_overlay, screen, state, camera, manager.map, debug_entities, show_borders=True)

    # Expand area overlay
    manager._render_expand_area(manager._last_state)

    return manager._dirty_rects
