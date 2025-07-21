# roguelike_game/systems/editor/tiles/view/tools/tile_toolbar_view.py
import pygame
from roguelike_editors.tiles.tiles_editor_config import TOOLS, BTN_W, BTN_H, THUMB, PAD, CLR_SELECTION, CLR_HOVER
from roguelike_engine.map.model.layer import Layer

class TileToolbarView:
    def __init__(self, toolbar):
        self.toolbar = toolbar

    def render(self, screen):

        for idx, tool in enumerate(TOOLS):
            px = self.toolbar.x
            py = self.toolbar.y + idx * (self.toolbar.size + self.toolbar.padding)
            rect = pygame.Rect(px, py, self.toolbar.size, self.toolbar.size)
            self.toolbar.icon_rects[tool] = rect
            screen.blit(self.toolbar.icons[tool], (px, py))
            # Yellow border for active tool or collisions mode
            if tool == "view_collisions":
                # Yellow border when collision mode (only or overlay) is active
                color = (255, 200, 0) if (self.toolbar.editor_state.toolbar_state.show_collisions or self.toolbar.editor_state.toolbar_state.show_collisions_overlay) else (255, 255, 255)
            else:
                color = (255, 200, 0) if self.toolbar.editor_state.current_tool == tool else (255, 255, 255)
            pygame.draw.rect(screen, color, rect, 4)

        # Collision picker UI
        if self.toolbar.editor_state.toolbar_state.collision_picker_open:
            options = [("#", "Collision"), (".", "Walk")]
            w = len(options) * (THUMB + PAD) + PAD
            label_font = pygame.font.SysFont("Arial", 14)
            char_font = pygame.font.SysFont("Arial", THUMB)
            h = THUMB + PAD + label_font.get_height() + PAD
            mouse_pos = pygame.mouse.get_pos()
            surf = pygame.Surface((w, h), pygame.SRCALPHA)
            surf.fill((20, 20, 20, 235))
            sw, sh = screen.get_size()
            if self.toolbar.editor_state.toolbar_state.collision_picker_pos is None:
                pos_x = (sw - w) // 2
                pos_y = (sh - h) // 2
                self.toolbar.editor_state.toolbar_state.collision_picker_pos = (pos_x, pos_y)
            else:
                pos_x, pos_y = self.toolbar.editor_state.toolbar_state.collision_picker_pos
            self.toolbar.editor_state.toolbar_state.collision_picker_panel_size = (w, h)
            self.toolbar.editor_state.toolbar_state.collision_picker_rects.clear()
            for i, (ch, label) in enumerate(options):
                x = PAD + i * (THUMB + PAD)
                y = PAD
                color = (255, 0, 0) if ch == "#" else (200, 200, 200)
                text_surf = char_font.render(ch, True, color)
                surf.blit(text_surf, (x + (THUMB - text_surf.get_width()) // 2,
                                      y + (THUMB - text_surf.get_height()) // 2))
                abs_rect = pygame.Rect(pos_x + x, pos_y + y, THUMB, THUMB)
                self.toolbar.editor_state.toolbar_state.collision_picker_rects[ch] = abs_rect
                if abs_rect.collidepoint(mouse_pos):
                    pygame.draw.rect(surf, CLR_HOVER, (x, y, THUMB, THUMB), 3)
                elif self.toolbar.editor_state.toolbar_state.collision_choice == ch:
                    pygame.draw.rect(surf, CLR_SELECTION, (x, y, THUMB, THUMB), 3)
                lbl_surf = label_font.render(label, True, (255, 255, 255))
                surf.blit(lbl_surf, (x + (THUMB - lbl_surf.get_width()) // 2,
                                     y + THUMB + PAD))
            screen.blit(surf, (pos_x, pos_y))