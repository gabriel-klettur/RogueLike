from __future__ import annotations

import pygame

from .day_time_panel_state import DayTimePanelState


class DayTimePanelController:
    def __init__(self, state: DayTimePanelState) -> None:
        self.state = state

    def handle_event(self, event: pygame.event.Event) -> None:
        """Handle mouse click events for the Daytime Tools panel."""
        if event.type != pygame.MOUSEBUTTONDOWN or getattr(event, "button", None) != 1:
            return
        st = self.state
        pos = getattr(event, "pos", None)
        if pos is None:
            return
        if not isinstance(st.panel_rect, pygame.Rect) or not st.panel_rect.collidepoint(pos):
            return
        try:
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            dn = get_global_daynight()
        except Exception:
            dn = None
        if dn is None:
            return

        x, y = pos
        # Helpers
        def _set_minute(m: int) -> None:
            try:
                dn.enabled = True
                dn.set_minute_of_day(int(m) % 1440)
            except Exception:
                pass

        def _get_minute() -> int:
            try:
                return int(dn.get_minute_of_day())
            except Exception:
                return 0

        def _ts_clamp(v: float) -> float:
            return max(0.05, min(5.0, float(v)))

        # Minute stepping
        for rect, delta in (
            (st._btn_time_m5, -5),
            (st._btn_time_p5, +5),
            (st._btn_time_m30, -30),
            (st._btn_time_p30, +30),
        ):
            if isinstance(rect, pygame.Rect) and rect.collidepoint(x, y):
                _set_minute(_get_minute() + delta)
                return

        # Jumps
        for rect, minute in (
            (st._btn_time_05, 300),
            (st._btn_time_07, 420),
            (st._btn_time_12, 720),
            (st._btn_time_19, 1140),
            (st._btn_time_21, 1260),
            (st._btn_time_00, 0),
        ):
            if isinstance(rect, pygame.Rect) and rect.collidepoint(x, y):
                _set_minute(minute)
                return

        # Min intensity stepper
        def _step_min_intensity(delta: float) -> None:
            try:
                cur = float(dn.get_min_intensity())
            except Exception:
                cur = 0.2
            dn.set_min_intensity(max(0.0, min(1.0, cur + delta)))

        if isinstance(st._btn_minI_minus, pygame.Rect) and st._btn_minI_minus.collidepoint(x, y):
            _step_min_intensity(-0.05); return
        if isinstance(st._btn_minI_plus, pygame.Rect) and st._btn_minI_plus.collidepoint(x, y):
            _step_min_intensity(+0.05); return

        # Time scale stepper
        if isinstance(st._btn_ts_minus, pygame.Rect) and st._btn_ts_minus.collidepoint(x, y):
            dn.set_time_scale(_ts_clamp(dn.time_scale_minutes_per_second - 0.05)); return
        if isinstance(st._btn_ts_plus, pygame.Rect) and st._btn_ts_plus.collidepoint(x, y):
            dn.set_time_scale(_ts_clamp(dn.time_scale_minutes_per_second + 0.05)); return

        # Keyframe intensity steppers
        def _kf(minute: int, delta: float) -> None:
            try:
                val = float(dn.get_intensity_at_minute(minute, apply_floor=False))
            except Exception:
                val = 0.0
            val = max(0.0, min(1.0, val + delta))
            dn.set_keyframe(minute, intensity=val)

        mapping = [
            (st._btn_i_0000_minus, 0, -0.05),
            (st._btn_i_0000_plus, 0, +0.05),
            (st._btn_i_0500_minus, 300, -0.05),
            (st._btn_i_0500_plus, 300, +0.05),
            (st._btn_i_0700_minus, 420, -0.05),
            (st._btn_i_0700_plus, 420, +0.05),
            (st._btn_i_1200_minus, 720, -0.05),
            (st._btn_i_1200_plus, 720, +0.05),
            (st._btn_i_1900_minus, 1140, -0.05),
            (st._btn_i_1900_plus, 1140, +0.05),
            (st._btn_i_2100_minus, 1260, -0.05),
            (st._btn_i_2100_plus, 1260, +0.05),
        ]
        for rect, minute, delta in mapping:
            if isinstance(rect, pygame.Rect) and rect.collidepoint(x, y):
                _kf(minute, delta)
                return

        # Save config
        if isinstance(st._btn_time_save, pygame.Rect) and st._btn_time_save.collidepoint(x, y):
            try:
                dn.save_config()
            except Exception:
                pass
            return

