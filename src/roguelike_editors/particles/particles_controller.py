import pygame
from .particles_model import ParticlesEditorModel
from .particles_view import ParticlesEditorView
from .particles_tool_bar_panel.particles_tool_bar_panel_model import ParticlesToolBarPanelModel
from .particles_tool_bar_panel.particles_tool_bar_panel_view import ParticlesToolBarPanelView
from .particles_tool_bar_panel.particles_tool_bar_panel_events import ParticlesToolBarPanelEventHandler
from .particles_tool_bar_panel.particles_tool_bar_panel_controller import ParticlesToolBarPanelController
from .particles_picker_panel.particles_picker_controller import ParticlesPickerController
from roguelike_editors.entities.services.constants import UI_MARGIN

class ParticlesEditorController:
    """Minimal controller for Particles Editor."""
    def __init__(self, font: pygame.font.Font | None = None):
        self.model = ParticlesEditorModel()
        self.view = ParticlesEditorView(self.model)
        self.font = font
        # Toolbar MVC
        self.particles_toolbar_model = ParticlesToolBarPanelModel()
        self.particles_toolbar_view = ParticlesToolBarPanelView(self, self.particles_toolbar_model)
        self.particles_toolbar_events = ParticlesToolBarPanelEventHandler(self, self.particles_toolbar_model)
        self.particles_toolbar_controller = ParticlesToolBarPanelController(
            self, self.particles_toolbar_model, self.particles_toolbar_view, self.particles_toolbar_events
        )
        # Picker MVC (lista de presets de partículas)
        self.particles_picker_controller = ParticlesPickerController(self.font)

    def toggle_visible(self):
        self.model.visible = not bool(self.model.visible)

    def handle_event(self, event: pygame.event.Event) -> None:
        # Solo manejar eventos del toolbar cuando el editor está visible
        if not getattr(self.model, 'visible', False):
            return
        # Delegar eventos al controlador del toolbar
        try:
            if self.particles_toolbar_controller.handle_event(event):
                return
        except Exception:
            pass
        # Reenviar eventos al picker cuando esté activo
        try:
            if getattr(self.particles_toolbar_model, 'active_tool', None) == 'particles_list':
                if self.particles_picker_controller.handle_event(event):
                    return
        except Exception:
            pass
        return

    def draw(self, screen: pygame.Surface) -> None:
        # Renderizar solo cuando el editor esté visible
        if not getattr(self.model, 'visible', False):
            return
        self.view.draw(screen)
        # Render toolbar below the title
        try:
            self.particles_toolbar_controller.render(screen)
        except Exception:
            pass
        # Render picker grid a la derecha de la toolbar cuando la herramienta esté activa
        try:
            if getattr(self.particles_toolbar_model, 'active_tool', None) == 'particles_list':
                tb_widget = getattr(self.particles_toolbar_view, 'widget', None)
                left_x = None
                top_y = None
                if tb_widget is not None:
                    try:
                        # Intentar usar el tamaño real del panel
                        w, h = tb_widget.panel.surface.get_size()
                        left_x = int(tb_widget.x + w + UI_MARGIN)
                        top_y = int(tb_widget.y)
                    except Exception:
                        # Fallback al tamaño base del icono
                        left_x = int(tb_widget.x + getattr(self.particles_toolbar_view, 'size', 48) + UI_MARGIN)
                        top_y = int(tb_widget.y)
                if left_x is None or top_y is None:
                    # Fallback general, debajo del título
                    title_rect = getattr(self.view, 'title_rect', None)
                    if title_rect is not None:
                        left_x = int(title_rect.left)
                        top_y = int(title_rect.bottom + UI_MARGIN)
                    else:
                        left_x, top_y = 16, 80
                self.particles_picker_controller.set_anchor(left_x, top_y)
                self.particles_picker_controller.draw(screen)
        except Exception:
            pass

    # ToolbarView expects controller to expose selection state for highlight
    def is_active(self, tool: str) -> bool:
        try:
            return getattr(self.particles_toolbar_model, 'active_tool', None) == tool
        except Exception:
            return False
