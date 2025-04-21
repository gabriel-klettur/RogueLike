import pygame
from roguelike_project.systems.editor.buildings.config import Z_PANEL_W, Z_PANEL_H, Z_BTN_W, Z_BTN_H

class ZToolView:
    def __init__(self, state, editor_state, target="bottom"):
        self.state        = state
        self.editor       = editor_state
        self.target       = target
        self.font         = pygame.font.SysFont("Arial", 16)
        self._panel_cache = {}   # zoom -> Surface
        self._text_cache  = {}   # (char, zoom) -> Surface

    def render(self, screen, building):
        cam         = self.state.camera
        w_scaled, h_scaled = cam.scale(building.image.get_size())
        x, y        = cam.apply((building.x, building.y))
        panel_x     = x + (w_scaled - Z_PANEL_W) // 2
        panel_y     = y + (h_scaled - 50 if self.target == "bottom" else 10)

        zoom  = round(cam.zoom, 2)
        base  = self._get_cached_panel_base(zoom)
        panel = base.copy()

        z_val = building.z_bottom if self.target == "bottom" else building.z_top
        txt   = self._get_text(f"Z: {z_val}", zoom)
        panel.blit(txt, txt.get_rect(center=(Z_PANEL_W//2, Z_PANEL_H//2)))

        screen.blit(panel, (panel_x, panel_y))

        minus = pygame.Rect(5, 5, Z_BTN_W, Z_BTN_H)
        plus  = pygame.Rect(Z_PANEL_W - 5 - Z_BTN_W, 5, Z_BTN_W, Z_BTN_H)
        bounds = {
            "panel_pos":  (panel_x, panel_y),
            "minus_rect": minus,
            "plus_rect":  plus,
        }
        building._ztool_bounds = getattr(building, "_ztool_bounds", {})
        building._ztool_bounds[self.target] = bounds

    def _get_cached_panel_base(self, zoom: float) -> pygame.Surface:
        if zoom in self._panel_cache:
            return self._panel_cache[zoom]

        surf = pygame.Surface((Z_PANEL_W, Z_PANEL_H), pygame.SRCALPHA, 32).convert_alpha()
        surf.fill((0, 0, 0, 190))
        pygame.draw.rect(surf, (255, 255, 255), surf.get_rect(), 2, border_radius=6)

        minus = pygame.Rect(5, 5, Z_BTN_W, Z_BTN_H)
        plus  = pygame.Rect(Z_PANEL_W - 5 - Z_BTN_W, 5, Z_BTN_W, Z_BTN_H)
        pygame.draw.rect(surf, (50, 50, 50), minus, border_radius=4)
        pygame.draw.rect(surf, (50, 50, 50), plus,  border_radius=4)
        surf.blit(self._get_text("-", zoom), (minus.x + 10, minus.y + 5))
        surf.blit(self._get_text("+", zoom), (plus.x  + 10, plus.y  + 5))

        self._panel_cache[zoom] = surf
        return surf

    def _get_text(self, txt: str, zoom: float) -> pygame.Surface:
        key = (txt, zoom)
        if key not in self._text_cache:
            self._text_cache[key] = self.font.render(txt, True, (255, 255, 255))
        return self._text_cache[key]
