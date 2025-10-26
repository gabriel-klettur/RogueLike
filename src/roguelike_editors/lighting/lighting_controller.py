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
        if event.type == pygame.MOUSEBUTTONDOWN:
            # Left click: either UI button interaction or map placement in spawn mode
            if getattr(event, 'button', None) == 1:
                # If spawn mode is active and click is outside the panel -> place light on map
                st = self.model
                pan = getattr(st, '_panel_rect', None)
                if getattr(st, 'spawn_mode', False) and (not isinstance(pan, pygame.Rect) or not pan.collidepoint(event.pos)):
                    self._spawn_at_screen(event.pos)
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

    def render(self, screen: pygame.Surface) -> None:
        if not getattr(self.model, 'visible', False):
            return
        # Read current states
        try:
            from roguelike_engine.rendering.lighting import get_global_lighting
            from roguelike_engine.rendering.lighting.daynight import get_global_daynight
            lights_on = bool(get_global_lighting().enabled)
            ambient_on = bool(get_global_daynight().enabled)
        except Exception:
            lights_on = False
            ambient_on = False
        self.view.render(screen, ambient_on=ambient_on, lights_on=lights_on)

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
            get_global_lighting().add(
                Light(x=wx, y=wy, radius=160, color=(255, 200, 140), intensity=1.0, falloff=2.0, flicker_amp=0.15, flicker_speed=2.5)
            )
        except Exception:
            pass
