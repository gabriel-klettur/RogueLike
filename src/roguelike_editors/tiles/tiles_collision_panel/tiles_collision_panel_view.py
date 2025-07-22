import pygame
from roguelike_editors.tiles.tiles_editor_config import THUMB, PAD, CLR_SELECTION, CLR_HOVER

class TilesCollisionPanelView:
    """View for the Tiles Collision Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def render(self, screen):
        # Render collision panel UI
        # Render collision picker UI (# collision, . walk)

        options = [("#", "Collision"), (".", "Walk")]
        # Panel dimensions
        w = len(options) * (THUMB + PAD) + PAD
        label_font = pygame.font.SysFont("Arial", 14)
        char_font = pygame.font.SysFont("Arial", THUMB)
        h = THUMB + PAD + label_font.get_height() + PAD
        mouse_pos = pygame.mouse.get_pos()
        surf = pygame.Surface((w, h), pygame.SRCALPHA)
        surf.fill((20, 20, 20, 200))
        # Determine panel position
        toolbar_state = self.controller.editor_state.toolbar_state
        # Dynamic positioning if not dragging
        if not toolbar_state.collision_picker_dragging:
            editor_ctrl = self.controller.editor_controller
            vp_state = editor_ctrl.view_panel_controller.state
            if hasattr(vp_state, 'pos') and hasattr(vp_state, 'size') and vp_state.pos and vp_state.size:
                x_vp, y_vp = vp_state.pos
                _, h_vp = vp_state.size
                pos_x = x_vp
                pos_y = y_vp + h_vp + PAD
            else:
                sw, sh = screen.get_size()
                pos_x = (sw - w) // 2
                pos_y = (sh - h) // 2
            toolbar_state.collision_picker_pos = (pos_x, pos_y)
        # Use stored position (for dragging)
        pos_x, pos_y = toolbar_state.collision_picker_pos
        # Store panel size for event handling
        toolbar_state.collision_picker_panel_size = (w, h)
        # Prepare rects
        self.state.option_rects.clear()
        for i, (ch, label) in enumerate(options):
            x = PAD + i * (THUMB + PAD)
            y = PAD
            # Draw character icon
            color = (255, 0, 0) if ch == "#" else (200, 200, 200)
            text_surf = char_font.render(ch, True, color)
            surf.blit(text_surf, (x + (THUMB - text_surf.get_width()) // 2,
                                  y + (THUMB - text_surf.get_height()) // 2))
            # Absolute rect for click detection
            abs_rect = pygame.Rect(pos_x + x, pos_y + y, THUMB, THUMB)
            self.state.option_rects[ch] = abs_rect
            # Hover and selection border
            if abs_rect.collidepoint(mouse_pos):
                pygame.draw.rect(surf, CLR_HOVER, (x, y, THUMB, THUMB), 3)
            elif toolbar_state.collision_choice == ch:
                pygame.draw.rect(surf, CLR_SELECTION, (x, y, THUMB, THUMB), 3)
            # Label below icon
            lbl_surf = label_font.render(label, True, (255, 255, 255))
            surf.blit(lbl_surf, (x + (THUMB - lbl_surf.get_width()) // 2,
                                 y + THUMB + PAD))
        # Blit panel
        pygame.draw.rect(surf, CLR_SELECTION, surf.get_rect(), 3)
        screen.blit(surf, (pos_x, pos_y))
