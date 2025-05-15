
# Path: src/roguelike_game/systems/editor/buildings/controller/tools/delete_tool.py
import pygame
from roguelike_game.systems.editor.buildings.model.persistence.save_buildings_to_json import save_buildings_to_json
from roguelike_engine.config import BUILDINGS_DATA_PATH

class DeleteTool:
    def __init__(self, state, editor_state, camera):
        self.state = state
        self.editor = editor_state
        self.camera = camera

    def delete_building_at_mouse(self, entities):
        mx, my = pygame.mouse.get_pos()
        world_x = mx / self.camera.zoom + self.camera.offset_x
        world_y = my / self.camera.zoom + self.camera.offset_y

        # iteramos sobre la lista de edificios
        for b in reversed(entities.buildings):
            if b.rect.collidepoint(world_x, world_y):
                entities.buildings.remove(b)
                print(f"❌ Edificio eliminado en ({int(world_x)}, {int(world_y)})")                

                return

        print("🕳️ No se encontró edificio para eliminar")