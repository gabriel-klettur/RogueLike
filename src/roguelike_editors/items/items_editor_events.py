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

        # Enrutado de rueda del ratón: si el ratón está sobre propiedades, scroll allí
        if event.type == pygame.MOUSEWHEEL:
            props_rect = getattr(props.model, 'panel_rect', None)
            mx, my = pygame.mouse.get_pos()
            if props_rect:
                over_props = props_rect.collidepoint(mx, my)
                logger.debug(f"[ItemsEditorEvents] MOUSEWHEEL pos=({mx},{my}) over_props={over_props} props_rect={props_rect}")
                if over_props:
                    props.handle_event(event)
                    return True
            else:
                logger.debug("[ItemsEditorEvents] MOUSEWHEEL without props.panel_rect; routing to picker")
            controller.picker_controller.handle_event(event)
            logger.debug("[ItemsEditorEvents] MOUSEWHEEL routed to picker")
            return True

        # Hit-test por orden z: propiedades encima del picker
        props_rect = getattr(props.model, 'panel_rect', None)
        picker_rect = getattr(controller.picker_controller.picker_state, 'rect', None)

        if hasattr(event, 'pos') and isinstance(getattr(event, 'pos'), (tuple, list)):
            mx, my = event.pos
            # 1) Propiedades primero
            if props_rect and props_rect.collidepoint(mx, my):
                props.handle_event(event)
                return True
            # 2) Siempre delegar al picker (aunque aún no haya rect válido)
            controller.picker_controller.handle_event(event)
            # 3) Click fuera limpia solo si conocemos el rect del picker
            if picker_rect and not picker_rect.collidepoint(mx, my):
                if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                    model.selected_item_id = None
            return True

        # Resto de eventos (teclado para navegar picker, rueda, etc.)
        controller.picker_controller.handle_event(event)
        # Notar que props.handle_event se auto-limita por hit-test y text_input
        return True

