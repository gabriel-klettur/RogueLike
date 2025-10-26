from __future__ import annotations

import pygame
from typing import Any

from .lighting_state import LightingEditorState
from .lighting_view import LightingEditorView


class LightingEditorController:
    def __init__(self, font: pygame.font.Font | None = None) -> None:
        self.model = LightingEditorState()
        self.view = LightingEditorView(self.model, font=font)
        self.game: Any | None = None  # set by manager

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
                pan = getattr(st, '_panel_rect', None)
                if getattr(st, 'spawn_mode', False) and (not isinstance(pan, pygame.Rect) or not pan.collidepoint(event.pos)):
                    self._spawn_at_screen(event.pos)
                    # Single-shot exits spawn mode automatically
                    if bool(getattr(st, 'spawn_single_shot', False)):
                        st.spawn_mode = False
                    return
                # Otherwise, treat as UI click
                self._on_click(event.pos)

    def _on_click(self, pos: tuple[int, int]) -> None:
        try:
            import pygame
            from roguelike_engine.rendering.lighting import get_global_lighting
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            from roguelike_engine.rendering.lighting.light_types import Light
        except Exception:
            return
        st = self.model
        x, y = pos
        # Spawn Type combo: toggle open/close
        try:
            import pygame  # ensure type
            if isinstance(getattr(st, '_combo_spawn_type', None), pygame.Rect) and st._combo_spawn_type.collidepoint(x, y):
                st.spawn_combo_open = not bool(getattr(st, 'spawn_combo_open', False))
                return
            # If open, check dropdown items selection
            if bool(getattr(st, 'spawn_combo_open', False)):
                items = getattr(st, '_combo_spawn_items', []) or []
                hit_any = False
                for ir, it in items:
                    if isinstance(ir, pygame.Rect) and ir.collidepoint(x, y):
                        st.spawn_preset = str(it)
                        # Apply preset values (Custom leaves current values)
                        if it == "Torch":
                            st.spawn_radius = 160; st.spawn_intensity = 1.0; st.spawn_falloff = 2.0
                            st.spawn_color = (255, 200, 140); st.spawn_flicker_amp = 0.15; st.spawn_flicker_speed = 2.5
                        elif it == "Lamp":
                            st.spawn_radius = 120; st.spawn_intensity = 0.9; st.spawn_falloff = 2.2
                            st.spawn_color = (255, 240, 200); st.spawn_flicker_amp = 0.05; st.spawn_flicker_speed = 1.2
                        elif it == "Magic":
                            st.spawn_radius = 180; st.spawn_intensity = 1.1; st.spawn_falloff = 1.6
                            st.spawn_color = (120, 200, 255); st.spawn_flicker_amp = 0.20; st.spawn_flicker_speed = 3.2
                        # Custom keeps current adjustments
                        st.spawn_combo_open = False
                        hit_any = True
                        return
                # Clicked elsewhere inside panel: close combo
                pan = getattr(st, '_panel_rect', None)
                if isinstance(pan, pygame.Rect) and pan.collidepoint(x, y) and not hit_any:
                    st.spawn_combo_open = False
                    # Don't return; allow other controls below to process
        except Exception:
            pass
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
        # Presets
        if hasattr(st, '_btn_preset_torch') and isinstance(st._btn_preset_torch, pygame.Rect) and st._btn_preset_torch.collidepoint(x, y):
            st.spawn_preset = "Torch"
            st.spawn_radius = 160; st.spawn_intensity = 1.0; st.spawn_falloff = 2.0
            st.spawn_color = (255, 200, 140); st.spawn_flicker_amp = 0.15; st.spawn_flicker_speed = 2.5
            return
        if hasattr(st, '_btn_preset_lamp') and isinstance(st._btn_preset_lamp, pygame.Rect) and st._btn_preset_lamp.collidepoint(x, y):
            st.spawn_preset = "Lamp"
            st.spawn_radius = 120; st.spawn_intensity = 0.9; st.spawn_falloff = 2.2
            st.spawn_color = (255, 240, 200); st.spawn_flicker_amp = 0.05; st.spawn_flicker_speed = 1.2
            return
        if hasattr(st, '_btn_preset_magic') and isinstance(st._btn_preset_magic, pygame.Rect) and st._btn_preset_magic.collidepoint(x, y):
            st.spawn_preset = "Magic"
            st.spawn_radius = 180; st.spawn_intensity = 1.1; st.spawn_falloff = 1.6
            st.spawn_color = (120, 200, 255); st.spawn_flicker_amp = 0.20; st.spawn_flicker_speed = 3.2
            return
        # Spawn steppers
        def _clamp(v, lo, hi):
            return lo if v < lo else hi if v > hi else v
        # Radius
        if hasattr(st, '_btn_sr_minus') and isinstance(st._btn_sr_minus, pygame.Rect) and st._btn_sr_minus.collidepoint(x, y):
            st.spawn_radius = _clamp(st.spawn_radius - 8, 16, 2048); return
        if hasattr(st, '_btn_sr_plus') and isinstance(st._btn_sr_plus, pygame.Rect) and st._btn_sr_plus.collidepoint(x, y):
            st.spawn_radius = _clamp(st.spawn_radius + 8, 16, 2048); return
        # Intensity
        if hasattr(st, '_btn_si_minus') and isinstance(st._btn_si_minus, pygame.Rect) and st._btn_si_minus.collidepoint(x, y):
            st.spawn_intensity = _clamp(st.spawn_intensity - 0.1, 0.0, 2.5); return
        if hasattr(st, '_btn_si_plus') and isinstance(st._btn_si_plus, pygame.Rect) and st._btn_si_plus.collidepoint(x, y):
            st.spawn_intensity = _clamp(st.spawn_intensity + 0.1, 0.0, 2.5); return
        # Falloff
        if hasattr(st, '_btn_sf_minus') and isinstance(st._btn_sf_minus, pygame.Rect) and st._btn_sf_minus.collidepoint(x, y):
            st.spawn_falloff = _clamp(st.spawn_falloff - 0.1, 0.5, 4.0); return
        if hasattr(st, '_btn_sf_plus') and isinstance(st._btn_sf_plus, pygame.Rect) and st._btn_sf_plus.collidepoint(x, y):
            st.spawn_falloff = _clamp(st.spawn_falloff + 0.1, 0.5, 4.0); return
        # Flicker amp
        if hasattr(st, '_btn_fa_minus') and isinstance(st._btn_fa_minus, pygame.Rect) and st._btn_fa_minus.collidepoint(x, y):
            st.spawn_flicker_amp = _clamp(st.spawn_flicker_amp - 0.05, 0.0, 1.0); return
        if hasattr(st, '_btn_fa_plus') and isinstance(st._btn_fa_plus, pygame.Rect) and st._btn_fa_plus.collidepoint(x, y):
            st.spawn_flicker_amp = _clamp(st.spawn_flicker_amp + 0.05, 0.0, 1.0); return
        # Flicker speed
        if hasattr(st, '_btn_fs_minus') and isinstance(st._btn_fs_minus, pygame.Rect) and st._btn_fs_minus.collidepoint(x, y):
            st.spawn_flicker_speed = _clamp(st.spawn_flicker_speed - 0.2, 0.0, 10.0); return
        if hasattr(st, '_btn_fs_plus') and isinstance(st._btn_fs_plus, pygame.Rect) and st._btn_fs_plus.collidepoint(x, y):
            st.spawn_flicker_speed = _clamp(st.spawn_flicker_speed + 0.2, 0.0, 10.0); return
        # Single-shot toggle
        if hasattr(st, '_btn_single_shot') and isinstance(st._btn_single_shot, pygame.Rect) and st._btn_single_shot.collidepoint(x, y):
            st.spawn_single_shot = not bool(st.spawn_single_shot); return
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
        # Time Scale steppers (game minutes per real second)
        try:
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            dn = get_global_daynight()
        except Exception:
            dn = None
        if dn is not None:
            def _ts_clamp(v: float) -> float:
                return max(0.05, min(5.0, float(v)))
            if hasattr(st, '_btn_ts_minus') and isinstance(st._btn_ts_minus, pygame.Rect) and st._btn_ts_minus.collidepoint(x, y):
                dn.set_time_scale(_ts_clamp(dn.time_scale_minutes_per_second - 0.05)); return
            if hasattr(st, '_btn_ts_plus') and isinstance(st._btn_ts_plus, pygame.Rect) and st._btn_ts_plus.collidepoint(x, y):
                dn.set_time_scale(_ts_clamp(dn.time_scale_minutes_per_second + 0.05)); return
            # Daytime Tools: step +/- minutes
            def _set_minute(m: int):
                try:
                    # Ensure ambient overlay is enabled so changes are visible
                    dn.enabled = True
                    dn.set_minute_of_day(int(m) % 1440)
                except Exception:
                    pass
            def _get_minute() -> int:
                try:
                    return int(dn.get_minute_of_day())
                except Exception:
                    return 0
            if hasattr(st, '_btn_time_m5') and isinstance(st._btn_time_m5, pygame.Rect) and st._btn_time_m5.collidepoint(x, y):
                _set_minute(_get_minute() - 5); return
            if hasattr(st, '_btn_time_p5') and isinstance(st._btn_time_p5, pygame.Rect) and st._btn_time_p5.collidepoint(x, y):
                _set_minute(_get_minute() + 5); return
            if hasattr(st, '_btn_time_m30') and isinstance(st._btn_time_m30, pygame.Rect) and st._btn_time_m30.collidepoint(x, y):
                _set_minute(_get_minute() - 30); return
            if hasattr(st, '_btn_time_p30') and isinstance(st._btn_time_p30, pygame.Rect) and st._btn_time_p30.collidepoint(x, y):
                _set_minute(_get_minute() + 30); return
            # Jumps
            if hasattr(st, '_btn_time_05') and isinstance(st._btn_time_05, pygame.Rect) and st._btn_time_05.collidepoint(x, y):
                _set_minute(300); return
            if hasattr(st, '_btn_time_07') and isinstance(st._btn_time_07, pygame.Rect) and st._btn_time_07.collidepoint(x, y):
                _set_minute(420); return
            if hasattr(st, '_btn_time_12') and isinstance(st._btn_time_12, pygame.Rect) and st._btn_time_12.collidepoint(x, y):
                _set_minute(720); return
            if hasattr(st, '_btn_time_19') and isinstance(st._btn_time_19, pygame.Rect) and st._btn_time_19.collidepoint(x, y):
                _set_minute(1140); return
            if hasattr(st, '_btn_time_21') and isinstance(st._btn_time_21, pygame.Rect) and st._btn_time_21.collidepoint(x, y):
                _set_minute(1260); return
            if hasattr(st, '_btn_time_00') and isinstance(st._btn_time_00, pygame.Rect) and st._btn_time_00.collidepoint(x, y):
                _set_minute(0); return
        # Color steppers (RGB)
        r, g, b = self.model.spawn_color
        def _cs(v):
            return max(0, min(255, int(v)))
        if hasattr(st, '_btn_r_minus') and isinstance(st._btn_r_minus, pygame.Rect) and st._btn_r_minus.collidepoint(x, y):
            self.model.spawn_color = (_cs(r - 5), g, b); return
        if hasattr(st, '_btn_r_plus') and isinstance(st._btn_r_plus, pygame.Rect) and st._btn_r_plus.collidepoint(x, y):
            self.model.spawn_color = (_cs(r + 5), g, b); return
        if hasattr(st, '_btn_g_minus') and isinstance(st._btn_g_minus, pygame.Rect) and st._btn_g_minus.collidepoint(x, y):
            self.model.spawn_color = (r, _cs(g - 5), b); return
        if hasattr(st, '_btn_g_plus') and isinstance(st._btn_g_plus, pygame.Rect) and st._btn_g_plus.collidepoint(x, y):
            self.model.spawn_color = (r, _cs(g + 5), b); return
        if hasattr(st, '_btn_b_minus') and isinstance(st._btn_b_minus, pygame.Rect) and st._btn_b_minus.collidepoint(x, y):
            self.model.spawn_color = (r, g, _cs(b - 5)); return
        if hasattr(st, '_btn_b_plus') and isinstance(st._btn_b_plus, pygame.Rect) and st._btn_b_plus.collidepoint(x, y):
            self.model.spawn_color = (r, g, _cs(b + 5)); return

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
            st = self.model
            get_global_lighting().add(
                Light(
                    x=wx,
                    y=wy,
                    radius=int(getattr(st, 'spawn_radius', 160)),
                    color=tuple(getattr(st, 'spawn_color', (255, 200, 140))),
                    intensity=float(getattr(st, 'spawn_intensity', 1.0)),
                    falloff=float(getattr(st, 'spawn_falloff', 2.0)),
                    flicker_amp=float(getattr(st, 'spawn_flicker_amp', 0.15)),
                    flicker_speed=float(getattr(st, 'spawn_flicker_speed', 2.5)),
                )
            )
        except Exception:
            pass
