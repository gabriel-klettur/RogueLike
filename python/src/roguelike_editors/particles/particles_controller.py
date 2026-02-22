import pygame
from .particles_model import ParticlesEditorModel
from .particles_view import ParticlesEditorView
from .particles_tool_bar_panel.particles_tool_bar_panel_model import ParticlesToolBarPanelModel
from .particles_tool_bar_panel.particles_tool_bar_panel_view import ParticlesToolBarPanelView
from .particles_tool_bar_panel.particles_tool_bar_panel_events import ParticlesToolBarPanelEventHandler
from .particles_tool_bar_panel.particles_tool_bar_panel_controller import ParticlesToolBarPanelController
from .particles_picker_panel.particles_picker_controller import ParticlesPickerController
from roguelike_editors.entities.services.constants import UI_MARGIN
from .particles_add_remove_panel.particles_add_remove_panel_model import (
    ParticlesAddRemovePanelModel,
)
from .particles_add_remove_panel.particles_add_remove_panel_view import (
    ParticlesAddRemovePanelView,
)
from .particles_add_remove_panel.particles_add_remove_panel_events import (
    ParticlesAddRemovePanelEventHandler,
)
from .particles_add_remove_panel.particles_add_remove_panel_controller import (
    ParticlesAddRemovePanelController,
)
from .particles_properties_panel.particles_properties_panel_controller import (
    ParticlesPropertiesPanelController,
)
from .particles_spells_list_panel.particles_spells_list_panel_controller import (
    ParticlesSpellsListPanelController,
)

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
        try:
            # Provide back-reference so picker events can access editor state
            self.particles_picker_controller.editor_controller = self
            # Ensure delete mode starts disabled
            self.particles_picker_controller.model.delete_mode_active = False
        except Exception:
            pass
        # Add/Remove MVC (panel lateral con acciones sobre partículas)
        self.particles_add_remove_model = ParticlesAddRemovePanelModel()
        self.particles_add_remove_view = ParticlesAddRemovePanelView(self, self.particles_add_remove_model)
        self.particles_add_remove_events = ParticlesAddRemovePanelEventHandler(self, self.particles_add_remove_model)
        self.particles_add_remove_controller = ParticlesAddRemovePanelController(
            self, self.particles_add_remove_model, self.particles_add_remove_view, self.particles_add_remove_events
        )
        # Ensure ToolbarView uses the panel controller for active-state checks
        try:
            if hasattr(self.particles_add_remove_view, 'widget'):
                self.particles_add_remove_view.widget.controller = self.particles_add_remove_controller
        except Exception:
            pass
        # Properties panel MVC (shows selected instance data)
        self.particles_properties_controller = ParticlesPropertiesPanelController(self.font)
        try:
            # Back-reference if needed later
            self.particles_properties_controller.editor_controller = self
        except Exception:
            pass
        # Spells-usage list panel MVC (shows spells referencing selected preset)
        self.particles_spells_list_controller = ParticlesSpellsListPanelController(self.font)
        try:
            self.particles_spells_list_controller.editor_controller = self
        except Exception:
            pass

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
        # Reenviar eventos al panel Add/Remove y picker cuando 'particles_list' esté activo
        try:
            if getattr(self.particles_toolbar_model, 'active_tool', None) == 'particles_list':
                # Primero, panel Add/Remove
                if self.particles_add_remove_controller.handle_event(event):
                    return
                # Luego, picker grid
                if self.particles_picker_controller.handle_event(event):
                    return
                # Finalmente, properties panel (no interactivo por ahora)
                if self.particles_properties_controller.handle_event(event):
                    return
                # Spells usage panel (toggle expand/collapse)
                try:
                    if self.particles_spells_list_controller.handle_event(event):
                        return
                except Exception:
                    pass
            else:
                # AÚN ASÍ: permitir interacciones con el mapa (selección/mover) aunque el panel esté oculto
                try:
                    if self.particles_add_remove_controller.handle_event(event):
                        return
                except Exception:
                    pass
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
        # Render panel Add/Remove y picker grid a la derecha cuando la herramienta esté activa
        try:
            if getattr(self.particles_toolbar_model, 'active_tool', None) == 'particles_list':
                # Asegurar visibilidad del panel Add/Remove
                self.particles_add_remove_model.visible = True
                # Renderizar panel Add/Remove a la derecha del toolbar
                self.particles_add_remove_controller.render(screen)

                # Calcular posición del picker a la derecha del panel Add/Remove
                left_x = None
                top_y = None
                ar_widget = getattr(self.particles_add_remove_view, 'widget', None)
                if ar_widget is not None:
                    try:
                        ar_pos = ar_widget.panel.pos or (ar_widget.x, ar_widget.y)
                        ar_w, _ = ar_widget.panel.surface.get_size()
                        left_x = int(ar_pos[0] + ar_w + UI_MARGIN)
                        top_y = int(ar_pos[1])
                    except Exception:
                        pass
                if left_x is None or top_y is None:
                    # Fallback: colocar a la derecha del toolbar
                    tb_widget = getattr(self.particles_toolbar_view, 'widget', None)
                    if tb_widget is not None:
                        try:
                            w, _ = tb_widget.panel.surface.get_size()
                            left_x = int((tb_widget.panel.pos or (tb_widget.x, tb_widget.y))[0] + w + UI_MARGIN)
                            top_y = int((tb_widget.panel.pos or (tb_widget.x, tb_widget.y))[1])
                        except Exception:
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
                # Properties: update from current selection and position the panel
                try:
                    sel_id = getattr(self.model, 'selected_instance_id', None)
                    self.particles_properties_controller.set_anchor_from_editor(self)
                    # Determine visibility: map selection or picker selection
                    picker_sel = None
                    try:
                        picker_sel = getattr(getattr(self.particles_picker_controller, 'model', None), 'selected_id', None)
                    except Exception:
                        picker_sel = None
                    vis = (sel_id is not None) or (isinstance(picker_sel, str))
                    self.particles_properties_controller.model.visible = bool(vis)
                    # Update map selection details only when present; otherwise clear
                    if sel_id is not None:
                        self.particles_properties_controller.show_for_id(sel_id)
                    else:
                        try:
                            m = self.particles_properties_controller.model
                            m.selected_id = None
                            m.entry = None
                        except Exception:
                            pass
                    self.particles_properties_controller.draw(screen)
                    # Spells-usage panel: visible only when a picker preset is selected
                    try:
                        self.particles_spells_list_controller.set_anchor_from_editor(self)
                        self.particles_spells_list_controller.model.visible = bool(isinstance(picker_sel, str))
                        if self.particles_spells_list_controller.model.visible:
                            self.particles_spells_list_controller.update_usages()
                            self.particles_spells_list_controller.render(screen)
                    except Exception:
                        pass
                except Exception:
                    pass
            else:
                # Ocultar panel Add/Remove si no está activa la lista
                self.particles_add_remove_model.visible = False
        except Exception:
            pass

    # ToolbarView expects controller to expose selection state for highlight
    def is_active(self, tool: str) -> bool:
        try:
            return getattr(self.particles_toolbar_model, 'active_tool', None) == tool
        except Exception:
            return False
