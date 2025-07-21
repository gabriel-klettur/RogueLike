class TilesCollisionPanelEventHandler:
    """Event handler for the Tiles Collision Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def handle_event(self, ev, *args, **kwargs):
        import pygame

        toolbar_state = self.controller.editor_state.toolbar_state
        # Left click selects collision option
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            pos = ev.pos
            for ch, rect in self.state.option_rects.items():
                if rect.collidepoint(pos):
                    toolbar_state.collision_choice = ch
                    toolbar_state.collision_picker_open = False
                    return True
        # Right click starts drag
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 3:
            pos = ev.pos
            x0, y0 = toolbar_state.collision_picker_pos
            w, h = toolbar_state.collision_picker_panel_size
            if x0 <= pos[0] <= x0 + w and y0 <= pos[1] <= y0 + h:
                toolbar_state.collision_picker_dragging = True
                toolbar_state.collision_picker_drag_offset = (pos[0] - x0, pos[1] - y0)
                return True
        # Dragging movement
        if ev.type == pygame.MOUSEMOTION and toolbar_state.collision_picker_dragging:
            mx, my = ev.pos
            dx, dy = toolbar_state.collision_picker_drag_offset
            toolbar_state.collision_picker_pos = (mx - dx, my - dy)
            return True
        # Stop drag
        if ev.type == pygame.MOUSEBUTTONUP and ev.button == 3:
            if toolbar_state.collision_picker_dragging:
                toolbar_state.collision_picker_dragging = False
                return True
        return False
        pass
