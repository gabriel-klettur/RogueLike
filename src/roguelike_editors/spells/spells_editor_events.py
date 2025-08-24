import pygame


class SpellsEditorEvents:
    """Top-level events router for the Spells Editor.

    Note: Global toggle shortcuts are handled centrally. This router delegates
    specific interactions to subcontrollers and does not manage visibility.
    """

    def handle_event(self, controller, event: pygame.event.Event) -> bool:
        return False

