import pygame

class TileToolbarEventHandler:
    """
    Manejador de eventos de la barra de herramientas de tiles.
    """
    def __init__(self, toolbar_controller):
        self.controller = toolbar_controller

    def handle_click(self, event):
        """
        Procesa eventos de click en la toolbar, replicando la lógica de apply_click.
        Devuelve True si el evento fue consumido.
        """
        if event.type != pygame.MOUSEBUTTONDOWN or event.button != 1:
            return False
        mouse_pos = event.pos
        print(f"[TOOLBAR CTRL] handle_click called, mouse_pos={mouse_pos}, icon_rects keys={list(self.controller.icon_rects.keys())}, layers_view_open={self.controller.editor.toolbar_state.layers_view_open}")
        for tool, rect in self.controller.icon_rects.items():
            print(f"[TOOLBAR CTRL] rect {tool} at {rect}, collide={rect.collidepoint(mouse_pos)}")
        # Handle layer visibility dropdown clicks
        if self.controller.editor.toolbar_state.layers_view_open:
            for key, rect in self.controller.layer_option_rects.items():
                if rect.collidepoint(mouse_pos):
                    # Toggle visibility para layers o buildings
                    if key == "buildings":
                        self.controller.editor.toolbar_state.show_buildings = not self.controller.editor.toolbar_state.show_buildings
                        print(f"[DEBUG][Layer View] buildings: {'visible' if self.controller.editor.toolbar_state.show_buildings else 'hidden'}")
                    else:
                        self.controller.editor.toolbar_state.visible_layers[key] = not self.controller.editor.toolbar_state.visible_layers[key]
                        print(f"[DEBUG][Layer View] {key}: {'visible' if self.controller.editor.toolbar_state.visible_layers[key] else 'hidden'}")
                    return True
        for tool, rect in self.controller.icon_rects.items():
            if rect.collidepoint(mouse_pos):
                if tool == "view":
                    # Toggle main view panel
                    self.controller.editor.toolbar_state.view_active = not self.controller.editor.toolbar_state.view_active
                elif tool == "view_layers":
                    # Toggle dropdown visibilidad de layers
                    self.controller.editor.toolbar_state.layers_view_open = not self.controller.editor.toolbar_state.layers_view_open
                    print(f"[DEBUG][View layers]: {'open' if self.controller.editor.toolbar_state.layers_view_open else 'closed'}")
                elif tool == "view_collisions":
                    # Ciclar modos de colisión (off -> only -> overlay -> off)
                    if not self.controller.editor.toolbar_state.show_collisions and not self.controller.editor.toolbar_state.show_collisions_overlay:
                        self.controller.editor.toolbar_state.show_collisions = True
                        self.controller.editor.toolbar_state.show_collisions_overlay = False
                    elif self.controller.editor.toolbar_state.show_collisions and not self.controller.editor.toolbar_state.show_collisions_overlay:
                        self.controller.editor.toolbar_state.show_collisions_overlay = True
                    else:
                        self.controller.editor.toolbar_state.show_collisions = False
                        self.controller.editor.toolbar_state.show_collisions_overlay = False
                    # Abrir/cerrar collision picker y cambiar a pincel de colisión
                    if self.controller.editor.toolbar_state.show_collisions or self.controller.editor.toolbar_state.show_collisions_overlay:
                        self.controller.editor.current_tool = "brush"
                        self.controller.editor.toolbar_state.collision_picker_open = True
                        self.controller.editor.picker_state.open = False
                    else:
                        # Cerrar collision picker cuando modo off
                        self.controller.editor.toolbar_state.collision_picker_open = False
                        self.controller.editor.toolbar_state.collision_choice = None
                    # Cerrar dropdown de layers
                    self.controller.editor.toolbar_state.layers_view_open = False
                    mode = 'overlay' if self.controller.editor.toolbar_state.show_collisions_overlay else ('only' if self.controller.editor.toolbar_state.show_collisions else 'off')
                    print(f"[DEBUG][Collision view mode]: {mode}")
                    return True
                else:
                    self.controller.editor.current_tool = tool
                # Manejo de picker para pincel (normal vs colisión)
                if tool == "brush":
                    if self.controller.editor.toolbar_state.show_collisions or self.controller.editor.toolbar_state.show_collisions_overlay:
                        # Toggle collision picker
                        if self.controller.editor.toolbar_state.collision_picker_open:
                            self.controller.editor.toolbar_state.collision_picker_open = False
                        else:
                            self.controller.editor.toolbar_state.collision_picker_open = True
                            self.controller.editor.picker_state.open = False
                    else:
                        # Toggle picker normal
                        if self.controller.editor.picker_state.open:
                            self.controller.editor.picker_state.open = False
                        else:
                            self.controller.editor.toolbar_state.collision_picker_open = False
                            self.controller.editor.toolbar_state.collision_choice = None
                            self.controller.editor.picker_state.open = True
                            self.controller.editor.scroll_offset = 0
                return True
        return False
