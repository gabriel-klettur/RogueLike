"""
Controlador del panel de Tutorial (Entities Editor).
"""
from .entities_tutorial_panel_model import EntitiesTutorialPanelModel
from .entities_tutorial_panel_view import EntitiesTutorialPanelView
from .entities_tutorial_panel_events import EntitiesTutorialPanelEventHandler
from roguelike_editors.entities.services.constants import ENTITIES_TOOL_ON_MAP


class EntitiesTutorialPanelController:
    def __init__(self, editor_controller):
        self.editor = editor_controller  # EntitiesEditorController
        self.model = EntitiesTutorialPanelModel()
        self.view = EntitiesTutorialPanelView(self, self.model)
        self.events = EntitiesTutorialPanelEventHandler(self, self.model)
        # Para alinear highlights con la toolbar
        self.view.toolbar_view = self.editor.toolbar_view
        # Tracking de paso
        self._last_step_index = None

    # Estado
    def is_active(self) -> bool:
        return bool(getattr(self.model, 'active', False))

    def activate(self) -> None:
        self.model.active = True
        self.model.reset_runtime()
        if self.model.step_index < 0:
            self.model.step_index = 0
        self._last_step_index = self.model.step_index

    def deactivate(self) -> None:
        self.model.active = False
        self.model.reset_runtime()
        self._last_step_index = None
        # Asegurar que el botón no quede marcado (la toolbar no resalta tool de tutorial)
        # No se requiere cambiar active_tool de entities.

    # Integración
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
        # Actualizar progreso del checklist
        self._update_checklist_progress()
        # Mantener referencia fresca de toolbar para el highlight
        self.view.toolbar_view = self.editor.toolbar_view
        self.view.render(screen)

    def on_step_changed(self, new_idx: int) -> None:
        # Limpiar progreso del paso destino
        self.model.checklist_done_by_step[new_idx] = set()
        # Sincronizar tracking
        self._last_step_index = new_idx

    # Checklist
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

        em = self.editor.model  # EntitiesEditorModel
        # Pulsos consumibles
        def consume(attr: str) -> bool:
            try:
                if bool(getattr(em, attr, False)):
                    setattr(em, attr, False)
                    return True
            except Exception:
                pass
            return False

        for item in checklist:
            iid = item.get('id')
            if not iid or iid in done_set:
                continue
            kind = (item.get('condition') or {}).get('kind')
            ok = False
            if kind == 'always':
                ok = True
            elif kind == 'entities_tool_on':
                ok = (getattr(em.toolbar_model, 'active_tool', None) == ENTITIES_TOOL_ON_MAP)
            elif kind == 'picker_visible':
                ok = bool(getattr(self.editor.picker_controller.model, 'visible', False))
            elif kind == 'spawn_mode_on':
                ok = bool(getattr(em, 'spawn_mode_active', False))
            elif kind == 'spawn_selection':
                # Consumir pulso si existe; fallback si ya hay tipo seleccionado
                ok = consume('tutorial_spawn_selection_pulse') or (getattr(em, 'spawn_entity_type', None) is not None)
            elif kind == 'entity_spawned':
                ok = consume('tutorial_entity_spawned_pulse')
            elif kind == 'delete_mode_on':
                ok = bool(getattr(em, 'delete_mode_active', False))
            elif kind == 'entity_deleted':
                ok = consume('tutorial_entity_deleted_pulse')
            elif kind == 'undo_done':
                ok = consume('tutorial_undo_pulse')
            elif kind == 'redo_done':
                ok = consume('tutorial_redo_pulse')
            elif kind == 'add_system_mode':
                ok = consume('tutorial_add_system_mode_pulse') or bool(getattr(self.editor.properties_controller.model, 'show_add_system_selector', False))
            if ok:
                done_set.add(iid)
