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

        ts = self.controller.editor_state.toolbar_state



        # Handle toolbar icon clicks
        for tool, rect in self.controller.icon_rects.items():
            if rect.collidepoint(mouse_pos):
                if tool == "view":
                    ts.view_active = not ts.view_active
                elif tool == "view_layers":
                    ts.layers_view_open = not ts.layers_view_open
                elif tool == "view_collisions":
                    # Ciclar modos de colisión (off -> only -> overlay -> off)
                    if not ts.show_collisions and not ts.show_collisions_overlay:
                        ts.show_collisions = True
                        ts.show_collisions_overlay = False
                    elif ts.show_collisions and not ts.show_collisions_overlay:
                        ts.show_collisions_overlay = True
                    else:
                        ts.show_collisions = False
                        ts.show_collisions_overlay = False
                    # Abrir/cerrar collision picker y cambiar a pincel de colisión
                    if ts.show_collisions or ts.show_collisions_overlay:
                        self.controller.editor_state.current_tool = "brush"
                        ts.collision_picker_open = True
                        self.controller.editor_state.picker_state.open = False
                    else:
                        ts.collision_picker_open = False
                        ts.collision_choice = None
                    ts.layers_view_open = False
                else:
                    self.controller.editor_state.current_tool = tool

                if tool == "brush":
                    if ts.show_collisions or ts.show_collisions_overlay:
                        # Toggle collision picker
                        ts.collision_picker_open = not ts.collision_picker_open
                        if ts.collision_picker_open:
                            self.controller.editor_state.picker_state.open = False
                    else:
                        # Toggle brush size panel
                        self.controller.editor_controller.size_panel_controller.toggle()
                        visible = self.controller.editor_controller.size_panel_controller.state.visible
                        # Open/close tile picker panel in sync with brush panel
                        self.controller.editor_state.picker_state.open = visible
                        # Select or deselect brush tool based on panel visibility
                        self.controller.editor_state.current_tool = "brush" if visible else "select"
                return True

        return False
