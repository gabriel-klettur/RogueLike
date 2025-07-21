import pygame





from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_view import TileToolbarView
from roguelike_editors.tiles.tiles_picker_panel.tile_picker_view import TilePickerView
from roguelike_editors.tiles.tile_outline_view import TileOutlineView


class TileEditorView:
    def __init__(self, controller, editor_state):
        self.controller = controller        
        self.editor     = editor_state

        self.toolbar_view = TileToolbarView(controller.toolbar)
        self.picker_view  = TilePickerView(editor_state.picker_state, controller.picker.assets)
        self.outline_view = TileOutlineView(controller, editor_state)        

    def render(self, screen, camera, map):
        if not self.editor.active:
            return

        # Render title panel
        self.controller.title_controller.render(screen)
        

        self.toolbar_view.render(screen)
        # Normal tile picker
        if self.editor.picker_state.open:
            self.picker_view.render(screen)

        if self.editor.toolbar_state.view_active:
            self.controller.view_panel_controller.render(screen, camera, map)
            

        # Render layers panel
        if self.editor.toolbar_state.layers_view_open:
            self.controller.layers_panel_controller.render(screen)
            
        # Render collision panel
        if self.editor.toolbar_state.collision_picker_open:
            self.controller.collision_panel_controller.render(screen)
            

        self.outline_view.render(screen, camera, map)    