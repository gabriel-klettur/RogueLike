from .building_colliders_panel_model import BuildingCollidersPanelModel
from .building_colliders_panel_view import BuildingCollidersPanelView
from .building_colliders_panel_events import BuildingCollidersPanelEventHandler


class BuildingCollidersPanelController:
    def __init__(self, state, editor_state, editor_view):
        self.state = state
        self.editor_state = editor_state
        self.editor_view = editor_view
        self.model = BuildingCollidersPanelModel()
        self.view = BuildingCollidersPanelView(state, editor_state, self.model)
        self.events = BuildingCollidersPanelEventHandler(state, editor_state, self.model)

    def is_active(self) -> bool:
        return self.model.active

    def activate(self):
        self.model.active = True
        self.model.picker_open = True
        self.model.brush_dragging = False
        # Reanclar el picker en cada activación para alinear con el botón del toolbar
        self.model.picker_pos = None
        # Señalizar modo de colisiones en el editor para ocultar herramientas
        try:
            self.editor_state.colliders_mode = True
        except Exception:
            pass

    def deactivate(self):
        self.model.active = False
        self.model.picker_open = False
        self.model.reset_runtime()
        # Restablecer flag de modo colisiones
        try:
            self.editor_state.colliders_mode = False
        except Exception:
            pass

    def toggle(self):
        if self.is_active():
            self.deactivate()
        else:
            self.activate()

    def handle_event(self, event, camera, buildings) -> bool:
        return self.events.handle(event, camera, buildings)

    def render(self, screen, camera, buildings):
        self.view.render(screen, camera, buildings, editor_view=self.editor_view)
