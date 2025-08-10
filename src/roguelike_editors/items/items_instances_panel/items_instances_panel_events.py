import os
import pygame
from typing import Any
import logging

from roguelike_ui.services.json_persistence import save_to_json

logger = logging.getLogger(__name__)


class ItemsInstancesPanelEvents:
    """
    Enrutador de eventos para el panel de instancias del mapa y editor de parámetros.
    """
    def handle_event(self, controller: Any, event: pygame.event.Event) -> bool:
        model = controller.model
        if not model.visible:
            return False

        # 1) Delegar a la lista de instancias del mapa
        inst = controller.map_ui.handle_event(event)
        if inst:
            inst_data = controller.map_ui.data.get(inst, {})
            # Seleccionar ítem en el grid de definiciones a través del orquestador
            item_def = inst_data.get('item_id')
            if item_def and controller.on_select_item_id:
                try:
                    controller.on_select_item_id(item_def)
                except Exception:
                    logger.exception("on_select_item_id callback failed from map_ui selection")
            # cargar valores al editor de params
            params = inst_data.get('params', {})
            controller.params_ui.load_values(params)
            return True

        # 2) Delegar al editor de parámetros
        if controller.params_ui.handle_event(event):
            try:
                new_params = controller.params_ui.get_values()
                inst_id = controller.map_ui.selected_instance
                if inst_id:
                    # actualizar datos en memoria y persistir
                    entry = controller.map_ui.data.get(inst_id, {})
                    entry['params'] = new_params
                    path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
                    save_to_json(path, inst_id, entry)
                    # refrescar lista de mapa
                    controller.map_ui.load()
                return True
            except Exception as e:
                logger.error(f"Params invalidos: {e}")
                return True
        return False
