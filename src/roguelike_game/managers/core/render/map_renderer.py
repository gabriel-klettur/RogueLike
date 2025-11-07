from __future__ import annotations

import logging
from typing import List
from roguelike_engine.config.map_config import global_map_settings
import json
from pathlib import Path
import logging


def render_map(manager, camera, screen, map_) -> List[object]:
    """Render tile map honoring editors' visibility and collision modes.

    Returns a list of dirty rects. All stateful caches live on 'manager' to
    preserve previous behavior and test hooks.
    """
    dirty_rects: list = []
    # Collision-only mode takes precedence over any early guard
    try:
        co_mode = (
            manager.tiles_editor.editor_state.active
            and manager.tiles_editor.editor_state.toolbar_state.show_collisions
            and not manager.tiles_editor.editor_state.toolbar_state.show_collisions_overlay
        )
        if co_mode:
            dirty = manager._render_collisions(screen, camera, map_)
            try:
                manager._last_collision_only = co_mode
            except Exception:
                pass
            return dirty
    except Exception:
        pass
    # Early tiles editor invalidation: ensure visible_layers changes invalidate cache even with minimal map objects
    try:
        editor_state = getattr(manager.tiles_editor, "editor_state", None)
        if editor_state and editor_state.active:
            visible = editor_state.toolbar_state.visible_layers
            if visible != getattr(manager, "_last_visible_layers", None):
                try:
                    map_.view.invalidate_cache()
                finally:
                    try:
                        manager._last_visible_layers = visible.copy()
                    except Exception:
                        manager._last_visible_layers = dict(visible)
    except Exception:
        pass
    # Early guard for blank worlds: if zones.json has no user-defined zones,
    # and there are no overlay files (or only the sentinel), skip drawing to avoid base fallback visuals
    try:
        te_active = bool(getattr(getattr(manager, 'tiles_editor', None), 'editor_state', None) and manager.tiles_editor.editor_state.active)
        if not te_active and getattr(global_map_settings, 'is_blank_world', None):
            if global_map_settings.is_blank_world():
                odir = getattr(global_map_settings, 'overlays_dir', None)
                files = list(Path(odir).glob('*.overlay.json')) if odir else []
                if not files:
                    # Return full-screen rect so the black clear is presented
                    try:
                        return [screen.get_rect()]
                    except Exception:
                        return dirty_rects
                # If only sentinel overlays are present, also skip drawing
                try:
                    # Normalize names: 'no zone.overlay.json' -> 'no zone'
                    stems = {
                        (s[:-8] if s.endswith('.overlay') else s)
                        for s in (f.stem.lower().replace('_', ' ') for f in files)
                    }
                    if stems.issubset({'no zone', 'no-zone', 'no_zone'}):
                        try:
                            return [screen.get_rect()]
                        except Exception:
                            return dirty_rects
                except Exception:
                    pass
    except Exception:
        pass
    # Defensive guard: minimal map objects (no 'matrix') should not call view.render
    try:
        if not te_active and not hasattr(map_, 'matrix'):
            try:
                return [screen.get_rect()]
            except Exception:
                return dirty_rects
    except Exception:
        pass
    # Hard guard: if overlays-driven AND (no overlay files) AND (no user-defined zones), render nothing
    try:
        if not te_active and getattr(global_map_settings, 'use_zones_json', False):
            odir = global_map_settings.overlays_dir
            if odir and list(Path(odir).glob('*.overlay.json')) == []:
                # Count user-defined zones from ZONES_INDEX (exclude sentinels)
                user_keys = []
                try:
                    z = getattr(global_map_settings, 'ZONES_INDEX', None)
                    if z and z.exists():
                        txt = z.read_text(encoding='utf-8').strip()
                        if txt:
                            data = json.loads(txt)
                            if isinstance(data, dict):
                                user_keys = [k for k in data.keys() if str(k).lower() not in ('no zone', 'no-zone')]
                        else:
                            user_keys = []
                    else:
                        user_keys = []
                except Exception:
                    user_keys = []
                if len(user_keys) == 0:
                    try:
                        last = getattr(manager, "_last_no_overlay_dir", None)
                        if last != odir:
                            logging.getLogger(__name__).info(
                                "[Render][Map] overlays-driven: no overlay files in %s -> skip draw", odir
                            )
                            try:
                                manager._last_no_overlay_dir = odir
                            except Exception:
                                pass
                    except Exception:
                        pass
                    try:
                        return [screen.get_rect()]
                    except Exception:
                        return dirty_rects
            try:
                if getattr(manager, "_last_no_overlay_dir", None) is not None:
                    manager._last_no_overlay_dir = None
            except Exception:
                pass
    except Exception:
        pass

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
        # Filter both tiles_by_layer (Tile grids) and layers (code grids) to honor visibility
        orig_tiles_by_layer = map_.tiles_by_layer
        filtered_tiles_by_layer = {layer: tiles for layer, tiles in orig_tiles_by_layer.items() if visible.get(layer, True)}
        orig_layers = map_.layers
        filtered_layers = {layer: orig_layers[layer] for layer in orig_layers if visible.get(layer, True)}
        map_.tiles_by_layer = filtered_tiles_by_layer
        map_.layers = filtered_layers
        try:
            dirty_rects = map_.view.render(screen, camera, map_)
        finally:
            map_.tiles_by_layer = orig_tiles_by_layer
            map_.layers = orig_layers
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
