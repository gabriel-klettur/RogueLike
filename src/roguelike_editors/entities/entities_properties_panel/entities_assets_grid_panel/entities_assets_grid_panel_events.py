import pygame
from typing import Any, Optional, Tuple

from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
import logging
from roguelike_editors.entities.services.commands import ToggleActiveSetCommand
logger = logging.getLogger(__name__)

class AssetsGridPanelEventHandler:
    """
    Manejador de eventos para el panel de cuadrícula de assets.

    Procesa eventos de ratón para hover, click y double-click
    sobre las celdas de assets, delegando en el controlador
    para mostrar el selector de assets cuando corresponda.
    """

    def __init__(self, controller: Any) -> None:
        self.controller = controller
        self.model = controller.model
        self.view = controller.view
        self.dc_detector = DoubleClickDetector()

    def _find_cell_key_at_pos(self, pos: Tuple[int, int]) -> Optional[str]:
        """Devuelve la clave de la celda bajo el cursor, o None si no hay celda."""
        entries = getattr(self.model, 'asset_cell_entries', None)
        if not entries:
            return None
        mx, my = pos
        for rect, key in entries:
            if rect.collidepoint(mx, my):
                return key
        return None

    def handle(self, event: pygame.event.Event) -> bool:
        """
        Entrada principal para procesar un evento.

        :param event: Evento de Pygame a procesar.
        :return: True si el evento fue consumido.
        """
        if event.type == pygame.MOUSEMOTION:
            return self._handle_hover(event)

        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            # Detección de click en combobox "Activo"
            rect = getattr(self.model, 'active_set_rect', None)
            if rect and rect.collidepoint(event.pos):
                # Toggle entre 'sets' y 'no-sets' usando comando undoable
                prop_ctrl = self.controller.parent_controller
                ent_id = prop_ctrl.model.selected_id
                if ent_id:
                    prop_ctrl.editor_controller.history.push(ToggleActiveSetCommand(prop_ctrl, ent_id))
                return True
            # Primero, verificar double-click para abrir picker
            if self._process_cell_double_click(event):
                return True
            # Luego, procesar click simple para selección
            if self._process_cell_click(event):
                return True
        return False

    def _handle_hover(self, event: pygame.event.Event) -> bool:
        """
        Detecta si el ratón está sobre alguna celda y actualiza el modelo.

        :return: True si alguna celda está en hover.
        """
        key = self._find_cell_key_at_pos(event.pos)
        self.model.hovered_asset_cell = key
        return key is not None

    def _process_cell_click(self, event: pygame.event.Event) -> bool:
        """
        Maneja el click simple: selecciona la celda bajo el cursor.

        :return: True si se hizo click sobre una celda.
        """
        key = self._find_cell_key_at_pos(event.pos)
        if key is None:
            return False
        # Actualizar selección en el modelo
        self.model.selected_asset_cell = key
        logger.debug(f"Clicked asset cell {key}")
        return True

    def _process_cell_double_click(self, event: pygame.event.Event) -> bool:
        """
        Maneja el double-click: abre el selector de assets en la posición calculada.

        :return: True si se abrió el picker.
        """
        entries = getattr(self.model, 'asset_cell_entries', None)
        if not entries:
            return False
        mx, my = event.pos
        for rect, key in entries:
            if rect.collidepoint(mx, my) and self.dc_detector.is_double_click(key):
                logger.debug(f"Double-click detected for asset cell {key}")
                self._open_assets_picker(key, rect)
                return True
        return False

    def _open_assets_picker(self, key: str, rect: pygame.Rect) -> None:
        """
        Abre el AssetsPickerController en la posición calculada.

        :param key: Clave de la celda seleccionada.
        :param rect: Rectángulo de la celda en UI.
        """
        prop_ctrl = self.controller.parent_controller
        editor_ctrl = prop_ctrl.editor_controller
        picker_model = editor_ctrl.picker_controller.model

        # Posicionar el picker debajo del panel de entidades o de la celda
        base_rect: Optional[pygame.Rect] = getattr(picker_model, 'panel_rect', None)
        if base_rect:
            x0, y0, width = base_rect.x, base_rect.bottom, base_rect.width
        else:
            x0, y0, width = rect.x, rect.bottom, rect.width

        logger.debug(f"Opening assets picker for cell {key} at ({x0}, {y0}) width {width}")
        # Provide label text: hovered entity id or selected one from Entities Picker
        label_provider = lambda: (picker_model.hovered_id or picker_model.selected_id or "")
        prop_ctrl.assets_picker_controller.show(
            key, x0, y0, width, prop_ctrl._on_asset_chosen, label_provider
        )
