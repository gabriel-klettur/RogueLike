from __future__ import annotations
import pygame


def handle_keyboard(editor: "EntitiesEditorController", event: pygame.event.Event) -> bool:
    """Atajos globales: Undo/Redo y reload neutrals."""
    if event.type != pygame.KEYDOWN:
        return False
    mods = pygame.key.get_mods()
    if mods & pygame.KMOD_CTRL and event.key == pygame.K_z:
        if editor.history.undo():
            try:
                setattr(editor.model, 'tutorial_undo_pulse', True)
            except Exception:
                pass
        return True
    if mods & pygame.KMOD_CTRL and (event.key == pygame.K_y or (mods & pygame.KMOD_SHIFT and event.key == pygame.K_z)):
        if editor.history.redo():
            try:
                setattr(editor.model, 'tutorial_redo_pulse', True)
            except Exception:
                pass
        return True
    if mods & pygame.KMOD_CTRL and event.key == pygame.K_r:
        try:
            editor.model.reload_neutrals()
            editor.render(editor.game.screen)
        except Exception:
            pass
        return True
    return False
