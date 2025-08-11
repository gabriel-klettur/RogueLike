import pygame


class SpellsEditorEvents:
    """Top-level events router for the Spells Editor.

    Keep global shortcuts here (e.g., toggle visibility) and let the
    subcontrollers consume more specific interactions.
    """

    def handle_event(self, controller, event: pygame.event.Event) -> bool:
        # Visibility toggle (mirror prior behavior that used F4)
        if event.type == pygame.KEYDOWN and event.key == pygame.K_F4:
            try:
                # The inner controller carries the working model
                model = getattr(controller, 'model', None)
                if model is None:
                    model = getattr(controller, 'inner', None) and controller.inner.model
                if model is None:
                    return False
                model.visible = not model.visible
                model.selected_id = None
                return True
            except Exception:
                return False
        return False

