import pygame
from typing import Optional

from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
import logging
logger = logging.getLogger(__name__)

class AssetsGridPanelEventHandler:
    """
    Manejador de eventos para el panel de cuadrícula de assets.

    Procesa eventos de ratón para hover, click y double-click
    sobre las celdas de assets, delegando en el controlador
    para mostrar el selector de assets cuando corresponda.
    """

    def __init__(self, controller) -> None:
        self.controller = controller
        self.model = controller.model
        self.view = controller.view
        self.dc_detector = DoubleClickDetector()

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
                # Toggle entre 'sets' y 'no-sets'
                prop_ctrl = self.controller.parent_controller
                ent_id = prop_ctrl.model.selected_id
                if ent_id and ent_id in prop_ctrl.model.player_stats:
                    # Cargar y actualizar JSON
                    path, data, entry = prop_ctrl._load_entity_data(ent_id)
                    assets = entry.setdefault('assets', {})
                    curr = assets.get('active_set', 'sets')
                    new = 'no-sets' if curr == 'sets' else 'sets'
                    assets['active_set'] = new
                    prop_ctrl._save_entity_data(ent_id, entry, path, data)
                    # Actualizar modelo en memoria
                    prop_ctrl.model.player_assets[ent_id]['active_set'] = new
                    prop_ctrl._on_active_set_toggled(ent_id)
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
        entries = getattr(self.model, 'asset_cell_entries', None)
        if not entries:
            return False

        mx, my = event.pos
        hovered: Optional[str] = None
        for rect, key in entries:
            if rect.collidepoint(mx, my):
                hovered = key
                break

        self.model.hovered_asset_cell = hovered
        return hovered is not None

    def _process_cell_click(self, event: pygame.event.Event) -> bool:
        """
        Maneja el click simple: selecciona la celda bajo el cursor.

        :return: True si se hizo click sobre una celda.
        """
        entries = getattr(self.model, 'asset_cell_entries', None)
        if not entries:
            return False

        mx, my = event.pos
        for rect, key in entries:
            if rect.collidepoint(mx, my):
                # Actualizar selección en el modelo
                self.model.selected_asset_cell = key
                logger.debug(f"Clicked asset cell {key}")
                return True
        return False

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
        prop_ctrl.assets_picker_controller.show(
            key, x0, y0, width, prop_ctrl._on_asset_chosen
        )
