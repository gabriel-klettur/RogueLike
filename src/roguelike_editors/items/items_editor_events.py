import pygame
from typing import Any
import logging
logger = logging.getLogger(__name__)


class ItemsEditorEvents:
    """Enrutador de eventos para el Editor de Ítems."""

    def handle_event(self, controller: Any, event: pygame.event.Event) -> bool:
        model = controller.model

        # Atajos globales (funcionan incluso si no es visible para abrir/cerrar)
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_F7:
                controller.toggle()
                return True
            if event.key == pygame.K_ESCAPE and model.visible:
                controller.hide()
                return True

        if not model.visible:
            return False

        # Si el panel de propiedades está editando texto, priorizarlo
        props = controller.properties_controller
        if getattr(props, 'text_input', None) and props.text_input.active:
            props.handle_event(event)
            return True

        # Enrutado de rueda del ratón: si el ratón está sobre propiedades o instancias, scroll allí
        if event.type == pygame.MOUSEWHEEL:
            props_rect = getattr(props.model, 'panel_rect', None)
            mx, my = pygame.mouse.get_pos()
            if props_rect:
                over_props = props_rect.collidepoint(mx, my)
                logger.debug(f"[ItemsEditorEvents] MOUSEWHEEL pos=({mx},{my}) over_props={over_props} props_rect={props_rect}")
                if over_props:
                    props.handle_event(event)
                    return True
            # Instances panel wheel routing
            try:
                list_rect, params_rect = controller.instances_controller.get_layout_rects()
            except Exception:
                list_rect = params_rect = None
            if list_rect and list_rect.collidepoint(mx, my) or (params_rect and params_rect.collidepoint(mx, my)):
                handled = controller.instances_controller.handle_event(event)
                if handled:
                    return True
            logger.debug("[ItemsEditorEvents] MOUSEWHEEL routing to picker by default")
            controller.picker_controller.handle_event(event)
            logger.debug("[ItemsEditorEvents] MOUSEWHEEL routed to picker")
            return True

        # Hit-test por orden z: propiedades encima del picker
        props_rect = getattr(props.model, 'panel_rect', None)
        picker_rect = getattr(controller.picker_controller.picker_state, 'rect', None)
        try:
            inst_list_rect, inst_params_rect = controller.instances_controller.get_layout_rects()
        except Exception:
            inst_list_rect = inst_params_rect = None

        if hasattr(event, 'pos') and isinstance(getattr(event, 'pos'), (tuple, list)):
            mx, my = event.pos
            # 1) Propiedades primero
            if props_rect and props_rect.collidepoint(mx, my):
                props.handle_event(event)
                return True
            # 2) Panel de instancias (lista/params)
            if (inst_list_rect and inst_list_rect.collidepoint(mx, my)) or (inst_params_rect and inst_params_rect.collidepoint(mx, my)):
                if controller.instances_controller.handle_event(event):
                    return True
            # 3) Siempre delegar al picker (aunque aún no haya rect válido)
            controller.picker_controller.handle_event(event)
            # 4) Click fuera limpia solo si estamos fuera de picker, props e instancias
            outside_picker = picker_rect and not picker_rect.collidepoint(mx, my)
            outside_props = props_rect and not props_rect.collidepoint(mx, my)
            outside_instances = (
                (inst_list_rect is None or not inst_list_rect.collidepoint(mx, my)) and
                (inst_params_rect is None or not inst_params_rect.collidepoint(mx, my))
            )
            if outside_picker and (outside_props or props_rect is None) and outside_instances:
                if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                    model.selected_item_id = None
            return True

        # Resto de eventos (teclado para navegar picker, rueda, etc.)
        # Permitir que el panel de instancias procese teclas/otros eventos (por ejemplo, ediciones de params)
        controller.instances_controller.handle_event(event)
        controller.picker_controller.handle_event(event)
        # Notar que props.handle_event se auto-limita por hit-test y text_input
        return True

