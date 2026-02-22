"""
Controlador del panel de Tutorial (FSM Editor).
"""
from __future__ import annotations
from typing import Optional, Any
from .fsm_tutorial_panel_model import FsmTutorialPanelModel
from .fsm_tutorial_panel_view import FsmTutorialPanelView
from .fsm_tutorial_panel_events import FsmTutorialPanelEventHandler


class FsmTutorialPanelController:
    def __init__(self, editor_controller) -> None:
        # Enlazado al controlador principal del FSM Editor
        self.editor: Any = editor_controller
        self.model = FsmTutorialPanelModel()
        self.view = FsmTutorialPanelView(self, self.model)
        self.events = FsmTutorialPanelEventHandler(self, self.model)
        # Cache de paso para limpiar al cambiar
        self._last_step_index: Optional[int] = None

    # --- Estado ---
    def is_active(self) -> bool:
        return bool(getattr(self.model, 'active', False))

    def activate(self) -> None:
        self.model.active = True
        self.model.reset_runtime()
        # Inicializar trackers con el estado actual para evitar falsos positivos
        gp = getattr(self.editor, 'graph_panel_controller', None)
        gm = getattr(gp, 'model', None)
        if gm is not None:
            try:
                self.model.last_zoom = float(getattr(gm, 'zoom', 1.0))
            except Exception:
                self.model.last_zoom = None
            try:
                self.model.last_pan = (float(getattr(gm, 'pan_x', 0.0)), float(getattr(gm, 'pan_y', 0.0)))
            except Exception:
                self.model.last_pan = None
            try:
                nodes = list(getattr(gm, 'nodes', []) or [])
                self.model.last_nodes_count = len(nodes)
            except Exception:
                self.model.last_nodes_count = None
            try:
                edges = list(getattr(gm, 'edges', []) or [])
                self.model.last_edges_count = len(edges)
            except Exception:
                self.model.last_edges_count = None
            try:
                self.model.last_initial_node_id = _detect_initial_id(gm)
            except Exception:
                self.model.last_initial_node_id = None
            try:
                self.model.legend_collapsed_prev = bool(getattr(gm, 'legend_collapsed', False))
            except Exception:
                self.model.legend_collapsed_prev = None
        self._last_step_index = getattr(self.model, 'step_index', 0)

    def deactivate(self) -> None:
        self.model.active = False
        self._last_step_index = None
        self.model.reset_runtime()

    def toggle(self) -> None:
        if self.is_active():
            self.deactivate()
        else:
            self.activate()

    # --- Integración ---
    def handle_event(self, event) -> bool:
        return self.events.handle(event)

    def render(self, screen) -> None:
        if not self.is_active():
            return
        # Sincronizar referencia de la toolbar principal para highlights
        try:
            tbv = getattr(getattr(self.editor, 'toolbar_controller', None), 'view', None)
            self.view.main_toolbar_view = getattr(tbv, 'toolbar', None)
        except Exception:
            self.view.main_toolbar_view = None
        # Limpiar al cambiar de paso externamente
        cur_idx = int(getattr(self.model, 'step_index', 0) or 0)
        if self._last_step_index is None or cur_idx != self._last_step_index:
            self.on_step_changed(cur_idx)
        # Actualizar progreso
        try:
            self._update_checklist_progress()
        except Exception:
            pass
        self.view.render(screen)

    def on_step_changed(self, new_idx: int) -> None:
        # Reiniciar progreso del nuevo paso para que el usuario lo vuelva a hacer
        try:
            self.model.checklist_done_by_step[new_idx] = set()
        except Exception:
            pass
        # Reset de banderas de edición
        self.model.editing_started = False
        self.model.last_editing_any = False
        # Recalcular bases
        gp = getattr(self.editor, 'graph_panel_controller', None)
        gm = getattr(gp, 'model', None)
        if gm is not None:
            try:
                self.model.last_zoom = float(getattr(gm, 'zoom', 1.0))
            except Exception:
                self.model.last_zoom = None
            try:
                self.model.last_pan = (float(getattr(gm, 'pan_x', 0.0)), float(getattr(gm, 'pan_y', 0.0)))
            except Exception:
                self.model.last_pan = None
            try:
                self.model.last_nodes_count = len(list(getattr(gm, 'nodes', []) or []))
            except Exception:
                self.model.last_nodes_count = None
            try:
                self.model.last_edges_count = len(list(getattr(gm, 'edges', []) or []))
            except Exception:
                self.model.last_edges_count = None
            try:
                self.model.last_initial_node_id = _detect_initial_id(gm)
            except Exception:
                self.model.last_initial_node_id = None
            try:
                self.model.legend_collapsed_prev = bool(getattr(gm, 'legend_collapsed', False))
            except Exception:
                self.model.legend_collapsed_prev = None
        self._last_step_index = new_idx

    # --- Checklist ---
    def _update_checklist_progress(self) -> None:
        idx = int(getattr(self.model, 'step_index', 0) or 0)
        steps = getattr(self.model, 'steps', [])
        if not steps or idx < 0 or idx >= len(steps):
            return
        checklist = steps[idx].get('checklist', []) or []
        if not checklist:
            return
        done_set = self.model.checklist_done_by_step.get(idx)
        if done_set is None:
            done_set = set()
            self.model.checklist_done_by_step[idx] = done_set

        # Estado actual
        sets_ctrl = getattr(self.editor, 'sets_panel_controller', None)
        sets_visible = bool(getattr(getattr(sets_ctrl, 'model', None), 'visible', False)) if sets_ctrl else False
        set_selected = getattr(getattr(sets_ctrl, 'model', None), 'selected_index', None) is not None if sets_ctrl else False

        gp = getattr(self.editor, 'graph_panel_controller', None)
        gm = getattr(gp, 'model', None)
        # Defaults
        zoom_changed = False
        pan_changed = False
        node_selected = False
        node_moved = False
        edit_started = False
        edit_committed = False
        nodes_inc = False
        nodes_dec = False
        edges_inc = False
        edges_dec = False
        initial_changed = False
        terminal_changed = False
        legend_toggled = False

        if gm is not None:
            # Zoom
            try:
                z = float(getattr(gm, 'zoom', 1.0))
                if self.model.last_zoom is not None and z != self.model.last_zoom:
                    zoom_changed = True
                self.model.last_zoom = z
            except Exception:
                pass
            # Pan
            try:
                p = (float(getattr(gm, 'pan_x', 0.0)), float(getattr(gm, 'pan_y', 0.0)))
                if self.model.last_pan is not None and p != self.model.last_pan:
                    pan_changed = True
                self.model.last_pan = p
            except Exception:
                pass
            # Selection and movement
            try:
                sel_id = getattr(gm, 'selected_node_id', None)
                if sel_id and sel_id != getattr(self.model, 'last_selected_node_id', None):
                    node_selected = True
                # movement only when same node and position changed
                pos = _node_pos_by_id(gm, sel_id) if sel_id else None
                if sel_id and self.model.last_selected_node_id == sel_id and pos and self.model.last_selected_node_pos and pos != self.model.last_selected_node_pos:
                    node_moved = True
                self.model.last_selected_node_id = sel_id
                self.model.last_selected_node_pos = pos
            except Exception:
                pass
            # Editing
            try:
                editing_any = (getattr(gm, 'editing_node_id', None) is not None) or (getattr(gm, 'editing_edge_index', None) is not None) or (getattr(gm, 'editing_edge_id', None) is not None)
                if editing_any and not self.model.last_editing_any:
                    edit_started = True
                    self.model.editing_started = True
                if self.model.editing_started and (not editing_any) and self.model.last_editing_any:
                    edit_committed = True
                    self.model.editing_started = False
                self.model.last_editing_any = editing_any
            except Exception:
                pass
            # Counts
            try:
                cur_nodes = len(list(getattr(gm, 'nodes', []) or []))
                if self.model.last_nodes_count is not None:
                    if cur_nodes > self.model.last_nodes_count:
                        nodes_inc = True
                    elif cur_nodes < self.model.last_nodes_count:
                        nodes_dec = True
                self.model.last_nodes_count = cur_nodes
            except Exception:
                pass
            try:
                cur_edges = len(list(getattr(gm, 'edges', []) or []))
                if self.model.last_edges_count is not None:
                    if cur_edges > self.model.last_edges_count:
                        edges_inc = True
                    elif cur_edges < self.model.last_edges_count:
                        edges_dec = True
                self.model.last_edges_count = cur_edges
            except Exception:
                pass
            # Initial/terminal
            try:
                ini_id = _detect_initial_id(gm)
                if self.model.last_initial_node_id is not None and ini_id != self.model.last_initial_node_id:
                    initial_changed = True
                self.model.last_initial_node_id = ini_id
            except Exception:
                pass
            try:
                has_terminal = _detect_terminal(gm)
                # consider a toggle as change
                if has_terminal is not None:
                    prev = getattr(self, '_prev_terminal', None)
                    if prev is not None and has_terminal != prev:
                        terminal_changed = True
                    self._prev_terminal = has_terminal
            except Exception:
                pass
            # Legend
            try:
                lc = bool(getattr(gm, 'legend_collapsed', False))
                if self.model.legend_collapsed_prev is not None and lc != self.model.legend_collapsed_prev:
                    legend_toggled = True
                self.model.legend_collapsed_prev = lc
            except Exception:
                pass

        # Evaluar checklist
        for item in checklist:
            iid = item.get('id')
            if not iid or iid in done_set:
                continue
            kind = (item.get('condition') or {}).get('kind')
            ok = False
            if kind == 'always':
                ok = True
            elif kind == 'sets_panel_visible':
                ok = sets_visible
            elif kind == 'set_selected':
                ok = set_selected
            elif kind == 'zoom_changed':
                ok = zoom_changed
            elif kind == 'pan_changed':
                ok = pan_changed
            elif kind == 'node_selected':
                ok = node_selected
            elif kind == 'node_moved':
                ok = node_moved
            elif kind == 'edit_started':
                ok = edit_started
            elif kind == 'edit_committed':
                ok = edit_committed
            elif kind == 'nodes_count_increased':
                ok = nodes_inc
            elif kind == 'nodes_count_decreased':
                ok = nodes_dec
            elif kind == 'edges_count_increased':
                ok = edges_inc
            elif kind == 'edges_count_decreased':
                ok = edges_dec
            elif kind == 'initial_changed':
                ok = initial_changed
            elif kind == 'terminal_changed':
                ok = terminal_changed
            elif kind == 'legend_toggled':
                ok = legend_toggled
            if ok:
                done_set.add(iid)


def _node_pos_by_id(gm, nid: Optional[str]):
    if not nid:
        return None
    try:
        idx = getattr(gm, 'node_index_by_id', {}).get(nid)
        if idx is None:
            # rebuild fallback
            try:
                gm.rebuild_caches()
                idx = getattr(gm, 'node_index_by_id', {}).get(nid)
            except Exception:
                idx = None
        if idx is None:
            # fallback scan
            for n in list(getattr(gm, 'nodes', []) or []):
                if n.get('id') == nid:
                    return (int(n.get('x', 0)), int(n.get('y', 0)))
            return None
        n = gm.nodes[idx]
        return (int(n.get('x', 0)), int(n.get('y', 0)))
    except Exception:
        return None


def _detect_initial_id(gm) -> Optional[str]:
    try:
        for n in list(getattr(gm, 'nodes', []) or []):
            if n.get('initial'):
                return n.get('id')
    except Exception:
        pass
    return None


def _detect_terminal(gm) -> Optional[bool]:
    # Consider true if any node has a truthy 'terminal' flag
    try:
        for n in list(getattr(gm, 'nodes', []) or []):
            if bool(n.get('terminal', False)):
                return True
    except Exception:
        pass
    return False
