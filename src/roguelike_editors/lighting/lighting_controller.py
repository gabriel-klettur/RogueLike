from __future__ import annotations

import pygame
from typing import Any
import math
import random
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.lighting.services.light_instances_service import (
    load_light_instances,
    _load_presets,
    update_instance_position,
    delete_instances,
)

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
        self.presets_controller = LightPresetsPanelController(self.presets_state, editor_controller=self)

    def handle_event(self, event: pygame.event.Event) -> None:
        if not getattr(self.model, 'visible', False):
            return
        # Cancel spawn mode with ESC
        if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
            try:
                self.model.spawn_mode = False
                # Cancel dragging and selection
                if getattr(self.model, '_dragging_inst', False):
                    self.model._dragging_inst = False
                self.model.selected_light_id = None
                try:
                    self.model.selected_light_ids.clear()
                except Exception:
                    pass
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
                            # Keep Presets panel toggle in sync
                            setattr(self.presets_state, 'spawn_mode', False)
                    except Exception:
                        pass
                    return
                # Selection/drag start when overlay is visible and clicking on a light outline
                if bool(getattr(st, 'overlay_visible', True)) and outside_left and outside_day and outside_preset:
                    mx, my = int(event.pos[0]), int(event.pos[1])
                    # Compute camera params
                    cam = getattr(self.game, 'camera', None)
                    if cam is not None:
                        z = float(getattr(cam, 'zoom', 1.0) or 1.0)
                        ox = float(getattr(cam, 'offset_x', 0.0) or 0.0)
                        oy = float(getattr(cam, 'offset_y', 0.0) or 0.0)
                        # Pick first matching instance
                        hit_id = None
                        presets = _load_presets()
                        for e in (load_light_instances() or []):
                            try:
                                zone = str(e.get('zone') or 'no zone')
                                rel_x = int(e.get('rel_x') or 0)
                                rel_y = int(e.get('rel_y') or 0)
                                off_tx, off_ty = global_map_settings.zone_offsets.get(zone, (0, 0))
                                wx = int(off_tx) * TILE_SIZE + rel_x
                                wy = int(off_ty) * TILE_SIZE + rel_y
                                sx = int((wx - ox) * z)
                                sy = int((wy - oy) * z)
                                preset_id = str(e.get('preset_id') or '')
                                base = presets.get(preset_id, {}) if isinstance(presets, dict) else {}
                                params = dict(base)
                                ov = e.get('overrides') if isinstance(e, dict) else None
                                if isinstance(ov, dict):
                                    for k, v in ov.items():
                                        params[k] = v
                                radius = int(params.get('radius', 160))
                                rr = int(max(1, radius) * z)
                                dx, dy = mx - sx, my - sy
                                if dx * dx + dy * dy <= rr * rr:
                                    hit_id = int(e.get('id')) if e.get('id') is not None else None
                                    break
                            except Exception:
                                continue
                        if hit_id is not None:
                            # Multi-select with CTRL: toggle membership; else select single
                            ctrl = False
                            try:
                                mods = getattr(event, 'mod', 0) or pygame.key.get_mods()
                                ctrl = bool(mods & pygame.KMOD_CTRL)
                            except Exception:
                                ctrl = False
                            try:
                                sel_set = getattr(st, 'selected_light_ids', set())
                            except Exception:
                                sel_set = set()
                            if ctrl:
                                if hit_id in sel_set:
                                    try:
                                        sel_set.remove(hit_id)
                                    except Exception:
                                        pass
                                else:
                                    sel_set.add(hit_id)
                                st.selected_light_ids = sel_set
                                st.selected_light_id = hit_id  # focus moves to last
                                return
                            else:
                                st.selected_light_ids = {hit_id}
                                st.selected_light_id = hit_id
                                # Start dragging only for single selection
                                st._dragging_inst = True
                                st._drag_world_x = mx / z + ox
                                st._drag_world_y = my / z + oy
                                return
                # Otherwise, treat as UI click
                self._on_click(event.pos)
        # Dragging
        if event.type == pygame.MOUSEMOTION and bool(getattr(self.model, '_dragging_inst', False)):
            try:
                cam = getattr(self.game, 'camera', None)
                if cam is None:
                    return
                z = float(getattr(cam, 'zoom', 1.0) or 1.0)
                ox = float(getattr(cam, 'offset_x', 0.0) or 0.0)
                oy = float(getattr(cam, 'offset_y', 0.0) or 0.0)
                mx, my = int(getattr(event, 'pos', (0, 0))[0]), int(getattr(event, 'pos', (0, 0))[1])
                self.model._drag_world_x = mx / z + ox
                self.model._drag_world_y = my / z + oy
                # Live-move the corresponding persistent light in the manager (visual feedback)
                lid = getattr(self.model, 'selected_light_id', None)
                if lid is not None:
                    try:
                        from roguelike_engine.rendering.lighting import get_global_lighting
                        lm = get_global_lighting()
                        pid = f"persist:{int(lid)}"
                        for lt in getattr(lm, '_lights', []):
                            try:
                                if getattr(lt, 'id', None) == pid:
                                    lt.x = float(self.model._drag_world_x)
                                    lt.y = float(self.model._drag_world_y)
                                    break
                            except Exception:
                                continue
                    except Exception:
                        pass
            except Exception:
                pass
            return
        if event.type == pygame.MOUSEBUTTONUP and getattr(self.model, '_dragging_inst', False):
            # Persist new position and update live light
            st = self.model
            st._dragging_inst = False
            lid = getattr(st, 'selected_light_id', None)
            wx = getattr(st, '_drag_world_x', None)
            wy = getattr(st, '_drag_world_y', None)
            if lid is not None and wx is not None and wy is not None:
                try:
                    update_instance_position(int(lid), float(wx), float(wy))
                except Exception:
                    pass
                # Move live light in manager if present
                try:
                    from roguelike_engine.rendering.lighting import get_global_lighting
                    lm = get_global_lighting()
                    pid = f"persist:{int(lid)}"
                    for lt in getattr(lm, '_lights', []):
                        try:
                            if getattr(lt, 'id', None) == pid:
                                lt.x = float(wx)
                                lt.y = float(wy)
                                break
                        except Exception:
                            continue
                except Exception:
                    pass
            return

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
        # Toggle Overlay visibility
        if hasattr(st, '_btn_overlay') and isinstance(st._btn_overlay, pygame.Rect) and st._btn_overlay.collidepoint(x, y):
            try:
                st.overlay_visible = not bool(getattr(st, 'overlay_visible', True))
            except Exception:
                pass
            return
        # Toggle Labels visibility
        if hasattr(st, '_btn_labels') and isinstance(st._btn_labels, pygame.Rect) and st._btn_labels.collidepoint(x, y):
            try:
                st.overlay_labels = not bool(getattr(st, 'overlay_labels', True))
            except Exception:
                pass
            return
        # Delete Selected
        if hasattr(st, '_btn_delete_selected') and isinstance(st._btn_delete_selected, pygame.Rect) and st._btn_delete_selected.collidepoint(x, y):
            try:
                ids = list(getattr(st, 'selected_light_ids', set()) or [])
                if ids:
                    deleted = delete_instances(ids)
                    # Remove from manager now
                    try:
                        from roguelike_engine.rendering.lighting import get_global_lighting
                        lm = get_global_lighting()
                        for i in ids:
                            lm.remove_by_id(f"persist:{int(i)}")
                    except Exception:
                        pass
                    # Clear selection
                    st.selected_light_ids.clear()
                    st.selected_light_id = None
            except Exception:
                pass
            return
        # Palette cycle for hovered/selected preset
        if hasattr(st, '_btn_palette_prev') and isinstance(st._btn_palette_prev, pygame.Rect) and st._btn_palette_prev.collidepoint(x, y):
            self._cycle_overlay_preset_color(prev=True); return
        if hasattr(st, '_btn_palette_next') and isinstance(st._btn_palette_next, pygame.Rect) and st._btn_palette_next.collidepoint(x, y):
            self._cycle_overlay_preset_color(prev=False); return
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
        # Draw overlays for persistent lights before panels so UI remains on top
        self._render_instances_overlay(screen)
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
            from roguelike_editors.lighting.services.light_instances_service import append_instance as persist_light_instance
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
                    flicker_phase_rad=random.Random().uniform(0.0, 2.0 * math.pi),
                    center_scale=float(getattr(stp, 'spawn_center_scale', 1.0)),
                )
            )
            # Persist to light_instances.json using preset id and overrides vs preset
            try:
                preset_id = str(getattr(stp, 'spawn_preset', 'Custom'))
                params = {
                    'radius': int(getattr(stp, 'spawn_radius', 160)),
                    'color': tuple(getattr(stp, 'spawn_color', (255, 200, 140))),
                    'intensity': float(getattr(stp, 'spawn_intensity', 1.0)),
                    'falloff': float(getattr(stp, 'spawn_falloff', 2.0)),
                    'flicker_amp': float(getattr(stp, 'spawn_flicker_amp', 0.15)),
                    'flicker_speed': float(getattr(stp, 'spawn_flicker_speed', 2.5)),
                    'center_scale': float(getattr(stp, 'spawn_center_scale', 1.0)),
                }
                persist_light_instance(preset_id, float(wx), float(wy), params=params)
            except Exception:
                pass
        except Exception:
            pass

    def _render_instances_overlay(self, screen: pygame.Surface) -> None:
        try:
            # Honor overlay visibility toggle
            if not bool(getattr(self.model, 'overlay_visible', True)):
                return
            cam = getattr(self.game, 'camera', None)
            if cam is None:
                return
            z = float(getattr(cam, 'zoom', 1.0) or 1.0)
            ox = float(getattr(cam, 'offset_x', 0.0) or 0.0)
            oy = float(getattr(cam, 'offset_y', 0.0) or 0.0)
            presets = _load_presets()
            insts = load_light_instances() or []
        except Exception:
            return
        sw, sh = screen.get_size()
        rect_screen = pygame.Rect(0, 0, sw, sh)
        try:
            font = getattr(self.view, 'font', None) or pygame.font.Font(None, 14)
        except Exception:
            font = None
        try:
            mx, my = pygame.mouse.get_pos()
        except Exception:
            mx = my = -9999
        show_labels = bool(getattr(self.model, 'overlay_labels', True))
        hovered_preset: str | None = None
        for e in insts:
            try:
                zone = str(e.get('zone') or 'no zone')
                rel_x = int(e.get('rel_x') or 0)
                rel_y = int(e.get('rel_y') or 0)
                off_tx, off_ty = global_map_settings.zone_offsets.get(zone, (0, 0))
                wx = int(off_tx) * TILE_SIZE + rel_x
                wy = int(off_ty) * TILE_SIZE + rel_y
                sx = int((wx - ox) * z)
                sy = int((wy - oy) * z)
                preset_id = str(e.get('preset_id') or '')
                base = presets.get(preset_id, {}) if isinstance(presets, dict) else {}
                params = dict(base)
                ov = e.get('overrides') if isinstance(e, dict) else None
                if isinstance(ov, dict):
                    for k, v in ov.items():
                        params[k] = v
                radius = int(params.get('radius', 160))
                # Overlay palette override
                pal = getattr(self.model, 'overlay_palette', {}) or {}
                color = pal.get(preset_id, params.get('color', (255, 200, 140)))
                try:
                    cr = int(color[0]); cg = int(color[1]); cb = int(color[2])
                except Exception:
                    cr, cg, cb = 255, 200, 140
                rr = int(max(1, radius) * z)
                if rr <= 1:
                    continue
                bb = pygame.Rect(sx - rr, sy - rr, rr * 2, rr * 2)
                if not rect_screen.colliderect(bb):
                    continue
                # Selection ring and drag preview
                try:
                    lid_int = int(e.get('id')) if e.get('id') is not None else None
                except Exception:
                    lid_int = None
                is_selected = lid_int is not None and lid_int == getattr(self.model, 'selected_light_id', None)
                if is_selected and bool(getattr(self.model, '_dragging_inst', False)):
                    wx_drag = getattr(self.model, '_drag_world_x', None)
                    wy_drag = getattr(self.model, '_drag_world_y', None)
                    if wx_drag is not None and wy_drag is not None:
                        sx = int((float(wx_drag) - ox) * z)
                        sy = int((float(wy_drag) - oy) * z)
                        bb = pygame.Rect(sx - rr, sy - rr, rr * 2, rr * 2)
                        if not rect_screen.colliderect(bb):
                            continue
                # Hover highlight
                dx = mx - sx; dy = my - sy
                hovered = (dx * dx + dy * dy) <= (rr * rr)
                if hovered:
                    hovered_preset = preset_id
                col = (80, 240, 255) if is_selected else ((255, 245, 120) if hovered else (cr, cg, cb))
                w_main = 3 if is_selected else (2 if hovered else 1)
                pygame.draw.circle(screen, col, (sx, sy), rr, width=w_main)
                pygame.draw.circle(screen, col, (sx, sy), max(1, int(2 * z)), width=1)
                # Id label near the circumference
                if font is not None and show_labels:
                    try:
                        lid = e.get('id')
                        label = f"#{int(lid)} {preset_id} (r={radius})" if lid is not None else f"{preset_id} (r={radius})"
                    except Exception:
                        label = f"{preset_id} (r={radius})"
                    ts = font.render(label, True, (10, 10, 14))
                    tw, th = ts.get_width(), ts.get_height()
                    lx = sx + rr + 6
                    ly = sy - th // 2
                    # Clamp inside screen
                    if lx + tw > sw - 4:
                        lx = max(4, sx - rr - 6 - tw)
                    if ly + th > sh - 4:
                        ly = max(4, sh - th - 4)
                    bg = pygame.Surface((tw + 6, th + 4), pygame.SRCALPHA)
                    bg.fill((245, 245, 250, 220))
                    screen.blit(bg, (lx - 3, ly - 2))
                    screen.blit(ts, (lx, ly))
            except Exception:
                continue
        # Save hovered preset id for palette UI
        try:
            self.model._hovered_preset_id = hovered_preset
        except Exception:
            pass

    def _cycle_overlay_preset_color(self, *, prev: bool) -> None:
        st = self.model
        try:
            # Determine target preset: hovered first, else from selected id
            pid = getattr(st, '_hovered_preset_id', None)
            if not pid and getattr(st, 'selected_light_id', None) is not None:
                sel_id = int(st.selected_light_id)
                for e in (load_light_instances() or []):
                    try:
                        if int(e.get('id')) == sel_id:
                            pid = str(e.get('preset_id') or '')
                            break
                    except Exception:
                        continue
            if not pid:
                return
            # Cycle through a fixed palette
            palette = [
                (255, 200, 140), (255, 255, 255), (120, 200, 255), (255, 120, 120), (120, 255, 160), (200, 140, 255), (255, 240, 120)
            ]
            pal_map = getattr(st, 'overlay_palette', {}) or {}
            cur = pal_map.get(pid)
            try:
                idx = palette.index(cur) if cur in palette else -1
            except Exception:
                idx = -1
            if prev:
                idx = (idx - 1) % len(palette)
            else:
                idx = (idx + 1) % len(palette)
            pal_map[pid] = palette[idx]
            st.overlay_palette = pal_map
        except Exception:
            pass
