import pygame

class EntitiesEditorView:
    """
    Vista encargada de renderizar el editor de entidades.
    Separa la lógica de pintura (view) de la lógica de control (controller).
    """
    def __init__(self, controller):
        self.controller = controller

    def render(self, screen: pygame.Surface) -> None:
        """
        Dibuja título, toolbar y panels de entidades según estado activo.
        """
        c = self.controller
        # Título
        c.title_controller.render(screen)
        # Toolbar
        c.toolbar_controller.render(screen)
        active = c.model.toolbar_model.active_tool
        if active in ('entities_on_map', 'entities_on_system'):
            widget = c.toolbar_view.widget
            rect = widget.icon_rects.get('entities_on_map')
            if rect:
                margin = 8
                # Posicionar panel Add/Remove
                ar_x = rect.right + margin
                ar_y = rect.y
                c.add_remove_view.widget.panel.pos = (ar_x, ar_y)
                # Posicionar panel Picker
                ar_w, _ = c.add_remove_view.widget.panel.surface.get_size()
                pick_x = ar_x + ar_w + margin
                pick_y = rect.y
                c.picker_controller.view.draggable_panel.pos = (pick_x, pick_y)
                c.picker_controller.view.x = pick_x
                c.picker_controller.view.y = pick_y
        # Dibuja panels activos
        c.add_remove_controller.render(screen)
        c.picker_controller.draw(screen)
        c.properties_controller.draw(screen)