"""
Controlador del panel de Tutorial (Spells Editor).
"""
import pygame
from .spells_tutorial_panel_model import SpellsTutorialPanelModel
from .spells_tutorial_panel_view import SpellsTutorialPanelView
from .spells_tutorial_panel_events import SpellsTutorialPanelEventHandler


class SpellsTutorialPanelController:
    def __init__(self, editor_controller):
        """
        Recibe el controlador principal del Spells Editor (inner controller),
        que expone:
          - model (SpellEditorModel)
          - view (SpellEditorView)
          - spells_toolbar_view (SpellsToolBarPanelView)
          - spells_add_remove_view (SpellsAddRemovePanelView)
          - spells_properties_controller (SpellsPropertiesPanelController)
        """
        self.editor = editor_controller
        self.model = SpellsTutorialPanelModel()
        self.view = SpellsTutorialPanelView(self.model, editor_controller)
        self.events = SpellsTutorialPanelEventHandler(self, self.model)
        # Alinear respecto del toolbar
        try:
            self.view.toolbar_view = getattr(editor_controller, 'spells_toolbar_view', None)
        except Exception:
            pass
        # Tracking
        self._last_step_index: int | None = None

    # Estado
    def is_active(self) -> bool:
        return bool(getattr(self.model, 'active', False))

    def activate(self) -> None:
        self.model.active = True
        self.model.reset_runtime()
        if self.model.step_index < 0:
            self.model.step_index = 0
        self._last_step_index = self.model.step_index
        # Inicializar métricas
        try:
            self.model.last_selected_id = getattr(self.editor.model, 'selected_id', None)
            self.model.last_spells_count = len(getattr(self.editor.model, 'spells', {}) or {})
        except Exception:
            self.model.last_selected_id = None
            self.model.last_spells_count = None

    def deactivate(self) -> None:
        self.model.active = False
        self.model.reset_runtime()
        self._last_step_index = None
        # Des-seleccionar botón de toolbar si estaba marcado
        try:
            tb_model = getattr(self.editor, 'spells_toolbar_model', None)
            if tb_model and getattr(tb_model, 'active_tool', None) == 'tutorial_spells':
                tb_model.active_tool = None
        except Exception:
            pass

    def toggle(self) -> None:
        if self.is_active():
            self.deactivate()
        else:
            self.activate()

    # Integración
    def handle_event(self, event: pygame.event.Event) -> bool:
        return self.events.handle(event)

    def render(self, screen: pygame.Surface) -> None:
        if not self.is_active():
            return
        # Si el paso cambió externamente, limpiar tracking
        try:
            cur_idx = int(getattr(self.model, 'step_index', 0) or 0)
        except Exception:
            cur_idx = 0
        if self._last_step_index is None or cur_idx != self._last_step_index:
            self._last_step_index = cur_idx
        # Actualizar progreso
        self._update_checklist_progress()
        self.view.render(screen)

    def on_step_changed(self, new_idx: int) -> None:
        # Reset por paso
        try:
            self.model.last_selected_id = getattr(self.editor.model, 'selected_id', None)
            self.model.last_spells_count = len(getattr(self.editor.model, 'spells', {}) or {})
        except Exception:
            self.model.last_selected_id = None
            self.model.last_spells_count = None
        self._last_step_index = new_idx

    # Checklist conditions
    def _update_checklist_progress(self) -> None:
        idx = int(getattr(self.model, 'step_index', 0) or 0)
        steps = getattr(self.model, 'steps', [])
        if not steps or idx < 0 or idx >= len(steps):
            return
        step = steps[idx]
        checklist = step.get('checklist', []) or []
        if not checklist:
            return
        done_set = getattr(self.model, 'checklist_done_by_step', {}).get(idx)
        if done_set is None:
            if not hasattr(self.model, 'checklist_done_by_step'):
                self.model.checklist_done_by_step = {}
            done_set = set()
            self.model.checklist_done_by_step[idx] = done_set

        # Estado actual
        em = self.editor.model
        # Picker visible
        picker_visible = bool(getattr(em, 'picker_visible', False))
        # Selección
        current_sel = getattr(em, 'selected_id', None)
        selected_changed = False
        if getattr(self.model, 'last_selected_id', None) != current_sel and current_sel is not None:
            # Marca cambio cuando hay una selección válida
            selected_changed = True
        # Conteo de spells
        try:
            cur_count = len(getattr(em, 'spells', {}) or {})
        except Exception:
            cur_count = None
        increased = decreased = False
        prev_count = getattr(self.model, 'last_spells_count', None)
        if isinstance(cur_count, int) and isinstance(prev_count, int):
            increased = cur_count > prev_count
            decreased = cur_count < prev_count
        # Propiedades visibles (cuando hay picker visible y el panel de propiedades dibujó su rect)
        props_visible = False
        try:
            props_model = getattr(self.editor.spells_properties_controller, 'model', None)
            props_rect = getattr(props_model, 'panel_rect', None) if props_model else None
            props_visible = picker_visible and (props_rect is not None)
        except Exception:
            props_visible = False

        # Evaluar
        for item in checklist:
            iid = item.get('id')
            if not iid or iid in done_set:
                continue
            kind = (item.get('condition') or {}).get('kind')
            ok = False
            if kind == 'always':
                ok = True
            elif kind == 'picker_visible':
                ok = picker_visible
            elif kind == 'selected_changed':
                ok = selected_changed
            elif kind == 'spell_count_increased':
                ok = increased
            elif kind == 'spell_count_decreased':
                ok = decreased
            elif kind == 'properties_visible':
                ok = props_visible
            if ok:
                done_set.add(iid)

        # Actualizar métricas
        self.model.last_selected_id = current_sel
        if isinstance(cur_count, int):
            self.model.last_spells_count = cur_count
