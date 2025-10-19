"""
Controlador del panel de Tutorial (Tiles Editor).
"""
import traceback
from .tiles_tutorial_panel_model import TilesTutorialPanelModel
from .tiles_tutorial_panel_view import TilesTutorialPanelView
from .tiles_tutorial_panel_events import TilesTutorialPanelEventHandler


class TilesTutorialPanelController:
    def __init__(self, editor_controller):
        self.editor_controller = editor_controller  # TileEditorController
        self.editor_state = editor_controller.editor
        self.model = TilesTutorialPanelModel()
        self.view = TilesTutorialPanelView(self, self.model)
        self.events = TilesTutorialPanelEventHandler(self, self.model)
        # Inyección para highlights
        try:
            self.view.toolbar_view = self.editor_controller.toolbar.view
        except Exception:
            # Tests may stub toolbar without a 'view'; degrade gracefully
            self.view.toolbar_view = None
        # Tracking de paso
        self._last_step_index = None

    # Estado
    def is_active(self) -> bool:
        return bool(getattr(self.model, 'active', False))

    def activate(self) -> None:
        self.model.active = True
        self.model.reset_runtime()
        # Al activar, limpiar posición para que el panel aparezca centrado
        try:
            self.model.pos = None
            self.model.dragging = False
            self.model.drag_offset = (0, 0)
        except Exception:
            pass
        if self.model.step_index < 0:
            self.model.step_index = 0
        self._last_step_index = self.model.step_index
        # Limpiar pulsos tutorial al activar
        self._clear_pulses()

    def deactivate(self) -> None:
        self.model.active = False
        self.model.reset_runtime()
        self._last_step_index = None
        self._clear_pulses()

    def toggle(self) -> None:
        if self.is_active():
            self.deactivate()
        else:
            self.activate()

    def handle_event(self, event) -> bool:
        return self.events.handle(event)

    def render(self, screen) -> None:
        if not self.is_active():
            return
        # Si el paso cambió externamente, sincronizar
        try:
            cur_idx = int(getattr(self.model, 'step_index', 0) or 0)
        except Exception:
            cur_idx = 0
        if self._last_step_index is None or cur_idx != self._last_step_index:
            self._last_step_index = cur_idx
        # Mantener referencia fresca de toolbar para el highlight
        try:
            self.view.toolbar_view = self.editor_controller.toolbar.view
        except Exception:
            pass
        # Actualizar progreso
        self._update_checklist_progress()
        self.view.render(screen)

    def on_step_changed(self, new_idx: int) -> None:
        # Limpiar progreso del paso destino
        self.model.checklist_done_by_step[new_idx] = set()
        self._last_step_index = new_idx

    # Checklist/progreso
    def _consume(self, attr: str) -> bool:
        try:
            if bool(getattr(self.editor_state, attr, False)):
                setattr(self.editor_state, attr, False)
                return True
        except Exception:
            pass
        return False

    def _update_checklist_progress(self) -> None:
        idx = int(getattr(self.model, 'step_index', 0) or 0)
        steps = getattr(self.model, 'steps', [])
        if not steps or idx < 0 or idx >= len(steps):
            return
        step = steps[idx]
        checklist = step.get('checklist', []) or []
        if not checklist:
            return
        done_set = self.model.checklist_done_by_step.get(idx)
        if done_set is None:
            done_set = set()
            self.model.checklist_done_by_step[idx] = done_set

        es = self.editor_state
        for item in checklist:
            iid = item.get('id')
            if not iid or iid in done_set:
                continue
            kind = (item.get('condition') or {}).get('kind')
            ok = False
            if kind == 'always':
                ok = True
            elif kind == 'tiles_editor_active':
                ok = bool(getattr(es, 'active', False))
            elif kind == 'size_panel_visible':
                ok = bool(getattr(es, 'size_panel_state', None) and getattr(es.size_panel_state, 'visible', False))
            elif kind == 'picker_open':
                ok = bool(getattr(es, 'picker_state', None) and getattr(es.picker_state, 'open', False))
            elif kind == 'choice_selected':
                ok = self._consume('tutorial_choice_selected_pulse') or bool(getattr(es, 'current_choice', None))
            elif kind == 'brush_painted':
                ok = self._consume('tutorial_brush_painted_pulse')
            elif kind == 'eyedropper_used':
                ok = self._consume('tutorial_eyedropper_pulse')
            elif kind == 'delete_done':
                ok = self._consume('tutorial_delete_pulse')
            elif kind == 'default_done':
                ok = self._consume('tutorial_default_pulse')
            elif kind == 'layers_open':
                ok = self._consume('tutorial_layers_open_pulse') or bool(getattr(es, 'toolbar_state', None) and getattr(es.toolbar_state, 'layers_view_open', False))
            elif kind == 'layer_changed':
                ok = self._consume('tutorial_layer_changed_pulse')
            elif kind == 'collisions_mode':
                ok = self._consume('tutorial_collisions_mode_pulse') or bool(getattr(es, 'toolbar_state', None) and (getattr(es.toolbar_state, 'show_collisions', False) or getattr(es.toolbar_state, 'show_collisions_overlay', False)))
            elif kind == 'collision_painted':
                ok = self._consume('tutorial_collision_painted_pulse')
            if ok:
                done_set.add(iid)

    def _clear_pulses(self) -> None:
        names = [
            'tutorial_choice_selected_pulse',
            'tutorial_brush_painted_pulse',
            'tutorial_eyedropper_pulse',
            'tutorial_delete_pulse',
            'tutorial_default_pulse',
            'tutorial_layers_open_pulse',
            'tutorial_layer_changed_pulse',
            'tutorial_collisions_mode_pulse',
            'tutorial_collision_painted_pulse',
        ]
        for n in names:
            try:
                setattr(self.editor_state, n, False)
            except Exception:
                pass
