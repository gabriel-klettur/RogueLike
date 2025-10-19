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
                # Push a undo stack (índice original)
                if not hasattr(self.editor, 'undo_stack'):
                    self.editor.undo_stack = []
                try:
                    idx = entities.buildings.index(b)
                except ValueError:
                    idx = len(entities.buildings) - 1
                self.editor.undo_stack.append((b, idx))
                # Eliminar
                entities.buildings.remove(b)
                # Limpiar selección/hover si corresponde
                try:
                    if getattr(self.editor, 'selected_building', None) is b:
                        self.editor.selected_building = None
                    if getattr(self.editor, 'hovered_building', None) is b:
                        self.editor.hovered_building = None
                except Exception:
                    pass
                # Pulso para el tutorial
                try:
                    setattr(self.editor, 'tutorial_deleted_pulse', True)
                except Exception:
                    pass
                logger.info(f"❌ DeleteTool: edificio eliminado en ({int(world_x)}, {int(world_y)}) | idx={idx}. Usa Ctrl+Z para deshacer.")
                return

        logger.info("🕳️ No se encontró edificio para eliminar")