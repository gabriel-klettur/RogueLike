from __future__ import annotations

import pygame

from .light_presets_panel_state import LightPresetsPanelState


class LightPresetsPanelController:
    def __init__(self, state: LightPresetsPanelState) -> None:
        self.state = state

    def handle_event(self, event: pygame.event.Event) -> None:
        if event.type != pygame.MOUSEBUTTONDOWN or getattr(event, "button", None) != 1:
            return
        st = self.state
        pos = getattr(event, "pos", None)
        if pos is None:
            return
        if not isinstance(st.panel_rect, pygame.Rect) or not st.panel_rect.collidepoint(pos):
            return

        x, y = pos

        def _clamp(v, lo, hi):
            return lo if v < lo else hi if v > hi else v

        # Combo toggle
        if isinstance(getattr(st, "_combo_spawn_type", None), pygame.Rect) and st._combo_spawn_type.collidepoint(x, y):
            st.spawn_combo_open = not bool(getattr(st, "spawn_combo_open", False))
            return

        # Helper to apply a preset from state.presets
        def _apply_preset(name: str) -> None:
            st.spawn_preset = name
            p = getattr(st, 'presets', {}).get(name, {}) if hasattr(st, 'presets') else {}
            try:
                st.spawn_radius = int(p.get("radius", st.spawn_radius))
                st.spawn_intensity = float(p.get("intensity", st.spawn_intensity))
                st.spawn_falloff = float(p.get("falloff", st.spawn_falloff))
                c = p.get("color", st.spawn_color)
                if isinstance(c, (list, tuple)) and len(c) == 3:
                    st.spawn_color = (int(c[0]), int(c[1]), int(c[2]))
                st.spawn_flicker_amp = float(p.get("flicker_amp", st.spawn_flicker_amp))
                st.spawn_flicker_speed = float(p.get("flicker_speed", st.spawn_flicker_speed))
                st.spawn_center_scale = float(p.get("center_scale", st.spawn_center_scale))
            except Exception:
                # Keep current values on any conversion error
                pass

        # Dropdown item selection
        if bool(getattr(st, "spawn_combo_open", False)):
            items = getattr(st, "_combo_spawn_items", []) or []
            for ir, it in items:
                if isinstance(ir, pygame.Rect) and ir.collidepoint(x, y):
                    _apply_preset(str(it))
                    st.spawn_combo_open = False
                    return
            # Clicked elsewhere inside panel closes combo; allow other controls to process
            if isinstance(st.panel_rect, pygame.Rect) and st.panel_rect.collidepoint(x, y):
                st.spawn_combo_open = False

        # Preset buttons
        if isinstance(st._btn_preset_torch, pygame.Rect) and st._btn_preset_torch.collidepoint(x, y):
            _apply_preset("Torch")
            return
        if isinstance(st._btn_preset_lamp, pygame.Rect) and st._btn_preset_lamp.collidepoint(x, y):
            _apply_preset("Lamp")
            return
        if isinstance(st._btn_preset_magic, pygame.Rect) and st._btn_preset_magic.collidepoint(x, y):
            _apply_preset("Magic")
            return

        # Param steppers
        if isinstance(st._btn_sr_minus, pygame.Rect) and st._btn_sr_minus.collidepoint(x, y):
            st.spawn_radius = _clamp(st.spawn_radius - 8, 16, 2048); return
        if isinstance(st._btn_sr_plus, pygame.Rect) and st._btn_sr_plus.collidepoint(x, y):
            st.spawn_radius = _clamp(st.spawn_radius + 8, 16, 2048); return
        if isinstance(st._btn_si_minus, pygame.Rect) and st._btn_si_minus.collidepoint(x, y):
            st.spawn_intensity = _clamp(st.spawn_intensity - 0.1, 0.0, 2.5); return
        if isinstance(st._btn_si_plus, pygame.Rect) and st._btn_si_plus.collidepoint(x, y):
            st.spawn_intensity = _clamp(st.spawn_intensity + 0.1, 0.0, 2.5); return
        if isinstance(st._btn_sf_minus, pygame.Rect) and st._btn_sf_minus.collidepoint(x, y):
            st.spawn_falloff = _clamp(st.spawn_falloff - 0.1, 0.5, 4.0); return
        if isinstance(st._btn_sf_plus, pygame.Rect) and st._btn_sf_plus.collidepoint(x, y):
            st.spawn_falloff = _clamp(st.spawn_falloff + 0.1, 0.5, 4.0); return
        if isinstance(st._btn_fa_minus, pygame.Rect) and st._btn_fa_minus.collidepoint(x, y):
            st.spawn_flicker_amp = _clamp(st.spawn_flicker_amp - 0.05, 0.0, 1.0); return
        if isinstance(st._btn_fa_plus, pygame.Rect) and st._btn_fa_plus.collidepoint(x, y):
            st.spawn_flicker_amp = _clamp(st.spawn_flicker_amp + 0.05, 0.0, 1.0); return
        if isinstance(st._btn_fs_minus, pygame.Rect) and st._btn_fs_minus.collidepoint(x, y):
            st.spawn_flicker_speed = _clamp(st.spawn_flicker_speed - 0.2, 0.0, 10.0); return
        if isinstance(st._btn_fs_plus, pygame.Rect) and st._btn_fs_plus.collidepoint(x, y):
            st.spawn_flicker_speed = _clamp(st.spawn_flicker_speed + 0.2, 0.0, 10.0); return

        # Center Scale steppers
        if isinstance(st._btn_cs_minus, pygame.Rect) and st._btn_cs_minus.collidepoint(x, y):
            st.spawn_center_scale = _clamp(st.spawn_center_scale - 0.05, 0.1, 2.0); return
        if isinstance(st._btn_cs_plus, pygame.Rect) and st._btn_cs_plus.collidepoint(x, y):
            st.spawn_center_scale = _clamp(st.spawn_center_scale + 0.05, 0.1, 2.0); return

        # Single-shot toggle
        if isinstance(st._btn_single_shot, pygame.Rect) and st._btn_single_shot.collidepoint(x, y):
            st.spawn_single_shot = not bool(st.spawn_single_shot); return

        # Color steppers
        r, g, b = st.spawn_color
        def _cs(v):
            return max(0, min(255, int(v)))
        if isinstance(st._btn_r_minus, pygame.Rect) and st._btn_r_minus.collidepoint(x, y):
            st.spawn_color = (_cs(r - 5), g, b); return
        if isinstance(st._btn_r_plus, pygame.Rect) and st._btn_r_plus.collidepoint(x, y):
            st.spawn_color = (_cs(r + 5), g, b); return
        if isinstance(st._btn_g_minus, pygame.Rect) and st._btn_g_minus.collidepoint(x, y):
            st.spawn_color = (r, _cs(g - 5), b); return
        if isinstance(st._btn_g_plus, pygame.Rect) and st._btn_g_plus.collidepoint(x, y):
            st.spawn_color = (r, _cs(g + 5), b); return
        if isinstance(st._btn_b_minus, pygame.Rect) and st._btn_b_minus.collidepoint(x, y):
            st.spawn_color = (r, g, _cs(b - 5)); return
        if isinstance(st._btn_b_plus, pygame.Rect) and st._btn_b_plus.collidepoint(x, y):
            st.spawn_color = (r, g, _cs(b + 5)); return

