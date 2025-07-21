import pygame

class TilesTitleEventHandler:
    """Event handler for the Tiles Title Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def handle_event(self, ev, *args, **kwargs):
        if ev.type == pygame.KEYDOWN:
            if ev.key == pygame.K_BACKSPACE:
                self.state.title = self.state.title[:-1]
            elif ev.key == pygame.K_RETURN:
                # finalize title input
                pass
            else:
                # append typed character
                self.state.title += ev.unicode
            return True
        return False