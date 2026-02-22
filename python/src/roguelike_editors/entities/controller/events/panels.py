from __future__ import annotations
import pygame


def handle_panels(editor: "EntitiesEditorController", event: pygame.event.Event) -> bool:
    """Delegación a subcontroladores de UI: título, toolbar y tutorial."""
    if editor.title_controller.handle_event(event):
        return True
    if editor.toolbar_controller.handle_event(event):
        return True
    try:
        if editor.tutorial_controller.handle_event(event):
            return True
    except Exception:
        pass
    return False
