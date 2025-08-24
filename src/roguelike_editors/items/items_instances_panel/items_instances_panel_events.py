import os
import pygame
from typing import Any
import logging

from roguelike_ui.services.json_persistence import save_to_json
from roguelike_engine.config.config_tiles import TILE_SIZE

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
            # Si es click izquierdo, iniciar enfoque de cámara mientras se mantenga presionado
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                try:
                    # Preferir posición absoluta (en píxeles). Si no existe, convertir tile -> píxeles.
                    x = y = None
                    pos = inst_data.get('position')
                    if isinstance(pos, dict):
                        px = pos.get('x')
                        py = pos.get('y')
                        if px is not None and py is not None:
                            x = float(px)
                            y = float(py)
                    if x is None or y is None:
                        tile = inst_data.get('tile', {})
                        tx = tile.get('x')
                        ty = tile.get('y')
                        if tx is not None and ty is not None:
                            # Convertir al centro del tile para un enfoque más natural
                            x = (float(tx) + 0.5) * TILE_SIZE
                            y = (float(ty) + 0.5) * TILE_SIZE
                    if controller.on_start_hold_focus and x is not None and y is not None:
                        controller.on_start_hold_focus(x, y)
                except Exception:
                    logger.exception("Failed to start hold focus from map_ui click")
            # cargar valores al editor de params
            params = inst_data.get('params', {})
            controller.params_ui.load_values(params)
            return True

        # 2) Delegar al editor de parámetros (solo si existe panel de params)
        if getattr(controller.model, 'params_rect', None) is not None and controller.params_ui.handle_event(event):
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
