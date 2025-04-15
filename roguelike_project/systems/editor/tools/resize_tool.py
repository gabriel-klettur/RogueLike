# roguelike_project/systems/editor/tools/resize_tool.py

import pygame

class ResizeTool:
    def __init__(self, state, editor_state, handle_size=50):
        self.state = state
        self.editor = editor_state
        self.handle_size = handle_size

    def check_resize_handle_click(self, mx, my, building):
        bx, by = self.state.camera.apply((building.x, building.y))
        bw, bh = self.state.camera.scale(building.image.get_size())

        handle_rect = pygame.Rect(
            bx + bw - self.handle_size,
            by,
            self.handle_size,
            self.handle_size
        )
        return handle_rect.collidepoint(mx, my)

    def update_resizing(self, mouse_pos):
        b = self.editor.selected_building
        if not b:
            return

        mx, my = mouse_pos
        start_mx, start_my = self.editor.resize_origin
        dx = mx - start_mx
        dy = my - start_my

        delta = max(dx, dy)
        w0, h0 = self.editor.initial_size
        aspect_ratio = w0 / h0 if h0 != 0 else 1

        new_width = max(50, int(w0 + delta))
        new_height = max(50, int(new_width / aspect_ratio))

        b.resize(new_width, new_height)

    def render_resize_handle(self, screen, building):
        x, y = self.state.camera.apply((building.x, building.y))
        w, h = self.state.camera.scale(building.image.get_size())

        handle_rect = pygame.Rect(
            x + w - self.handle_size,
            y,
            self.handle_size,
            self.handle_size
        )
        pygame.draw.rect(screen, (0, 150, 255), handle_rect)
        pygame.draw.rect(screen, (255, 255, 255), handle_rect, 2)
