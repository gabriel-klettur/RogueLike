import pygame
from .particles_model import ParticlesEditorModel
from .particles_view import ParticlesEditorView
from .particles_tool_bar_panel.particles_tool_bar_panel_model import ParticlesToolBarPanelModel
from .particles_tool_bar_panel.particles_tool_bar_panel_view import ParticlesToolBarPanelView
from .particles_tool_bar_panel.particles_tool_bar_panel_events import ParticlesToolBarPanelEventHandler

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

    def toggle_visible(self):
        self.model.visible = not bool(self.model.visible)

    def handle_event(self, event: pygame.event.Event) -> None:
        # Solo manejar eventos del toolbar cuando el editor está visible
        if not getattr(self.model, 'visible', False):
            return
        # Toolbar drag and basic event routing
        try:
            if hasattr(self.particles_toolbar_view, 'handle_event') and self.particles_toolbar_view.handle_event(event):
                return
        except Exception:
            pass
        try:
            if hasattr(self.particles_toolbar_events, 'handle_event') and self.particles_toolbar_events.handle_event(event):
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
            self.particles_toolbar_view.render(screen)
        except Exception:
            pass

    # ToolbarView expects controller to expose selection state for highlight
    def is_active(self, tool: str) -> bool:
        try:
            return getattr(self.particles_toolbar_model, 'active_tool', None) == tool
        except Exception:
            return False
