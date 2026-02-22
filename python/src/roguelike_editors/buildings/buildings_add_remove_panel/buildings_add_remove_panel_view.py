"""
Vista para el panel de Add/Remove del Buildings Editor usando ToolbarView (estilo Items).
"""

import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_ui.widgets.toolbar_panel import ToolbarView


class BuildingsAddRemovePanelView:
    def __init__(self, state, editor_state, model, editor_view):
        self.state = state
        self.editor = editor_state
        self.model = model
        # Referencia al BuildingEditorView (para fallback de posicionamiento)
        self.editor_view = editor_view
        # Referencia (opcional) al BuildingsToolBarPanelView para alinear a la derecha
        self.toolbar_view = None  # se inyecta desde BuildingEditorManager
        # El ToolbarView necesita un controller con is_active(tool); se inyecta después
        self.controller = None

        # Config básica
        self.size = getattr(self.model, 'icon_size', 64)
        self.padding = getattr(self.model, 'padding', 8)

        # Cargar iconos
        def _load(path: str):
            try:
                return load_image(path, scale=(self.size, self.size))
            except Exception:
                surf = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
                surf.fill((90, 90, 90, 180))
                return surf

        self.icons = {
            'add_building': _load('assets/ui/add_building.png'),
            'remove_building': _load('assets/ui/remove_building.png'),
            'add_building_on_system': _load('assets/ui/add_building_on_system.png'),
        }

        # Posición inicial (fallback); se recalcula en render
        self.x, self.y = 10, 80

        # Widget compartido (vertical por defecto)
        self.widget = ToolbarView(
            controller=self.controller,  # corregido tras inyección en el controller
            items=self.model.tools,
            icons=self.icons,
            x=self.x,
            y=self.y,
            size=self.size,
            padding=self.padding,
            name='BuildingsAddRemovePanel',
        )

    def _compute_position_next_to_toolbar(self, screen: pygame.Surface) -> tuple[int, int]:
        """Calcula la posición a la DERECHA del toolbar de Buildings.
        Prioriza el layout final basado en el título (igual que el toolbar),
        usando el ANCHO real del toolbar para alinear perfectamente.
        Fallback: usar la posición actual del toolbar; luego defaults.
        """
        tb_view = getattr(self, 'toolbar_view', None)
        # 1) Si tenemos title_rect, replicamos el mismo anclaje que el toolbar y sumamos su ancho
        try:
            title_rect = getattr(self.editor_view, '_last_title_rect', None)
            if title_rect is None and hasattr(self.editor_view, 'title_view'):
                title_widget = getattr(self.editor_view.title_view, 'widget', None)
                if title_widget is not None and hasattr(title_widget, 'rect'):
                    title_rect = title_widget.rect
        except Exception:
            title_rect = None
        if title_rect is not None and tb_view is not None and hasattr(tb_view, 'widget'):
            try:
                tb_w = tb_view.widget.panel.surface.get_width()
                return (title_rect.left + tb_w + 8, title_rect.bottom + 8)
            except Exception:
                pass
        # 2) Si no hay title_rect, usamos la posición actual del toolbar
        if tb_view is not None and hasattr(tb_view, 'widget'):
            try:
                tb_pos = tb_view.widget.panel.pos or (tb_view.widget.x, tb_view.widget.y)
                tb_w, _ = tb_view.widget.panel.surface.get_size()
                return (tb_pos[0] + tb_w + 8, tb_pos[1])
            except Exception:
                pass
        # 3) Fallback: bajo el título
        if title_rect is not None:
            return (title_rect.left, title_rect.bottom + 8)
        # 4) Último fallback
        return (self.x, self.y)

    def _publish_panel_rect(self) -> None:
        """Publica el rect del panel para que el picker pueda alinearse a la derecha."""
        try:
            panel_pos = self.widget.panel.pos or (self.widget.x, self.widget.y)
            panel_size = self.widget.panel.surface.get_size()
            rect = pygame.Rect(panel_pos, panel_size)
            self.model.panel_rect = rect
            self.editor.add_remove_panel_rect = rect
        except Exception:
            pass

    def render(self, screen: pygame.Surface) -> None:
        if not getattr(self.model, 'active', False):
            return
        # Reposicionar dinámicamente junto al toolbar de Buildings
        desired_x, desired_y = self._compute_position_next_to_toolbar(screen)
        try:
            # ToolbarView usa panel.pos
            self.widget.panel.pos = (desired_x, desired_y)
        except Exception:
            self.widget.x, self.widget.y = desired_x, desired_y

        # Renderizar toolbar vertical y exponer rects para eventos
        self.widget.render(screen)
        # Mantener icon_rects en el modelo para el event handler
        try:
            self.model.icon_rects = dict(self.widget.icon_rects)
        except Exception:
            pass
        # Publicar rect del panel
        self._publish_panel_rect()

