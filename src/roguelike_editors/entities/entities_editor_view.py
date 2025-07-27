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
        widget = c.toolbar_view.widget
        margin = 8
        # Si se seleccionó alguna herramienta de entidades, mostrar panels
        if active in ('entities_on_map', 'entities_on_system'):
            rect = widget.icon_rects.get('entities_on_map')

            # Dibujar panels activos
            c.add_remove_controller.render(screen)
            c.picker_controller.draw(screen)
            c.properties_controller.draw(screen)
        