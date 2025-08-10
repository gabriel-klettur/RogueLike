

import pygame
import logging
logger = logging.getLogger(__name__)

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
                logger.info(f"❌ Edificio eliminado en ({int(world_x)}, {int(world_y)})")                

                return

        logger.info("🕳️ No se encontró edificio para eliminar")