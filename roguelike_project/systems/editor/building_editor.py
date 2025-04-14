# roguelike_project/editor/building_editor.py

import pygame

class BuildingEditor:
    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state

    def update(self):
        # Si se está arrastrando, mover el edificio seleccionado con el mouse
        if self.editor.dragging and self.editor.selected_building:
            mx, my = pygame.mouse.get_pos()
            world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
            world_y = my / self.state.camera.zoom + self.state.camera.offset_y
            self.editor.selected_building.x = world_x - self.editor.offset_x
            self.editor.selected_building.y = world_y - self.editor.offset_y

    def handle_mouse_down(self, pos):
        mx, my = pos
        world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
        world_y = my / self.state.camera.zoom + self.state.camera.offset_y

        for building in reversed(self.state.buildings):  # Revisar en orden inverso (z-index visual)
            if building.rect.collidepoint(world_x, world_y):
                self.editor.selected_building = building
                self.editor.dragging = True
                self.editor.offset_x = world_x - building.x
                self.editor.offset_y = world_y - building.y
                print(f"🏗️ Edificio seleccionado: {building.image}")
                return

        print("❌ No se seleccionó ningún edificio")

    def handle_mouse_up(self):
        if self.editor.dragging:
            print("📍 Edificio reposicionado.")
        self.editor.dragging = False
        self.editor.selected_building = None

    def render_selection_outline(self, screen):
        # Dibuja un rectángulo blanco alrededor del edificio seleccionado
        if self.editor.selected_building:
            building = self.editor.selected_building
            x, y = self.state.camera.apply((building.x, building.y))
            w, h = self.state.camera.scale((building.image.get_width(), building.image.get_height()))
            pygame.draw.rect(screen, (255, 255, 255), pygame.Rect(x, y, w, h), 2)
