from __future__ import annotations

import logging
from typing import List


def render_map(manager, camera, screen, map_) -> List[object]:
    """Render tile map honoring editors' visibility and collision modes.

    Returns a list of dirty rects. All stateful caches live on 'manager' to
    preserve previous behavior and test hooks.
    """
    dirty_rects: list = []

    # Map Editor: filter by visible_layers and invalidate cache only on change
    if manager.map_editor.editor_state.active:
        visible = manager.map_editor.editor_state.visible_layers
        if visible != manager._last_map_visible_layers:
            map_.view.invalidate_cache()
            manager._last_map_visible_layers = visible.copy()
            try:
                logger = logging.getLogger(__name__)
                vis_names = {getattr(k, "name", str(k)): v for k, v in visible.items()}
                logger.debug("[Render][MapEditor] visible_layers=%s", vis_names)
            except Exception:
                pass
        orig = map_.tiles_by_layer
        filtered = {layer: tiles for layer, tiles in orig.items() if visible.get(layer, True)}
        map_.tiles_by_layer = filtered
        try:
            dirty_rects = map_.view.render(screen, camera, map_)
        finally:
            map_.tiles_by_layer = orig
        return dirty_rects

    # Collision-only mode: render only collision grid (log only on toggle)
    co_mode = (
        manager.tiles_editor.editor_state.active
        and manager.tiles_editor.editor_state.toolbar_state.show_collisions
        and not manager.tiles_editor.editor_state.toolbar_state.show_collisions_overlay
    )
    last_co = getattr(manager, "_last_collision_only", None)
    if co_mode and co_mode != last_co:
        try:
            logging.getLogger(__name__).debug("[Render] Collision-only mode active -> skipping tile layers")
        except Exception:
            pass
    if co_mode:
        dirty = manager._render_collisions(screen, camera, map_)
        manager._last_collision_only = co_mode
        return dirty

    # Layer visibility filter when tile editor is active
    editor_state = getattr(manager.tiles_editor, "editor_state", None)
    if editor_state and editor_state.active:
        visible = editor_state.toolbar_state.visible_layers
        if visible != manager._last_visible_layers:
            map_.view.invalidate_cache()
            manager._last_visible_layers = visible.copy()
            try:
                logger = logging.getLogger(__name__)
                vis_names = {getattr(k, "name", str(k)): v for k, v in visible.items()}
                logger.debug("[Render][TilesEditor] visible_layers=%s", vis_names)
            except Exception:
                pass
        # Log current layer only when it changes
        try:
            current_layer = getattr(editor_state, "current_layer", None)
            if current_layer != manager._last_current_layer:
                logging.getLogger(__name__).debug("[Render][TilesEditor] current_layer=%s", current_layer)
                manager._last_current_layer = current_layer
        except Exception:
            pass
        # Temporarily filter map layers mapping for rendering
        orig_layers = map_.layers
        filtered_layers = {layer: orig_layers[layer] for layer in orig_layers if visible.get(layer, True)}
        map_.layers = filtered_layers
        dirty_rects = map_.view.render(screen, camera, map_)
        map_.layers = orig_layers
    else:
        dirty_rects = map_.view.render(screen, camera, map_)

    # Update collision-only toggle state when not in collision-only mode
    if getattr(manager, "_last_collision_only", None) != co_mode:
        manager._last_collision_only = co_mode

    # Overlay collision grid in overlay mode
    if (
        manager.tiles_editor.editor_state.active
        and manager.tiles_editor.editor_state.toolbar_state.show_collisions_overlay
    ):
        dirty2 = manager._render_collisions(screen, camera, map_)
        dirty_rects.extend(dirty2)

    return dirty_rects
