import pygame
import logging
from roguelike_editors.buildings.buildings_editor_config import (
    Z_PANEL_W,
    Z_PANEL_H,
    Z_BTN_W,
    Z_BTN_H,
)
logger = logging.getLogger(__name__)

class ZTool:
    """
    Panel flotante para editar la capa Z de un edificio.
    — target  : "bottom" | "top"    
    """    

    def __init__(self, state, editor_state, *, target: str = "bottom"):
        self.state = state
        self.editor_state = editor_state
        self.target = target          # bottom | top

    # ------------------------------------------------------------------ #
    # MOUSE CLICK                                                        #
    # ------------------------------------------------------------------ #
    def handle_mouse_click(self, mouse_pos: tuple[int, int], buildings, camera) -> bool:
        """Detecta clicks sobre los botones +/- del panel Z.

        Calcula los rects en coordenadas de pantalla en el momento del click
        usando el camera y el tamaño/posición actual del building, para evitar
        desfases tras un resize o cambios de zoom.

        Returns True si consumió el evento.
        """
        mx, my = mouse_pos
        # Si existe un edificio activo (el único que dibuja paneles), limitar a ese
        active = getattr(self.editor_state, 'active_building', None)
        to_iter = [active] if active is not None else list(reversed(buildings))
        # Prioriza el edificio más arriba (render order) recorriendo al revés si no hay activo
        for b in to_iter:
            minus_rect, plus_rect = self._get_button_rects(b, camera)
            if minus_rect and minus_rect.collidepoint(mx, my):
                self._update_z(b, -1)
                return True
            if plus_rect and plus_rect.collidepoint(mx, my):
                self._update_z(b, +1)
                return True
        return False

    def _update_z(self, building, delta):
        if self.target == "bottom":
            # Update bottom and keep non-negative
            building.z_bottom = max(0, building.z_bottom + delta)
            # Ensure top is never below bottom
            if building.z_top < building.z_bottom:
                building.z_top = building.z_bottom
            # Sync global z_state with bottom layer (collision/main layer)
            self.state.z_state.set(building, building.z_bottom)
            logger.info(f"⬇️  Z‑bottom nuevo: {building.z_bottom} (top={building.z_top})")
        else:
            # Update top ensuring it's not below bottom and is non-negative
            new_top = max(0, building.z_top + delta)
            building.z_top = max(building.z_bottom, new_top)
            logger.info(f"⬆️  Z‑top nuevo: {building.z_top} (bottom={building.z_bottom})")

    # ------------------------------------------------------------------ #
    # LÓGICA DE GEOMETRÍA (screen-space)
    # ------------------------------------------------------------------ #
    def _compute_panel_pos(self, building, camera) -> tuple[int, int]:
        """Posición topleft del panel en pantalla para este building/target."""
        # Tamaño del edificio en pantalla según zoom
        w_scaled, h_scaled = camera.scale(building.image.get_size())
        # Posición del edificio en pantalla
        x, y = camera.apply((building.x, building.y))
        panel_x = x + (w_scaled - Z_PANEL_W) // 2
        # Anclaje arriba/abajo con ligero margen
        panel_y = y + (h_scaled - 50 if self.target == "bottom" else 10)
        return int(panel_x), int(panel_y)

    def _get_button_rects(self, building, camera) -> tuple[pygame.Rect | None, pygame.Rect | None]:
        """Rectángulos absolutos de los botones '-' y '+' en pantalla.

        Devuelve (minus_rect, plus_rect). Puede devolver (None, None) si el
        building no tiene image o no está inicializado, por robustez.
        """
        if not getattr(building, "image", None):
            return None, None
        panel_x, panel_y = self._compute_panel_pos(building, camera)
        minus_rel = pygame.Rect(5, 5, Z_BTN_W, Z_BTN_H)
        plus_rel  = pygame.Rect(Z_PANEL_W - 5 - Z_BTN_W, 5, Z_BTN_W, Z_BTN_H)
        return (
            minus_rel.move(panel_x, panel_y),
            plus_rel.move(panel_x, panel_y),
        )