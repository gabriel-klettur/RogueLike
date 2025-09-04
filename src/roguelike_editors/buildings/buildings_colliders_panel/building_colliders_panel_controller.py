from .building_colliders_panel_model import BuildingCollidersPanelModel
from .building_colliders_panel_view import BuildingCollidersPanelView
from .building_colliders_panel_events import BuildingCollidersPanelEventHandler
import logging


class BuildingCollidersPanelController:
    def __init__(self, state, editor_state, editor_view):
        self.state = state
        self.editor_state = editor_state
        self.editor_view = editor_view
        self.model = BuildingCollidersPanelModel()
        self.view = BuildingCollidersPanelView(state, editor_state, self.model)
        self.events = BuildingCollidersPanelEventHandler(state, editor_state, self.model)
        # Optional: set by BuildingEditorManager for cross-panel coordination (tutorial keep-alive)
        self.editor_manager = None
        self._logger = logging.getLogger("buildings.colliders")

    def is_active(self) -> bool:
        return self.model.active

    def activate(self):
        # Debug: entering colliders mode
        try:
            self._logger.info(
                "[Colliders] activate() called; colliders_mode(before)=%s, tutorial_active(before)=%s",
                getattr(self.editor_state, 'colliders_mode', None),
                bool(getattr(getattr(self, 'editor_manager', None), 'tutorial', None) and self.editor_manager.tutorial.is_active())
            )
        except Exception:
            pass
        # Record tutorial state before switching on colliders (keep-alive only if it was active)
        try:
            tut = getattr(getattr(self, 'editor_manager', None), 'tutorial', None)
            self.model._tutorial_keep_alive = bool(tut and tut.is_active())
        except Exception:
            self.model._tutorial_keep_alive = False
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
        # Keep tutorial visible if it was active before enabling colliders
        try:
            tut = getattr(getattr(self, 'editor_manager', None), 'tutorial', None)
            if getattr(self.model, '_tutorial_keep_alive', False) and tut and not tut.is_active():
                # Preserve tutorial progress/state when reactivating due to keep-alive
                try:
                    prev_idx = getattr(getattr(tut, 'model', None), 'step_index', 0)
                    prev_done = dict(getattr(getattr(tut, 'model', None), 'checklist_done_by_step', {}) or {})
                except Exception:
                    prev_idx = 0
                    prev_done = {}
                tut.activate()
                # Restore step index and checklist completion so the user doesn't lose progress
                try:
                    if hasattr(tut, 'model'):
                        tut.model.step_index = prev_idx
                        tut.model.checklist_done_by_step = prev_done
                    # Sync controller's last step tracker to avoid forced clears
                    try:
                        setattr(tut, '_last_step_index', prev_idx)
                    except Exception:
                        pass
                except Exception:
                    pass
                try:
                    self._logger.info("[Colliders] Keep-alive: reactivated Tutorial after enabling Colliders")
                except Exception:
                    pass
        except Exception:
            pass

    def deactivate(self):
        try:
            self._logger.info(
                "[Colliders] deactivate() called; colliders_mode(before)=%s",
                getattr(self.editor_state, 'colliders_mode', None)
            )
        except Exception:
            pass
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
