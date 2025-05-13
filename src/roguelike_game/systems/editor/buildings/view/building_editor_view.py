# Path: src/roguelike_game/systems/editor/buildings/view/building_editor_view.py
import pygame
from roguelike_game.systems.editor.buildings.view.tools.default_tool_view import DefaultToolView

from roguelike_game.systems.editor.buildings.view.tools.split_tool_view   import SplitToolView
from roguelike_game.systems.editor.buildings.view.tools.z_tool_view       import ZToolView

#! Picker
from roguelike_game.systems.editor.buildings.view.picker.picker_view      import PickerView

class BuildingEditorView:
    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state
        self.default_view  = DefaultToolView(state, editor_state)

        self.split_view    = SplitToolView(state, editor_state)
        self.z_bottom_view = ZToolView(state, editor_state, target="bottom")
        self.z_top_view    = ZToolView(state, editor_state, target="top")
        
        #! Picker
        self.picker_view = PickerView(editor_state)

    def render(self, screen, camera, buildings):
        if not self.editor.active:
            return
        
        # Si estamos en modo picker, pintamos el selector completo
        if self.editor.picker_active:
            self.picker_view.render(screen, camera)            
        
        # Renderizado de cada edificio
        for b in buildings:
            x, y = camera.apply((b.x, b.y))
            w, h = camera.scale(b.image.get_size())
            rect = pygame.Rect(x, y, w, h)
            # Si es el hovered_building, dibuja contorno cian grueso
            if hasattr(self.editor, 'hovered_building') and b == self.editor.hovered_building:
                pygame.draw.rect(screen, (0, 255, 255), rect, 4)
            # contorno general del edificio
            pygame.draw.rect(screen, (255, 255, 255), rect, 1)

            # Render de cada handle/herramienta
            self.default_view.render_reset_handle(screen, b, camera)

            self.split_view.render(screen, b, camera)
            self.z_bottom_view.render(screen, b, camera)
            self.z_top_view.render(screen, b, camera)