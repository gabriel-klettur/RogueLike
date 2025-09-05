"""
Vista para la toolbar de Spells.
"""

import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_engine.utils.loader import load_image
from roguelike_editors.entities.services.constants import UI_MARGIN


class SpellsToolBarPanelView:
    """Vista de la toolbar de Spells, posicionada bajo la barra de título."""
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model
        # Posición por defecto; se recalcula en render
        self.x, self.y = 10, 10
        self.size = 64
        self.padding = 8
        icon_paths = {
            'tutorial_spells': 'assets/ui/tutorials_button.png',
            'spells_on_map': 'assets/ui/spells_on_map_icon.png',
            'undo': 'assets/ui/undo.png',
            'redo': 'assets/ui/redo.png',
        }
        self.icons = {}
        for tool in self.model.tools:
            path = icon_paths.get(tool)
            if path:
                try:
                    img = load_image(path, scale=(self.size, self.size))
                except Exception:
                    img = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
                    img.fill((100, 100, 100, 150))
            else:
                img = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
                img.fill((100, 100, 100, 150))
            self.icons[tool] = img

        self.widget = ToolbarView(
            controller=self.controller,
            items=self.model.tools,
            icons=self.icons,
            x=self.x,
            y=self.y,
            size=self.size,
            padding=self.padding,
            name='SpellsToolBarPanel',
        )

    def render(self, screen: pygame.Surface):
        # Calcular tamaño total de íconos
        total_w = self.size * len(self.model.tools) + self.padding * (len(self.model.tools) - 1)
        # Obtener title_rect desde la vista del editor
        editor_view = getattr(self.controller, 'view', None)
        title_rect = getattr(editor_view, 'title_rect', None)
        if title_rect is not None:
            # Alinear el borde izquierdo con el título y posicionar justo debajo
            new_x = int(title_rect.left)
            new_y = int(title_rect.bottom + UI_MARGIN)
            # Actualizar tanto atributos como panel.pos (usado en render)
            self.widget.x = new_x
            self.widget.y = new_y
            try:
                self.widget.panel.pos = (new_x, new_y)
            except Exception:
                pass
        else:
            # Fallback: centrar en pantalla y usar posición por defecto en Y
            try:
                sw = screen.get_width()
            except Exception:
                sw = 1280
            new_x = (sw - total_w) // 2
            new_y = self.y
            self.widget.x = new_x
            self.widget.y = new_y
            try:
                self.widget.panel.pos = (new_x, new_y)
            except Exception:
                pass
        self.widget.render(screen)

    def handle_event(self, event):
        return self.widget.handle_event(event)

