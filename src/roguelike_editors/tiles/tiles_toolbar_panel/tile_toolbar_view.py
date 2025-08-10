from roguelike_editors.tiles.tiles_editor_config import TOOLS, CLR_SELECTION
from roguelike_ui.widgets.toolbar_panel import ToolbarView


class TileToolbarView:
    """
    Vista de la barra de herramientas de tiles usando el widget genérico ToolbarView.
    """
    def __init__(self, toolbar):
        """
        Args:
            toolbar: Controlador asociado que provee estado y assets.
        """
        self.toolbar = toolbar
        # Crear widget genérico con los iconos provistos por el controlador
        self.widget = ToolbarView(
            controller=self.toolbar,
            items=TOOLS,
            icons=self.toolbar.icons,
            x=self.toolbar.x,
            y=self.toolbar.y,
            size=self.toolbar.size,
            padding=self.toolbar.padding,
            selection_color=CLR_SELECTION,
            name='TilesToolBar'
        )

    def render(self, screen):
        # Delegar render en el widget compartido
        self.widget.render(screen)
        # Exponer rectángulos de iconos para compatibilidad con handlers/tests existentes
        self.toolbar.icon_rects = dict(self.widget.icon_rects)

    def handle_event(self, event):
        """Delegar eventos (drag con botón derecho) al widget genérico."""
        return self.widget.handle_event(event)
