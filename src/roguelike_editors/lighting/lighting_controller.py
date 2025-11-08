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
        self.presets_controller = LightPresetsPanelController(self.presets_state)

    def handle_event(self, event: pygame.event.Event) -> None:
        if not getattr(self.model, 'visible', False):
            return
        # Cancel spawn mode with ESC
        if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
            try:
                self.model.spawn_mode = False
            except Exception:
                pass
            return
        # Mouse wheel scrolling within panel
        if event.type == pygame.MOUSEWHEEL:
            st = self.model
            try:
                so = int(getattr(st, 'scroll_offset', 0))
                vp = getattr(st, '_viewport_rect', None)
                ch = int(getattr(st, '_content_height', 0))
                if isinstance(vp, pygame.Rect) and ch > vp.height:
                    step = st.row_h
                    # pygame: event.y > 0 means scroll up
                    so = max(0, min(ch - vp.height, so - event.y * step))
                    st.scroll_offset = so
            except Exception:
                pass
            return
        # Scrollbar dragging and track clicks
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            st = self.model
            thumb = getattr(st, '_scrollbar_thumb', None)
            track = getattr(st, '_scrollbar_track', None)
            if isinstance(thumb, pygame.Rect) and thumb.collidepoint(event.pos):
                st._dragging_scroll = True
                st._drag_start_y = event.pos[1]
                st._drag_start_offset = int(getattr(st, 'scroll_offset', 0))
                return
            if isinstance(track, pygame.Rect) and track.collidepoint(event.pos) and (not isinstance(thumb, pygame.Rect) or not thumb.collidepoint(event.pos)):
                try:
                    vp = getattr(st, '_viewport_rect', None)
                    ch = int(getattr(st, '_content_height', 0))
                    if isinstance(vp, pygame.Rect) and ch > vp.height:
                        page = max(st.row_h, vp.height - st.row_h)
                        so = int(getattr(st, 'scroll_offset', 0))
                        if event.pos[1] < thumb.top:
                            so = max(0, so - page)
                        else:
                            so = min(ch - vp.height, so + page)
                        st.scroll_offset = so
                except Exception:
                    pass
                return
        if event.type == pygame.MOUSEMOTION and bool(getattr(self.model, '_dragging_scroll', False)):
            st = self.model
            try:
                track = getattr(st, '_scrollbar_track', None)
                thumb = getattr(st, '_scrollbar_thumb', None)
                vp = getattr(st, '_viewport_rect', None)
                ch = int(getattr(st, '_content_height', 0))
                if isinstance(track, pygame.Rect) and isinstance(thumb, pygame.Rect) and isinstance(vp, pygame.Rect) and ch > vp.height:
                    dy = int(event.pos[1] - int(st._drag_start_y or 0))
                    denom = max(1, track.height - thumb.height)
                    frac = max(0.0, min(1.0, float(dy) / float(denom)))
                    max_off = ch - vp.height
                    base_off = int(st._drag_start_offset or 0)
                    st.scroll_offset = max(0, min(max_off, base_off + int(frac * max_off)))
            except Exception:
                pass
            return
        if event.type == pygame.MOUSEBUTTONUP and getattr(self.model, '_dragging_scroll', False):
            self.model._dragging_scroll = False
            self.model._drag_start_y = None
            self.model._drag_start_offset = None
            return
        if event.type == pygame.MOUSEBUTTONDOWN:
            # Left click: either UI button interaction or map placement in spawn mode
            if getattr(event, 'button', None) == 1:
                # If spawn mode is active and click is outside the panel -> place light on map
                st = self.model
                pan_left = getattr(st, '_panel_rect', None)
                pan_day = getattr(self.daytime_state, 'panel_rect', None)
                pan_preset = getattr(self.presets_state, 'panel_rect', None)
                # If click is inside the DayTime panel, delegate and return
                if isinstance(pan_day, pygame.Rect) and pan_day.collidepoint(event.pos):
                    try:
                        from .panels.day_time_panel.day_time_panel_events import DayTimePanelEventHandler
                        DayTimePanelEventHandler.handle_event(self.daytime_controller, event)
                    except Exception:
                        pass
                    return
                # If click is inside the Presets panel, delegate and return
                if isinstance(pan_preset, pygame.Rect) and pan_preset.collidepoint(event.pos):
                    try:
                        from .panels.light_presets_panel.light_presets_panel_events import LightPresetsPanelEventHandler
                        LightPresetsPanelEventHandler.handle_event(self.presets_controller, event)
                    except Exception:
                        pass
                    return
                outside_left = not (isinstance(pan_left, pygame.Rect) and pan_left.collidepoint(event.pos))
                outside_day = not (isinstance(pan_day, pygame.Rect) and pan_day.collidepoint(event.pos))
                outside_preset = not (isinstance(pan_preset, pygame.Rect) and pan_preset.collidepoint(event.pos))
                if getattr(st, 'spawn_mode', False) and outside_left and outside_day and outside_preset:
                    # Ensure point lights manager is enabled so debug lights are visible
                    try:
                        from roguelike_engine.rendering.lighting import get_global_lighting
                        get_global_lighting().set_enabled(True)
                    except Exception:
                        pass
                    self._spawn_at_screen(event.pos)
                    # Single-shot exits spawn mode automatically
                    try:
                        if bool(getattr(self.presets_state, 'spawn_single_shot', False)):
                            st.spawn_mode = False
                    except Exception:
                        pass
                    return
                # Otherwise, treat as UI click
                self._on_click(event.pos)

    def _on_click(self, pos: tuple[int, int]) -> None:
        try:
            from roguelike_engine.rendering.lighting import get_global_lighting
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
        except Exception:
            return
        st = self.model
        x, y = pos
        # Preset UI interactions are delegated to LightPresetsPanelController; nothing to handle here
        # Toggle Ambient
        if isinstance(st._btn_ambient, pygame.Rect) and st._btn_ambient.collidepoint(x, y):
            try:
                dn = get_global_daynight()
                dn.enabled = not dn.enabled
            except Exception:
                pass
            return
        # Toggle Point Lights
        if isinstance(st._btn_lights, pygame.Rect) and st._btn_lights.collidepoint(x, y):
            try:
                lm = get_global_lighting()
                lm.set_enabled(not lm.enabled)
            except Exception:
                pass
            return
        # Toggle Spawn Debug Light mode (map click placement)
        if isinstance(st._btn_spawn, pygame.Rect) and st._btn_spawn.collidepoint(x, y):
            st.spawn_mode = not bool(getattr(st, 'spawn_mode', False))
            if st.spawn_mode:
                try:
                    from roguelike_engine.rendering.lighting import get_global_lighting
                    lm = get_global_lighting()
                    lm.set_enabled(True)
                    if not lm.should_render():
                        lm.set_quality('lights_low')
                except Exception:
                    pass
            return
        # Clear Debug Lights
        if isinstance(st._btn_clear, pygame.Rect) and st._btn_clear.collidepoint(x, y):
            try:
                get_global_lighting().clear_debug_lights()
            except Exception:
                pass
            return
        # Toggle Tile Occlusion
        if hasattr(st, '_btn_occlusion') and isinstance(st._btn_occlusion, pygame.Rect) and st._btn_occlusion.collidepoint(x, y):
            try:
                lm = get_global_lighting()
                lm.set_tile_occlusion(not lm.tile_occlusion_enabled())
            except Exception:
                pass
            return
        # Toggle Shadows (stub)
        if hasattr(st, '_btn_shadows') and isinstance(st._btn_shadows, pygame.Rect) and st._btn_shadows.collidepoint(x, y):
            try:
                from roguelike_engine.rendering.lighting import get_global_lighting
                lm = get_global_lighting()
                lm.set_shadow_polygons(not lm.shadow_polygons_enabled())
            except Exception:
                pass
            return
        # Preset buttons and steppers are handled by LightPresetsPanel; no-op here
        # Manager tunables (quality/limits)
        try:
            from roguelike_engine.rendering.lighting import get_global_lighting
            lm = get_global_lighting()
        except Exception:
            lm = None
        if lm is not None:
            # Low-res scale
            if hasattr(st, '_btn_lrs_minus') and isinstance(st._btn_lrs_minus, pygame.Rect) and st._btn_lrs_minus.collidepoint(x, y):
                lm.set_low_res_scale(max(1, lm.current_low_res_scale() - 1)); return
            if hasattr(st, '_btn_lrs_plus') and isinstance(st._btn_lrs_plus, pygame.Rect) and st._btn_lrs_plus.collidepoint(x, y):
                lm.set_low_res_scale(min(8, lm.current_low_res_scale() + 1)); return
            # Max lights
            if hasattr(st, '_btn_ml_minus') and isinstance(st._btn_ml_minus, pygame.Rect) and st._btn_ml_minus.collidepoint(x, y):
                lm.set_max_lights(max(0, lm.current_max_lights() - 1)); return
            if hasattr(st, '_btn_ml_plus') and isinstance(st._btn_ml_plus, pygame.Rect) and st._btn_ml_plus.collidepoint(x, y):
                lm.set_max_lights(min(256, lm.current_max_lights() + 1)); return
            # Max radius
            if hasattr(st, '_btn_mr_minus') and isinstance(st._btn_mr_minus, pygame.Rect) and st._btn_mr_minus.collidepoint(x, y):
                lm.set_max_radius(max(16, lm.current_max_radius() - 8)); return
            if hasattr(st, '_btn_mr_plus') and isinstance(st._btn_mr_plus, pygame.Rect) and st._btn_mr_plus.collidepoint(x, y):
                lm.set_max_radius(min(2048, lm.current_max_radius() + 8)); return
            # Shadow hero count
            if hasattr(st, '_btn_sh_hero_minus') and isinstance(st._btn_sh_hero_minus, pygame.Rect) and st._btn_sh_hero_minus.collidepoint(x, y):
                lm.set_shadow_hero_count(max(0, lm.get_shadow_hero_count() - 1)); return
            if hasattr(st, '_btn_sh_hero_plus') and isinstance(st._btn_sh_hero_plus, pygame.Rect) and st._btn_sh_hero_plus.collidepoint(x, y):
                lm.set_shadow_hero_count(min(2, lm.get_shadow_hero_count() + 1)); return
            # Shadow rays
            if hasattr(st, '_btn_sh_rays_minus') and isinstance(st._btn_sh_rays_minus, pygame.Rect) and st._btn_sh_rays_minus.collidepoint(x, y):
                lm.set_shadow_rays(max(8, lm.get_shadow_rays() - 8)); return
            if hasattr(st, '_btn_sh_rays_plus') and isinstance(st._btn_sh_rays_plus, pygame.Rect) and st._btn_sh_rays_plus.collidepoint(x, y):
                lm.set_shadow_rays(min(256, lm.get_shadow_rays() + 8)); return
        # DayTime Tools and Presets are delegated to their panels; no color handling here

    def render(self, screen: pygame.Surface) -> None:
        if not getattr(self.model, 'visible', False):
            return
        # Read current states
        try:
            from roguelike_engine.rendering.lighting import get_global_lighting
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            lm = get_global_lighting()
            lights_on = bool(lm.enabled)
            ambient_on = bool(get_global_daynight().enabled)
            occlusion_on = bool(lm.tile_occlusion_enabled())
        except Exception:
            lights_on = False
            ambient_on = False
            occlusion_on = False
        try:
            shadows_on = bool(lm.shadow_polygons_enabled())
        except Exception:
            shadows_on = False
        self.view.render(screen, ambient_on=ambient_on, lights_on=lights_on, occlusion_on=occlusion_on, shadows_on=shadows_on)
        # Render DayTime Tools panel anchored to the main panel
        if isinstance(getattr(self.model, '_panel_rect', None), pygame.Rect):
            self.daytime_view.render(screen, anchor_rect=self.model._panel_rect, row_h=self.model.row_h)
        # Render Light Presets panel anchored to the DayTime Tools panel
        if isinstance(getattr(self.daytime_state, 'panel_rect', None), pygame.Rect):
            self.presets_view.render(screen, anchor_rect=self.daytime_state.panel_rect, row_h=self.model.row_h)

    def _spawn_at_screen(self, pos: tuple[int, int]) -> None:
        """Convert screen pos to world and spawn a debug light."""
        try:
            from roguelike_engine.rendering.lighting import get_global_lighting
            from roguelike_engine.rendering.lighting.light_types import Light
            mx, my = int(pos[0]), int(pos[1])
            cam = getattr(self.game, 'camera', None)
            if cam is not None:
                z = float(getattr(cam, 'zoom', 1.0) or 1.0)
                ox = round(getattr(cam, 'offset_x', 0.0) * z) / z
                oy = round(getattr(cam, 'offset_y', 0.0) * z) / z
                wx = (mx / z) + ox
                wy = (my / z) + oy
            else:
                wx, wy = float(mx), float(my)
            stp = self.presets_state
            lm = get_global_lighting()
            # Ensure lights system is visible
            lm.set_enabled(True)
            if not lm.should_render():
                try:
                    lm.set_quality('lights_low')
                except Exception:
                    pass
            lm.add(
                Light(
                    x=wx,
                    y=wy,
                    radius=int(getattr(stp, 'spawn_radius', 160)),
                    color=tuple(getattr(stp, 'spawn_color', (255, 200, 140))),
                    intensity=float(getattr(stp, 'spawn_intensity', 1.0)),
                    falloff=float(getattr(stp, 'spawn_falloff', 2.0)),
                    flicker_amp=float(getattr(stp, 'spawn_flicker_amp', 0.15)),
                    flicker_speed=float(getattr(stp, 'spawn_flicker_speed', 2.5)),
                    center_scale=float(getattr(stp, 'spawn_center_scale', 1.0)),
                )
            )
        except Exception:
            pass
