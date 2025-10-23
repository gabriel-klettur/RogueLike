from __future__ import annotations

import sys
from types import SimpleNamespace

from roguelike_engine.utils.benchmark import benchmark
from .pipeline_helpers import (
    log_tile_editor_debug,
    render_ecs_trail,
    should_render_minimap,
    render_spell_debug_overlays,
)


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

    def _step_crosshair():
        from roguelike_engine.utils.mouse import draw_mouse_crosshair
        draw_mouse_crosshair(screen, camera)

    def _step_menu():
        manager._render_menu(screen, menu)

    def _step_minimap():
        if should_render_minimap(manager, state, menu):
            manager._render_minimap(screen)

    def _step_editors():
        manager._render_editors()

    steps = [
        ("3.0. init_and_cleaning", _step_init_and_cleaning),
        ("3.1. map", _step_map),
        ("3.5. ecs_trail", _step_ecs_trail),
        ("3.2. z_entities", _step_z_entities),
        ("3.35. attack_telegraphs", _step_attack_telegraphs),
        ("3.4. tile_editor", _step_tile_editor),
        ("3.55. spell_debug", _step_spell_debug),
        ("3.6. crosshair", _step_crosshair),
        ("3.7. menu", _step_menu),
        ("3.8. minimap", _step_minimap),
        ("3.11. editors", _step_editors),
    ]

    for key, fn in steps:
        @benchmark(perf_log, key)
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
