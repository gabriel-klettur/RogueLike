"""
Controlador del panel de Tutorial (Map Editor).
"""
import traceback
from .map_tutorial_panel_model import MapTutorialPanelModel
from .map_tutorial_panel_view import MapTutorialPanelView
from .map_tutorial_panel_events import MapTutorialPanelEventHandler


class MapTutorialPanelController:
    def __init__(self, state, editor_state, editor_view, editor_manager):
        self.state = state
        self.editor_state = editor_state
        self.editor_view = editor_view
        self.editor_manager = editor_manager

        self.model = MapTutorialPanelModel()
        self.view = MapTutorialPanelView(state, editor_state, self.model, editor_view)
        self.events = MapTutorialPanelEventHandler(state, editor_state, self, self.model)

        # Inyección para alinear con la toolbar (se completa desde el Manager)
        self.view.toolbar_view = None
        self._last_step_index = None

    # Estado
    def is_active(self, _tool: str | None = None) -> bool:
        return bool(getattr(self.model, "active", False))

    def activate(self) -> None:
        self.model.active = True
        self.model.reset_runtime()
        if self.model.step_index < 0:
            self.model.step_index = 0
        self._last_step_index = self.model.step_index
        # Limpiar pulsos tutorial al iniciar
        self._clear_pulses()
        # Limpiar caches de la vista que dependen de hover (no de selección)
        self._clear_hover_highlight()

    def deactivate(self) -> None:
        self.model.active = False
        self.model.reset_runtime()
        self._last_step_index = None
        # Limpiar pulsos para próxima sesión
        self._clear_pulses()
        self._clear_hover_highlight()

    def toggle(self) -> None:
        if self.is_active():
            self.deactivate()
        else:
            self.activate()

    # Integración
    def handle_event(self, event) -> bool:
        return self.events.handle(event)

    def render(self, screen) -> None:
        if not self.is_active():
            return
        # Limpiar hover si el paso cambió externamente
        try:
            cur_idx = int(getattr(self.model, "step_index", 0) or 0)
        except Exception:
            cur_idx = 0
        if self._last_step_index is None or cur_idx != self._last_step_index:
            self._clear_hover_highlight()
            self._last_step_index = cur_idx
        # Actualizar progreso del checklist
        self._update_checklist_progress()
        self.view.render(screen)

    def on_step_changed(self, new_idx: int) -> None:
        # Reset por paso
        self.model.checklist_done_by_step[new_idx] = set()
        self.model.last_selected_zone = None
        self.model.last_camera_offset = None
        self.model.last_camera_zoom = None
        last_open = getattr(self.editor_state, "layers_view_open", False)
        self.model.last_layers_open = bool(last_open)
        self.model.last_zone_count = None
        # Limpiar pulsos explícitos
        self._clear_pulses()
        # Limpieza de hover/rects cacheados
        self._clear_hover_highlight()
        self._last_step_index = new_idx

    # --- Utilidades internas ---
    def _clear_hover_highlight(self) -> None:
        # Para Map Editor, mantener selección de zona; solo limpiar caches si existieran
        try:
            ev = getattr(self, "editor_view", None)
            if ev is not None:
                if hasattr(ev, "_last_selected_zone_rect"):
                    # No borrar la rect de la zona seleccionada para que el highlight persista
                    pass
        except Exception:
            pass

    def _clear_pulses(self) -> None:
        es = self.editor_state
        for attr in (
            "tutorial_camera_panned_pulse",
            "tutorial_camera_zoom_changed_pulse",
            "tutorial_layers_view_opened_pulse",
            "tutorial_paint_tiles_confirmed_pulse",
            "tutorial_paint_tiles_finalized_pulse",
            "tutorial_undo_performed_pulse",
            "tutorial_redo_performed_pulse",
            "tutorial_clear_colliders_finalized_pulse",
            "tutorial_paint_colliders_finalized_pulse",
            "tutorial_zone_added_pulse",
            "tutorial_zone_deleted_pulse",
            "tutorial_zone_renamed_pulse",
            "tutorial_zones_saved_pulse",
        ):
            try:
                setattr(es, attr, False)
            except Exception:
                pass

    # --- Checklist ---
    def _update_checklist_progress(self) -> None:
        idx = int(getattr(self.model, "step_index", 0) or 0)
        steps = getattr(self.model, "steps", [])
        if not steps or idx < 0 or idx >= len(steps):
            return
        step = steps[idx]
        checklist = step.get("checklist", []) or []
        if not checklist:
            return

        done_set = self.model.checklist_done_by_step.get(idx)
        if done_set is None:
            done_set = set()
            self.model.checklist_done_by_step[idx] = done_set

        es = self.editor_state
        ev = self.editor_view

        # Pulsos explícitos
        def consume(attr: str) -> bool:
            try:
                if bool(getattr(es, attr, False)):
                    setattr(es, attr, False)
                    return True
            except Exception:
                pass
            return False

        camera_panned = consume("tutorial_camera_panned_pulse")
        camera_zoom_changed = consume("tutorial_camera_zoom_changed_pulse")
        layers_opened_pulse = consume("tutorial_layers_view_opened_pulse")
        paint_tiles_confirmed = consume("tutorial_paint_tiles_confirmed_pulse")
        paint_tiles_finalized = consume("tutorial_paint_tiles_finalized_pulse")
        undo_performed = consume("tutorial_undo_performed_pulse")
        redo_performed = consume("tutorial_redo_performed_pulse")
        clear_colliders_finalized = consume("tutorial_clear_colliders_finalized_pulse")
        paint_colliders_finalized = consume("tutorial_paint_colliders_finalized_pulse")
        zone_added = consume("tutorial_zone_added_pulse")
        zone_deleted = consume("tutorial_zone_deleted_pulse")
        zone_renamed = consume("tutorial_zone_renamed_pulse")
        zones_saved = consume("tutorial_zones_saved_pulse")

        # Transiciones detectadas por diferencias
        selected_changed = False
        try:
            cur_sel = getattr(es, "selected_zone", None)
            if self.model.last_selected_zone is None:
                self.model.last_selected_zone = cur_sel
            else:
                if cur_sel != self.model.last_selected_zone:
                    selected_changed = True
                    self.model.last_selected_zone = cur_sel
        except Exception:
            pass

        layers_opened = False
        try:
            cur_open = bool(getattr(es, "layers_view_open", False))
            if self.model.last_layers_open is None:
                self.model.last_layers_open = cur_open
            else:
                if (not self.model.last_layers_open) and cur_open:
                    layers_opened = True
                self.model.last_layers_open = cur_open
        except Exception:
            pass

        # Evaluar condiciones
        for item in checklist:
            iid = item.get("id")
            if not iid or iid in done_set:
                continue
            cond = (item.get("condition") or {})
            kind = cond.get("kind")
            ok = False
            if kind == "always":
                ok = True
            elif kind == "zone_selected_changed":
                ok = selected_changed
            elif kind == "camera_panned":
                ok = camera_panned
            elif kind == "camera_zoom_changed":
                ok = camera_zoom_changed
            elif kind == "layers_view_opened":
                ok = layers_opened or layers_opened_pulse
            elif kind == "paint_tiles_confirmed":
                ok = paint_tiles_confirmed
            elif kind == "paint_tiles_finalized":
                ok = paint_tiles_finalized
            elif kind == "undo_performed":
                ok = undo_performed
            elif kind == "redo_performed":
                ok = redo_performed
            elif kind == "clear_colliders_finalized":
                ok = clear_colliders_finalized
            elif kind == "paint_colliders_finalized":
                ok = paint_colliders_finalized
            elif kind == "zone_added":
                ok = zone_added
            elif kind == "zone_deleted":
                ok = zone_deleted
            elif kind == "zone_renamed":
                ok = zone_renamed
            elif kind == "zones_saved":
                ok = zones_saved

            if ok:
                done_set.add(iid)
