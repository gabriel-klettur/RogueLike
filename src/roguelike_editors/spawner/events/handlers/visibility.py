from __future__ import annotations

import logging
import roguelike_engine.config.config as config


def toggle_visible(h) -> None:
    """Toggle visibility and manage side effects like input suppression and drag cancel.
    Delegates the logic formerly in SpawnerEditorEventHandler.toggle_visible
    """
    logger = logging.getLogger(__name__)
    h.model.visible = not h.model.visible
    ctx = h._make_ctx()
    world = ctx.world
    if not h.model.visible:
        # Stop any ongoing drags and clear hover/selection
        h.model.dragging = False
        h.model.dragging_eid = None
        h.model.hovered_eid = None
        try:
            h.model.resizing_visual = False
            h.model.resizing_visual_bid = None
        except AttributeError:
            logger.debug("toggle_visible: failed to reset resizing flags", exc_info=True)
        try:
            h.model.split_drag_active = False
            h.model.split_drag_bid = None
        except AttributeError:
            logger.debug("toggle_visible: failed to reset split-drag flags", exc_info=True)
        h.panning = False
        h.info_dragging = False
        h._drag_start_entry = None
        try:
            if world and hasattr(world, 'state'):
                setattr(world.state, 'spawner_editor_hovered_eid', None)
                setattr(world.state, 'spawner_selected_eid', None)
                setattr(world.state, 'spawner_input_suppressed', False)
                setattr(world.state, 'spawner_editor_active', False)
        except AttributeError:
            logger.debug("toggle_visible: failed to clear world.state flags", exc_info=True)
        # Clear split propagation key to avoid stale propagation next time
        try:
            setattr(h.model, '_split_propagation_key', None)
        except AttributeError:
            logger.debug("toggle_visible: failed to clear _split_propagation_key", exc_info=True)
        # Ensure global DEBUG_SPAWNER is disabled when the editor is hidden so
        # multi-visual preview and debug overlays revert to runtime behaviour.
        try:
            config.DEBUG_SPAWNER = False
        except Exception:
            logger.debug("toggle_visible: failed to reset DEBUG_SPAWNER on close", exc_info=True)
    else:
        # Mark editor as active globally
        try:
            if world and hasattr(world, 'state'):
                setattr(world.state, 'spawner_editor_active', True)
        except AttributeError:
            logger.debug("toggle_visible: failed to set world.state.spawner_editor_active", exc_info=True)
        # When the editor is made visible via internal UI actions (not only the
        # global toggle), keep DEBUG_SPAWNER in sync so preview/debug systems
        # behave consistently.
        try:
            config.DEBUG_SPAWNER = True
        except Exception:
            logger.debug("toggle_visible: failed to enable DEBUG_SPAWNER on open", exc_info=True)
        # Reveal all mapped visuals for the currently selected instance (if any)
        try:
            ip = getattr(h.controller, 'instance_properties', None)
            if ip is not None and hasattr(ip, 'visuals'):
                sel_inst = getattr(getattr(ip, 'model', None), 'selected_instance', None)
                if isinstance(sel_inst, dict):
                    ip.visuals.reveal_all_mapped_buildings()
        except Exception:
            logger.debug("toggle_visible: reveal_all_mapped_buildings failed on open", exc_info=True)
