import pygame
from roguelike_editors.tiles.tiles_editor_config import TOOLS, CLR_SELECTION
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_ui.panel import DraggablePanel  # re-export for tests monkeypatch
from roguelike_ui.widgets.button import Button  # re-export for tests monkeypatch


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
        # Compat: exponer los botones como en la vista específica anterior
        # para que los tests puedan inspeccionarlos/monkeypatchearlos.
        self.buttons = self.widget.buttons

    def render(self, screen):
        # Delegar render en el widget compartido
        self.widget.render(screen)
        # Exponer rectángulos de iconos para compatibilidad con handlers/tests existentes
        self.toolbar.icon_rects = dict(self.widget.icon_rects)

    def handle_event(self, event):
        """Delegar eventos (drag con botón derecho) al widget genérico."""
        return self.widget.handle_event(event)

    # --- Métodos helper para compatibilidad con tests previos ---
    def _compute_icon_rect(self, x: int, y: int, index: int) -> pygame.Rect:
        """
        Devuelve el rect del icono en la columna única, desplazado por índice
        respetando tamaño y padding configurados en el controlador.
        """
        size = self.toolbar.size
        pad = self.toolbar.padding
        return pygame.Rect(x, y + index * (size + pad), size, size)

    def _get_panel_position(self):
        """Obtiene la posición actual del panel desde el estado o fallback al layout del controlador."""
        ts = self.toolbar.editor_state.toolbar_state
        return ts.pos if getattr(ts, 'pos', None) else (self.toolbar.x, self.toolbar.y)
