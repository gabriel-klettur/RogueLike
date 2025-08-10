import pygame
import logging

logger = logging.getLogger(__name__)

class EntitiesAssetsPickerPanelEventHandler:
    """Event handler para el picker de assets de entidades."""
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view
        # Conectar callbacks del FileSystemPicker a las acciones del modelo
        # Doble clic / Enter en archivo -> emitir on_asset_chosen
        def _on_open(path):
            if self.model.on_asset_chosen:
                logger.debug(f" Invoking on_asset_chosen callback for key={self.model.key}, path={path}")
                self.model.on_asset_chosen(self.model.key, path)
        self.view.fs_view.on_open = _on_open
        # Selección (click o teclado) ya sincroniza self.model.fs_model.selected en FileSystemPickerView
        self.view.fs_view.on_select = lambda idx: None

    def handle(self, event: pygame.event.Event) -> bool:
        """Delegar eventos a FileSystemPicker/PickerPanel y gestionar cierre/ocultación."""
        # Cerrar con ESC
        if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
            self.controller.hide()
            return True

        # Rect del panel dibujado (incluyendo posibles labels/footer)
        if self.model.panel_rect is not None:
            panel_rect = self.model.panel_rect
        else:
            x, y = self.model.pos
            surf = self.view.fs_view.panel.surface
            w, h = surf.get_size()
            panel_rect = pygame.Rect(x, y, w, h)

        # Teclado: siempre delegar al picker cuando está visible
        if event.type == pygame.KEYDOWN:
            self.view.fs_view.handle_event(event, self.model.pos)
            return True

        # Rueda/Movimiento/Clic: decidir por posición
        if event.type in (pygame.MOUSEMOTION, pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            # Eventos de ratón sin pos (MOUSEWHEEL) usan el mouse global para decidir
            mx, my = pygame.mouse.get_pos() if not hasattr(event, 'pos') else event.pos
            if panel_rect.collidepoint(mx, my):
                # Delegar al FS picker (traduce coords internas y propaga a PickerPanel)
                self.view.fs_view.handle_event(event, self.model.pos)
                return True
            # Clic fuera -> ocultar
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                self.controller.hide()
                return True

        return False