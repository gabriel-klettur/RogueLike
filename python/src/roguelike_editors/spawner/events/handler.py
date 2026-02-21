from __future__ import annotations

from typing import Optional
from types import SimpleNamespace
import pygame
import logging

from .types import EditorCtx
from .utils import safe_get_world, safe_get_camera

# Modular handlers
from .handlers import (
    handle_visuals_picker as h_visuals_picker,
    handle_mousedown_left as h_mousedown_left,
    handle_mousedown_right as h_mousedown_right,
    handle_mousebuttonup as h_mousebuttonup,
    handle_mousemotion as h_mousemotion,
    toggle_visible as h_toggle_visible,
    handle_keydown as h_handle_keydown,
)

from roguelike_editors.buildings.tools.split_z_tool.split_tool import SplitTool
from roguelike_editors.buildings.tools.z_tool.z_tool import ZTool


logger = logging.getLogger(__name__)


class SpawnerEditorEventHandler:
    """Minimal Spawner Editor event handler that delegates to modular event functions.

    - Builds a small EditorCtx (world/camera/controller/tool adapters)
    - Routes each event to split/selection/anchor/resize/confirmation functions
    - Maintains editor flags like visibility and input suppression
    """

    def __init__(self, controller: 'SpawnerEditorController'):
        self.controller = controller
        self.model = controller.model
        self.font = controller.font
        self.game = controller.game
        # Split-drag state sampled MOTION1 once per drag
        self._split_drag_first_logged: bool = False
        # Info overlay / panning flags kept for parity (can extend later)
        self.info_dragging: bool = False
        self.info_drag_offset: tuple[int, int] = (0, 0)
        self.panning: bool = False
        self.pan_start: tuple[int, int] = (0, 0)
        self.pan_offset_start: tuple[float, float] = (0.0, 0.0)
        # Snapshot used by zone confirmation flow
        self._drag_start_entry: Optional[dict] = None
        # Visual moving (RMB-drag) helpers
        self._moving_visual_delta_world: tuple[int, int] | None = None

        # Shared tools adapters (reuse Buildings Editor logic)
        try:
            self._split_adapter = SimpleNamespace(split_dragging=False, selected_building=None)
            self._split_tool = SplitTool(None, self._split_adapter)
        except (AttributeError, TypeError):
            self._split_adapter = SimpleNamespace(split_dragging=False, selected_building=None)
            self._split_tool = None
        try:
            self._z_adapter = SimpleNamespace(active_building=None)
            _z_state = getattr(controller, 'z_state', None)
            if _z_state is None or not hasattr(_z_state, 'set'):
                _z_state = SimpleNamespace(set=lambda *args, **kwargs: None)
            self._z_tool_bottom = ZTool(SimpleNamespace(z_state=_z_state), self._z_adapter, target="bottom")
            self._z_tool_top = ZTool(SimpleNamespace(z_state=_z_state), self._z_adapter, target="top")
        except (AttributeError, TypeError):
            self._z_adapter = SimpleNamespace(active_building=None)
            self._z_tool_bottom = None
            self._z_tool_top = None

    # Public API ---------------------------------------------------------------
    def set_game(self, game) -> None:
        self.game = game

    def _make_ctx(self) -> EditorCtx:
        world = safe_get_world(getattr(self, 'game', None))
        camera = safe_get_camera(getattr(self, 'game', None))
        return EditorCtx(
            controller=self.controller,
            model=self.model,
            game=self.game,
            world=world,
            camera=camera,
            split_tool=self._split_tool,
            split_adapter=self._split_adapter,
            logger=logger,
        )

    def toggle_visible(self) -> None:
        """Toggle visibility delegating to modular handler."""
        h_toggle_visible(self)

    # Orchestrated event dispatcher ------------------------------------------
    def handle_event(self, event: pygame.event.Event) -> bool:
        if not self.model.visible or not self.game:
            return False
        ctx = self._make_ctx()
        world, camera = ctx.world, ctx.camera
        if not world or not camera:
            return False

        # 1) Visuals Picker overlay has priority and blocks gameplay
        try:
            handled = h_visuals_picker(self, ctx, event)
            if handled:
                return True
        except Exception:
            logger.debug("handle_event: exception while routing to visuals picker", exc_info=True)

        # 2) Mouse button up events (split end, visual move end, anchor drop, resize finish)
        if event.type == pygame.MOUSEBUTTONUP:
            if h_mousebuttonup(self, ctx, event):
                return True

        # 3) Mouse motion events (split drag, hover, resize motion, anchor drag, move visual)
        if event.type == pygame.MOUSEMOTION:
            if h_mousemotion(self, ctx, event):
                return True

        # 4) Mouse button down events
        if event.type == pygame.MOUSEBUTTONDOWN:
            if getattr(event, 'button', None) == 1:
                if h_mousedown_left(self, ctx, event):
                    return True
            elif getattr(event, 'button', None) == 3:
                if h_mousedown_right(self, ctx, event):
                    return True

        # 5) Keydown confirmations (zone/delete)
        if event.type == pygame.KEYDOWN:
            if h_handle_keydown(self, ctx, event):
                return True

        return False

    # Note: size-reset helper moved to handlers.helpers.reset_selected_building_size
