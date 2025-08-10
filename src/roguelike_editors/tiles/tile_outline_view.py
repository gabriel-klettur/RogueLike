# roguelike_project/systems/editor/tiles/view/tools/tile_outline_view.py

import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.tiles.tiles_editor_config import (
    OUTLINE_HOVER,
    OUTLINE_SEL,
    OUTLINE_CHOICE,
    OUTLINE_WIDTH,
    HOVER_ALPHA,
    EYEDROPPER_BLINK_DURATION_MS,
    EYEDROPPER_BLINK_INTERVAL_MS,
)

class TileOutlineView:
    """Dibuja contornos y overlays de hover/selección en el editor de tiles."""

    def __init__(self, controller, editor_state) -> None:
        self.controller = controller
        self.editor = editor_state
        # Cachear surfaces semitransparentes por tamaño para evitar crear por frame
        self._hover_surf_cache: dict[tuple[int, int], pygame.Surface] = {}
        self._blink_surf_cache: dict[tuple[int, int], pygame.Surface] = {}

    def render(self, screen: pygame.Surface, camera, game_map) -> None:
        """Renderiza outline de hover y selección.

        Args:
            screen: Surface destino.
            camera: Cámara con método apply((x, y)).
            game_map: Mapa actual (se usa para `_tile_under_mouse`).
        """

        # Hover / brush preview (brush, delete y default)
        if self.editor.current_tool in ("brush", "delete", "default"):
            hover = self.controller._tile_under_mouse(pygame.mouse.get_pos(), camera, game_map)
            if hover:
                rect = self._compute_rect(hover, camera)
                # semi-transparent fill (reusar surface del tamaño actual)
                hover_surf = self._get_hover_surface(rect.width, rect.height)
                screen.blit(hover_surf, rect.topleft)
                pygame.draw.rect(screen, OUTLINE_HOVER, rect, OUTLINE_WIDTH)

        # Seleccionado
        sel = self.editor.selected_tile
        if sel:
            rect = self._compute_rect(sel, camera)
            # Eyedropper flash overlay
            flash_start = self.editor.eyedropper_flash_start
            if flash_start is not None:
                elapsed = pygame.time.get_ticks() - flash_start
                if elapsed < EYEDROPPER_BLINK_DURATION_MS:
                    if (elapsed // EYEDROPPER_BLINK_INTERVAL_MS) % 2 == 0:
                        blink_surf = self._get_blink_surface(rect.width, rect.height)
                        screen.blit(blink_surf, rect.topleft)
                else:
                    # End flash
                    self.editor.eyedropper_flash_start = None
            pygame.draw.rect(screen, OUTLINE_SEL, rect, OUTLINE_WIDTH)

    def _compute_rect(self, tile, camera) -> pygame.Rect:
        """Calcula el rect del tile respetando el tamaño seleccionado del brush."""
        w, h = self.editor.size_panel_state.selected_size
        x0, y0 = camera.apply((tile.x, tile.y))
        x1, y1 = camera.apply((tile.x + TILE_SIZE * w, tile.y + TILE_SIZE * h))
        return pygame.Rect(x0, y0, x1 - x0, y1 - y0)

    def _get_hover_surface(self, w: int, h: int) -> pygame.Surface:
        """Obtiene una surface semitransparente para hover del tamaño indicado."""
        key = (w, h)
        surf = self._hover_surf_cache.get(key)
        if surf is None:
            surf = pygame.Surface((w, h), pygame.SRCALPHA)
            surf.fill((*OUTLINE_HOVER, HOVER_ALPHA))
            self._hover_surf_cache[key] = surf
        return surf

    def _get_blink_surface(self, w: int, h: int) -> pygame.Surface:
        """Obtiene una surface semitransparente para el flash del cuentagotas."""
        key = (w, h)
        surf = self._blink_surf_cache.get(key)
        if surf is None:
            surf = pygame.Surface((w, h), pygame.SRCALPHA)
            surf.fill((*OUTLINE_CHOICE, 100))
            self._blink_surf_cache[key] = surf
        return surf