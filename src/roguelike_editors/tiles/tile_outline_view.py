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

    def render(self, screen: pygame.Surface, camera, game_map) -> None:
        """Renderiza outline de hover y selección.

        Args:
            screen: Surface destino.
            camera: Cámara con método apply((x, y)).
            game_map: Mapa actual (se usa para `_tile_under_mouse`).
        """

        # Hover / brush preview (solo en modo brush)
        if self.editor.current_tool == "brush":
            hover = self.controller._tile_under_mouse(pygame.mouse.get_pos(), camera, game_map)
            if hover:
                rect = self._compute_rect(hover, camera)
                # semi-transparent fill
                hover_surf = pygame.Surface((rect.width, rect.height), pygame.SRCALPHA)
                hover_surf.fill((*OUTLINE_HOVER, HOVER_ALPHA))
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
                        blink_surf = pygame.Surface((rect.width, rect.height), pygame.SRCALPHA)
                        blink_surf.fill((*OUTLINE_CHOICE, 100))
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