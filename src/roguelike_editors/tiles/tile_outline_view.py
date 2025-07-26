# roguelike_project/systems/editor/tiles/view/tools/tile_outline_view.py

import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.tiles.tiles_editor_config import OUTLINE_HOVER, OUTLINE_SEL

class TileOutlineView:
    def __init__(self, controller, editor_state):
        self.controller = controller        
        self.editor = editor_state

    def render(self, screen, camera, map):
        
        # Hover / brush preview
        hover = self.controller._tile_under_mouse(pygame.mouse.get_pos(), camera, map)
        if hover:
            w, h = self.controller.editor.size_panel_state.selected_size
            x0, y0 = camera.apply((hover.x, hover.y))
            x1, y1 = camera.apply((hover.x + TILE_SIZE * w, hover.y + TILE_SIZE * h))
            rect = pygame.Rect(x0, y0, x1 - x0, y1 - y0)
            pygame.draw.rect(screen, OUTLINE_HOVER, rect, 3)

        # Seleccionado
        sel = self.editor.selected_tile
        if sel:
            w, h = self.controller.editor.size_panel_state.selected_size
            x0, y0 = camera.apply((sel.x, sel.y))
            x1, y1 = camera.apply((sel.x + TILE_SIZE * w, sel.y + TILE_SIZE * h))
            rect = pygame.Rect(x0, y0, x1 - x0, y1 - y0)
            # Eyedropper flash overlay
            flash_start = self.editor.eyedropper_flash_start
            if flash_start is not None:
                elapsed = pygame.time.get_ticks() - flash_start
                if elapsed < 3000:  # duration in ms
                    if (elapsed // 300) % 2 == 0:
                        blink_surf = pygame.Surface((rect.width, rect.height), pygame.SRCALPHA)
                        blink_surf.fill((255, 255, 0, 100))
                        screen.blit(blink_surf, rect.topleft)
                else:
                    # End flash
                    self.editor.eyedropper_flash_start = None
            pygame.draw.rect(screen, OUTLINE_SEL, rect, 3)