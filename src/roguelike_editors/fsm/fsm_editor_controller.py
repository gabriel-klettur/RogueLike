"""FSM Editor - Main Controller (skeleton)

Orchestrates panels (title, toolbar, sets, graph, properties), global state,
persistence hooks, history, and runtime reload bridge.
"""
from __future__ import annotations
from typing import Optional
import pygame

from .fsm_toolbar.fsm_toolbar_controller import FsmToolbarController
from .fsm_sets_panel.fsm_sets_panel_controller import FsmSetsPanelController
from .fsm_assigment_animations.fsm_assigment_animations_controller import (
    FsmAssigmentAnimationsController,
)
from .fsm_assigment_entities.fsm_assigment_entities_controller import (
    FsmAssigmentEntitiesController,
)
from .fsm_graph_panel.fsm_graph_panel_controller import FsmGraphPanelController
from .fsm_properties_panel.fsm_properties_panel_controller import FsmPropertiesPanelController
from .fsm_editor_view import FsmEditorView
from roguelike_editors.fsm.services.fsm_runtime_bridge import get_snapshot
# Tutorial panel (FSM)
from roguelike_editors.fsm.fsm_tutorial_panel import FsmTutorialPanelController
# Optional ids index helper (may not be present in some contexts)
try:  # pragma: no cover
    from roguelike_editors.fsm.services.fsm_runtime_bridge import get_set_ids as _get_set_ids
except Exception:  # pragma: no cover
    _get_set_ids = None
from roguelike_editors.fsm.services.editor_layout import (
    compute_panel_anchor_next_to_toolbar,
    compute_graph_canvas_anchor,
)
from roguelike_editors.fsm.services.graph_build import build_graph_from_set

