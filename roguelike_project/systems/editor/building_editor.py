import pygame

class BuildingEditor:
    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state

    def update(self):
        if self.editor.dragging and self.editor.selected_building:
            mx, my = pygame.mouse.get_pos()
            world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
            world_y = my / self.state.camera.zoom + self.state.camera.offset_y

            b = self.editor.selected_building
            b.x = world_x - self.editor.offset_x
            b.y = world_y - self.editor.offset_y
            b.rect.topleft = (b.x, b.y)  

    def handle_mouse_down(self, pos):
        mx, my = pos
        world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
        world_y = my / self.state.camera.zoom + self.state.camera.offset_y

        for building in reversed(self.state.buildings):
            if building.rect.collidepoint(world_x, world_y):
                self.editor.selected_building = building
                self.editor.dragging = True
                self.editor.offset_x = world_x - building.x
                self.editor.offset_y = world_y - building.y
                print(f"🏗️ Edificio seleccionado: {building.image_path}")
                return

    def handle_mouse_up(self):
        self.editor.dragging = False
        self.editor.selected_building = None

    def render_selection_outline(self, screen):
        if self.editor.selected_building:
            building = self.editor.selected_building
            x, y = self.state.camera.apply((building.x, building.y))
            w, h = self.state.camera.scale((building.image.get_width(), building.image.get_height()))

            rect = pygame.Rect(x, y, w, h)

            if self.editor.dragging:
                # 🟩 Fondo verde con transparencia
                overlay = pygame.Surface((w, h), pygame.SRCALPHA)
                overlay.fill((0, 255, 0, 80))  # RGBA → verde transparente
                screen.blit(overlay, (x, y))

                # 🟢 Borde verde grueso
                pygame.draw.rect(screen, (0, 255, 0), rect, 4)
            else:
                # ⚪ Borde blanco fino
                pygame.draw.rect(screen, (255, 255, 255), rect, 2)