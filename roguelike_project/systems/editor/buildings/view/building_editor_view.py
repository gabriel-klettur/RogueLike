import pygame
from roguelike_project.systems.editor.buildings.view.tools.default_tool_view import DefaultToolView
from roguelike_project.systems.editor.buildings.view.tools.resize_tool_view  import ResizeToolView
from roguelike_project.systems.editor.buildings.view.tools.split_tool_view   import SplitToolView
from roguelike_project.systems.editor.buildings.view.tools.z_tool_view       import ZToolView

class BuildingEditorView:
    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state
        self.default_view  = DefaultToolView(state, editor_state)
        self.resize_view   = ResizeToolView(state, editor_state)
        self.split_view    = SplitToolView(state, editor_state)
        self.z_bottom_view = ZToolView(state, editor_state, target="bottom")
        self.z_top_view    = ZToolView(state, editor_state, target="top")

    def render(self, screen):
        if not self.editor.active:
            return

        cam = self.state.camera
        # Renderizado de cada edificio
        for b in self.state.buildings:
            x, y = cam.apply((b.x, b.y))
            w, h = cam.scale(b.image.get_size())
            # contorno general del edificio
            pygame.draw.rect(screen, (255, 255, 255), pygame.Rect(x, y, w, h), 1)

            # Render de cada handle/herramienta
            self.default_view.render_reset_handle(screen, b)
            self.resize_view.render_resize_handle(screen, b)
            self.split_view.render(screen, b)
            self.z_bottom_view.render(screen, b)
            self.z_top_view.render(screen, b)
