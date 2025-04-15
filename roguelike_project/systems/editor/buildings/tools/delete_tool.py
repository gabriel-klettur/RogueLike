# roguelike_project/editor/tools/delete_tool.py

import pygame

class DeleteTool:
    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state

    def delete_building_at_mouse(self):
        mx, my = pygame.mouse.get_pos()
        world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
        world_y = my / self.state.camera.zoom + self.state.camera.offset_y

        for building in reversed(self.state.buildings):
            if building.rect.collidepoint(world_x, world_y):
                self.state.buildings.remove(building)
                print(f"❌ Edificio eliminado en ({int(world_x)}, {int(world_y)})")
                return

        print("🕳️ No se encontró edificio para eliminar")
