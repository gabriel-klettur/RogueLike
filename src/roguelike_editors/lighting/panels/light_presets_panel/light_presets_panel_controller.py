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

        # Dropdown item selection
        if bool(getattr(st, "spawn_combo_open", False)):
            items = getattr(st, "_combo_spawn_items", []) or []
            for ir, it in items:
                if isinstance(ir, pygame.Rect) and ir.collidepoint(x, y):
                    st.spawn_preset = str(it)
                    if it == "Torch":
                        st.spawn_radius = 160; st.spawn_intensity = 1.0; st.spawn_falloff = 2.0
                        st.spawn_color = (255, 200, 140); st.spawn_flicker_amp = 0.15; st.spawn_flicker_speed = 2.5
                    elif it == "Lamp":
                        st.spawn_radius = 120; st.spawn_intensity = 0.9; st.spawn_falloff = 2.2
                        st.spawn_color = (255, 240, 200); st.spawn_flicker_amp = 0.05; st.spawn_flicker_speed = 1.2
                    elif it == "Magic":
                        st.spawn_radius = 180; st.spawn_intensity = 1.1; st.spawn_falloff = 1.6
                        st.spawn_color = (120, 200, 255); st.spawn_flicker_amp = 0.20; st.spawn_flicker_speed = 3.2
                    st.spawn_combo_open = False
                    return
            # Clicked elsewhere inside panel closes combo; allow other controls to process
            if isinstance(st.panel_rect, pygame.Rect) and st.panel_rect.collidepoint(x, y):
                st.spawn_combo_open = False

        # Preset buttons
        if isinstance(st._btn_preset_torch, pygame.Rect) and st._btn_preset_torch.collidepoint(x, y):
            st.spawn_preset = "Torch"
            st.spawn_radius = 160; st.spawn_intensity = 1.0; st.spawn_falloff = 2.0
            st.spawn_color = (255, 200, 140); st.spawn_flicker_amp = 0.15; st.spawn_flicker_speed = 2.5
            return
        if isinstance(st._btn_preset_lamp, pygame.Rect) and st._btn_preset_lamp.collidepoint(x, y):
            st.spawn_preset = "Lamp"
            st.spawn_radius = 120; st.spawn_intensity = 0.9; st.spawn_falloff = 2.2
            st.spawn_color = (255, 240, 200); st.spawn_flicker_amp = 0.05; st.spawn_flicker_speed = 1.2
            return
        if isinstance(st._btn_preset_magic, pygame.Rect) and st._btn_preset_magic.collidepoint(x, y):
            st.spawn_preset = "Magic"
            st.spawn_radius = 180; st.spawn_intensity = 1.1; st.spawn_falloff = 1.6
            st.spawn_color = (120, 200, 255); st.spawn_flicker_amp = 0.20; st.spawn_flicker_speed = 3.2
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