class FsmEditorController:
    def __init__(self) -> None:
        # Visibility toggled by F12 elsewhere (FMSEventSpy/FMSController integration)
        self.visible: bool = False

        # Lazy-created/plugged submodules. Wired in later phases.
        self.title_controller = None
        self.toolbar_controller: Optional[FsmToolbarController] = FsmToolbarController()
        self.sets_panel_controller = FsmSetsPanelController()
        self.anim_panel_controller: Optional[FsmAssigmentAnimationsController] = FsmAssigmentAnimationsController()
        self.entities_panel_controller: Optional[FsmAssigmentEntitiesController] = FsmAssigmentEntitiesController()
        self.graph_panel_controller: Optional[FsmGraphPanelController] = FsmGraphPanelController()
        self.properties_panel_controller: Optional[FsmPropertiesPanelController] = FsmPropertiesPanelController()

        # View/Event handler can be split; keep placeholders for now
        self.view: Optional[FsmEditorView] = FsmEditorView()
        self.events = None
        # Tutorial panel controller (lazy UI overlay)
        self.tutorial_panel_controller: Optional[FsmTutorialPanelController] = FsmTutorialPanelController(self)
        # Allow toolbar events to access the owning editor (for tutorial toggle)
        try:
            setattr(self.toolbar_controller, 'owner_editor', self)
        except Exception:
            pass

    # --- Lifecycle ---
    def render(self, screen) -> None:
        if not self.visible:
            return
        # Toolbar (left column, anchored). Returns its rect.
        toolbar_rect = None
        if self.toolbar_controller:
            try:
                toolbar_rect = self.toolbar_controller.render(screen)
            except Exception:
                toolbar_rect = None
        # Toggle Sets Panel by active tool
        tool = None
        try:
            tool = getattr(self.toolbar_controller.model, 'active_tool', None) if self.toolbar_controller else None
        except Exception:
            tool = None
        sets_rect = None
        anim_rect = None
        entities_rect = None
        props_rect = None
        if self.sets_panel_controller:
            try:
                self.sets_panel_controller.model.visible = (tool == 'sets_list')
                if self.sets_panel_controller.model.visible:
                    # Populate items prioritizing snapshot (test monkeypatches this).
                    # If snapshot is empty or missing, fallback to ids index helper.
                    try:
                        snap = get_snapshot() or {}
                    except Exception:
                        snap = {}
                    set_ids = [s.get('id', '?') for s in snap.get('sets', [])]
                    if not set_ids and _get_set_ids is not None:
                        try:
                            set_ids = list(_get_set_ids() or [])
                        except Exception:
                            set_ids = []
                    self.sets_panel_controller.model.items = set_ids
                    # Anchor next to toolbar
                    anchor = compute_panel_anchor_next_to_toolbar(
                        toolbar_rect, screen.get_size(), (300, 240)
                    )
                    sets_rect = self.sets_panel_controller.render(screen, anchor=anchor)
            except Exception:
                pass
        # Animations assignment panel
        if self.anim_panel_controller:
            try:
                self.anim_panel_controller.model.visible = (tool == 'sets_animation_assignment')
                if self.anim_panel_controller.model.visible:
                    # Anchor next to toolbar
                    anchor = compute_panel_anchor_next_to_toolbar(
                        toolbar_rect, screen.get_size(), (420, 320)
                    )
                    anim_rect = self.anim_panel_controller.render(screen, anchor=anchor)
            except Exception:
                pass
        # Entities assignment panel
        if self.entities_panel_controller:
            try:
                self.entities_panel_controller.model.visible = (tool == 'sets_entities_assignment')
                if self.entities_panel_controller.model.visible:
                    anchor = compute_panel_anchor_next_to_toolbar(
                        toolbar_rect, screen.get_size(), (420, 320)
                    )
                    entities_rect = self.entities_panel_controller.render(screen, anchor=anchor)
            except Exception:
                pass
        # Properties panel
        if self.properties_panel_controller:
            try:
                self.properties_panel_controller.model.visible = (tool == 'set_properties')
                if self.properties_panel_controller.model.visible:
                    anchor = compute_panel_anchor_next_to_toolbar(
                        toolbar_rect, screen.get_size(), (540, 420)
                    )
                    props_rect = self.properties_panel_controller.render(screen, anchor=anchor)
            except Exception:
                pass
        # Graph panel to the right of the Sets panel when an item is selected
        if self.graph_panel_controller:
            try:
                # Only visible when sets tool active and a set selected
                selected_idx = None
                if self.sets_panel_controller and getattr(self.sets_panel_controller.model, 'visible', False):
                    selected_idx = getattr(self.sets_panel_controller.model, 'selected_index', None)
                self.graph_panel_controller.model.visible = (selected_idx is not None)
                if self.graph_panel_controller.model.visible and selected_idx is not None:
                    # Determine selected set id
                    items = getattr(self.sets_panel_controller.model, 'items', []) if self.sets_panel_controller else []
                    if 0 <= int(selected_idx) < len(items):
                        set_id = items[int(selected_idx)]
                    else:
                        set_id = None
                    # If changed, rebuild nodes/edges from snapshot
                    if set_id and self.graph_panel_controller.model.selected_set_id != set_id:
                        # Reset viewport defaults on set change; may be overridden by persisted viewport below
                        try:
                            self.graph_panel_controller.model.zoom = 1.0
                            self.graph_panel_controller.model.pan_x = 0.0
                            self.graph_panel_controller.model.pan_y = 0.0
                        except Exception:
                            pass
                        snap = get_snapshot()
                        set_def = None
                        try:
                            by_id = {s.get('id'): s for s in snap.get('sets', [])}
                            set_def = by_id.get(set_id)
                        except Exception:
                            set_def = None
                        nodes: list = []
                        edges: list = []
                        if set_def:
                            nodes, edges = build_graph_from_set(set_def, self.graph_panel_controller.model, canvas=(800, 520))
                        self.graph_panel_controller.model.selected_set_id = set_id
                        self.graph_panel_controller.model.nodes = nodes
                        self.graph_panel_controller.model.edges = edges
                    # Compute anchor to the right of sets panel
                    g_anchor = compute_graph_canvas_anchor(sets_rect, screen.get_size(), canvas_size=(800, 520))
                    self.graph_panel_controller.render(screen, anchor=g_anchor)
            except Exception:
                pass
        # Tutorial panel overlay: independent toggle via toolbar events
        try:
            if self.tutorial_panel_controller is not None and self.tutorial_panel_controller.is_active():
                self.tutorial_panel_controller.render(screen)
        except Exception:
            pass
        # Shared chrome (e.g., Title) rendered by view
        try:
            if self.view:
                self.view.render(self, screen)
        except Exception:
            pass
        return

    def handle_event(self, event) -> bool:
        if not self.visible:
            return False
        # Toolbar first, so drag/clicks don't leak to canvas
        if self.toolbar_controller and self.toolbar_controller.handle_event(event):
            return True
        # Tutorial panel events (ESC close, button clicks). Handle early so clicks over panel don't leak.
        try:
            if self.tutorial_panel_controller and self.tutorial_panel_controller.is_active():
                if self.tutorial_panel_controller.handle_event(event):
                    return True
        except Exception:
            pass
        # Sets panel events if visible
        try:
            if self.sets_panel_controller and getattr(self.sets_panel_controller.model, 'visible', False):
                if self.sets_panel_controller.handle_event(event):
                    return True
        except Exception:
            pass
        # Animations panel events if visible
        try:
            if self.anim_panel_controller and getattr(self.anim_panel_controller.model, 'visible', False):
                if self.anim_panel_controller.handle_event(event):
                    return True
        except Exception:
            pass
        # Entities panel events if visible
        try:
            if self.entities_panel_controller and getattr(self.entities_panel_controller.model, 'visible', False):
                if self.entities_panel_controller.handle_event(event):
                    return True
        except Exception:
            pass
        # Properties panel events if visible
        try:
            if self.properties_panel_controller and getattr(self.properties_panel_controller.model, 'visible', False):
                if self.properties_panel_controller.handle_event(event):
                    return True
        except Exception:
            pass
        # Graph panel events if visible
        try:
            if self.graph_panel_controller and getattr(self.graph_panel_controller.model, 'visible', False):
                if self.graph_panel_controller.handle_event(event):
                    return True
        except Exception:
            pass
        # TODO: delegate to graph/properties event handlers next
        return False

    # --- Visibility ---
    def toggle_visible(self, flag: Optional[bool] = None) -> None:
        if flag is None:
            self.visible = not self.visible
        else:
            self.visible = bool(flag)
        # Mirror gate flag used by debug overlay systems
        try:
            import roguelike_engine.config.config as config
            config.DEBUG_ENTITIES = self.visible
        except Exception:
            pass


__all__ = ["FsmEditorController"]
