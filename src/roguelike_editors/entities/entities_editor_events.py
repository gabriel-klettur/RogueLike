import pygame
from roguelike_editors.entities.entities_editor_model import EntitiesEditorModel
from roguelike_editors.entities.entities_editor_controller import EntitiesEditorController
import logging
logger = logging.getLogger(__name__)

class EntitiesEditorEventHandler:
    """
    Manejador de eventos global para el editor de entidades.

    Gestiona toggles, panning y delega eventos a EntitiesEditorController.
    """
    def __init__(self, model: EntitiesEditorModel, controller: EntitiesEditorController):
        self.model = model
        self.controller = controller
        self.panning = False
        self.pan_start = (0, 0)
        self.pan_offset_start = (0.0, 0.0)

    def handle(self, events, camera, game_map=None) -> bool:
        """
        Procesa lista de eventos y retorna True si alguno fue consumido.
        """
        for ev in events:
            # Debug left click global
            if ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, 'button', None) == 1:
                logger.debug(f" Left click en {ev.pos}, spawn_mode={self.model.spawn_mode_active}, spawn_entity_type={self.model.spawn_entity_type}")
            # Cerrar con ESC
            if ev.type == pygame.KEYDOWN and ev.key == pygame.K_ESCAPE:
                self.model.active = False
                return True
            # Panning con botón medio
            if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 2:
                self.panning = True
                self.pan_start = ev.pos
                self.pan_offset_start = (camera.offset_x, camera.offset_y)
                return True
            if ev.type == pygame.MOUSEMOTION and self.panning:
                dx = ev.pos[0] - self.pan_start[0]
                dy = ev.pos[1] - self.pan_start[1]
                camera.offset_x = self.pan_offset_start[0] - dx / camera.zoom
                camera.offset_y = self.pan_offset_start[1] - dy / camera.zoom
                return True
            if ev.type == pygame.MOUSEBUTTONUP and ev.button == 2 and self.panning:
                self.panning = False
                return True
            # Delegar evento a MVC
            if self.controller.handle_event(ev):
                return True
        return False
