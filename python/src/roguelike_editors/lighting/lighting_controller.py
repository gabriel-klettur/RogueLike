from __future__ import annotations

import pygame
from typing import Any

from .lighting_state import LightingEditorState
from .lighting_view import LightingEditorView
from .panels.day_time_panel.day_time_panel_state import DayTimePanelState
from .panels.day_time_panel.day_time_panel_view import DayTimePanelView
from .panels.day_time_panel.day_time_panel_controller import DayTimePanelController
from .panels.light_presets_panel.light_presets_panel_state import LightPresetsPanelState
from .panels.light_presets_panel.light_presets_panel_view import LightPresetsPanelView
from .panels.light_presets_panel.light_presets_panel_controller import LightPresetsPanelController
from .controller_parts import events as _events
from .controller_parts import mouse as _mouse
from .controller_parts import ui as _ui
from .controller_parts import render as _render

# Test-override friendly module attributes (legacy test compatibility)
try:
    from roguelike_engine.config.config_tiles import TILE_SIZE as _TS  # type: ignore
    TILE_SIZE = int(_TS)
except Exception:
    TILE_SIZE = 32

try:
    from roguelike_engine.config.map_config import global_map_settings as _GMS  # type: ignore
    global_map_settings = _GMS
except Exception:
    class _FallbackGMS:
        zone_offsets = {}

    global_map_settings = _FallbackGMS()


# Service function aliases for tests (monkeypatch-friendly)
try:
    from roguelike_editors.lighting.services.light_instances_service import (
        _load_presets as _svc__load_presets,
        load_light_instances as _svc_load_light_instances,
    )

    def _load_presets():  # type: ignore
        return _svc__load_presets()

    def load_light_instances():  # type: ignore
        return _svc_load_light_instances()
except Exception:
    def _load_presets():  # type: ignore
        return {}

    def load_light_instances():  # type: ignore
        return []


try:
    from roguelike_editors.lighting.services.light_instances_service import delete_instances as _svc_delete_instances  # type: ignore

    def delete_instances(ids):  # type: ignore
        return _svc_delete_instances(ids)
except Exception:
    def delete_instances(ids):  # type: ignore
        return 0

try:
    from roguelike_editors.lighting.services.light_instances_service import update_instance_position as _svc_update_pos  # type: ignore

    def update_instance_position(*a, **k):  # type: ignore
        return _svc_update_pos(*a, **k)
except Exception:
    def update_instance_position(*a, **k):  # type: ignore
        return None


class LightingEditorController:
    def __init__(self, font: pygame.font.Font | None = None) -> None:
        self.model = LightingEditorState()
        self.view = LightingEditorView(self.model, font=font)
        self.game: Any | None = None  # set by manager
        # DayTime Tools (delegated panel MVC)
        self.daytime_state = DayTimePanelState()
        self.daytime_view = DayTimePanelView(self.daytime_state, font=font)
        self.daytime_controller = DayTimePanelController(self.daytime_state)
        # Light Presets (delegated panel MVC)
        self.presets_state = LightPresetsPanelState()
        self.presets_view = LightPresetsPanelView(self.presets_state, font=font)
        self.presets_controller = LightPresetsPanelController(self.presets_state, editor_controller=self)

    def handle_event(self, event: pygame.event.Event) -> None:
        if not getattr(self.model, 'visible', False):
            return
        if _events.handle_event(self, event):
            return
        if event.type == pygame.MOUSEBUTTONDOWN and _mouse.on_mousebuttondown(self, event):
            return
        if _mouse.on_mousemotion_drag_instance(self, event):
            return
        if _mouse.on_mousebuttonup_stopdrag(self, event):
            return
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            _ui.click_ui(self, event.pos)


    def _on_click(self, pos: tuple[int, int]) -> None:
        _ui.click_ui(self, pos)


    def render(self, screen: pygame.Surface) -> None:
        if not getattr(self.model, 'visible', False):
            return
        try:
            from roguelike_engine.rendering.lighting import get_global_lighting
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            lm = get_global_lighting()
            lights_on = bool(lm.enabled)
            ambient_on = bool(get_global_daynight().enabled)
            occlusion_on = bool(lm.tile_occlusion_enabled())
            shadows_on = bool(lm.shadow_polygons_enabled())
        except Exception:
            lights_on = False
            ambient_on = False
            occlusion_on = False
            shadows_on = False
        _render.render_instances_overlay(self, screen)
        self.view.render(screen, ambient_on=ambient_on, lights_on=lights_on, occlusion_on=occlusion_on, shadows_on=shadows_on)
        if isinstance(getattr(self.model, '_panel_rect', None), pygame.Rect):
            self.daytime_view.render(screen, anchor_rect=self.model._panel_rect, row_h=self.model.row_h)
        if isinstance(getattr(self.daytime_state, 'panel_rect', None), pygame.Rect):
            self.presets_view.render(screen, anchor_rect=self.daytime_state.panel_rect, row_h=self.model.row_h)


