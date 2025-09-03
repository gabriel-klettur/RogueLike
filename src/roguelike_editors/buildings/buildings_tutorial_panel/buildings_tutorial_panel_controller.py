"""
Controlador del panel de Tutorial (Buildings Editor).
"""
from .buildings_tutorial_panel_model import BuildingsTutorialPanelModel
from .buildings_tutorial_panel_view import BuildingsTutorialPanelView
from .buildings_tutorial_panel_events import BuildingsTutorialPanelEventHandler


class BuildingsTutorialPanelController:
    def __init__(self, state, editor_state, editor_view, editor_manager):
        self.state = state
        self.editor_state = editor_state
        self.editor_view = editor_view
        self.editor_manager = editor_manager

        self.model = BuildingsTutorialPanelModel()
        self.view = BuildingsTutorialPanelView(state, editor_state, self.model, editor_view)
        self.events = BuildingsTutorialPanelEventHandler(state, editor_state, self, self.model)

        # Inyección para alineación con la toolbar (lo completará el Manager)
        self.view.toolbar_view = None

    # Estado
    def is_active(self, _tool: str | None = None) -> bool:
        return bool(getattr(self.model, 'active', False))

    def activate(self) -> None:
        self.model.active = True
        self.model.reset_runtime()
        # Comenzar desde el primer paso si deseamos
        if self.model.step_index < 0:
            self.model.step_index = 0
        # Resetear progreso de checklist y métricas runtime
        try:
            self.model.checklist_done_by_step.clear()
            self.model.last_active_building_id = None
            self.model.last_active_pos = None
            self.model.last_split_ratio = None
            self.model.last_z_bottom = None
            self.model.last_z_top = None
        except Exception:
            pass

    def deactivate(self) -> None:
        self.model.active = False
        self.model.reset_runtime()
        # Limpiar progreso/metricas para próxima sesión
        try:
            self.model.checklist_done_by_step.clear()
            self.model.last_active_building_id = None
            self.model.last_active_pos = None
            self.model.last_split_ratio = None
            self.model.last_z_bottom = None
            self.model.last_z_top = None
        except Exception:
            pass
        # Asegurar que la toolbar no quede marcando el botón activo tras cerrar por ESC/Cerrar
        try:
            tb_model = getattr(self.editor_manager, 'buildings_toolbar_model', None)
            if tb_model and getattr(tb_model, 'active_tool', None) == 'tutorial_building':
                tb_model.active_tool = None
        except Exception:
            pass

    def toggle(self) -> None:
        if self.is_active():
            self.deactivate()
        else:
            self.activate()

    # Integración
    def handle_event(self, event) -> bool:
        return self.events.handle(event)

    def render(self, screen) -> None:
        if self.is_active():
            # Actualizar progreso del checklist antes de renderizar
            try:
                self._update_checklist_progress()
            except Exception:
                pass
            self.view.render(screen)

    # --- Checklist ---
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

        # Datos de entorno/editor
        es = self.editor_state
        ev = self.editor_view
        active = getattr(es, 'active_building', None) or getattr(es, 'selected_building', None)
        hovered = getattr(es, 'hovered_building', None)

        # Condiciones auxiliares
        def is_hover_or_active() -> bool:
            if hovered is not None:
                return True
            if active is not None:
                return True
            # Fallback a rects cacheados
            if getattr(ev, '_last_hovered_building_rect', None) is not None:
                return True
            if getattr(ev, '_last_active_building_rect', None) is not None:
                return True
            return False

        # Detectar cambios (comparar con métricas previas)
        moved = False
        split_changed = False
        z_bottom_changed = False
        z_top_changed = False

        if active is not None:
            bid = getattr(active, 'id', None)
            pos = (getattr(active, 'x', None), getattr(active, 'y', None))
            split_ratio = getattr(active, 'split_ratio', None)
            zb = getattr(active, 'z_bottom', None)
            zt = getattr(active, 'z_top', None)

            # Movimiento: requiere posición anterior existente y mismo edificio
            if self.model.last_active_building_id == bid and self.model.last_active_pos is not None and pos != self.model.last_active_pos:
                moved = True
            # Split/Z cambios
            if self.model.last_split_ratio is not None and split_ratio is not None and split_ratio != self.model.last_split_ratio:
                split_changed = True
            if self.model.last_z_bottom is not None and zb is not None and zb != self.model.last_z_bottom:
                z_bottom_changed = True
            if self.model.last_z_top is not None and zt is not None and zt != self.model.last_z_top:
                z_top_changed = True

            # Actualizar métricas para siguiente frame
            self.model.last_active_building_id = bid
            self.model.last_active_pos = pos
            self.model.last_split_ratio = split_ratio
            self.model.last_z_bottom = zb
            self.model.last_z_top = zt

        # Evaluar condiciones
        for item in checklist:
            iid = item.get('id')
            if not iid or iid in done_set:
                continue
            cond = (item.get('condition') or {})
            kind = cond.get('kind')
            ok = False
            if kind == 'always':
                ok = True
            elif kind == 'hover_or_active':
                ok = is_hover_or_active()
            elif kind == 'active_position_changed':
                ok = moved
            elif kind == 'split_changed':
                ok = split_changed
            elif kind == 'z_bottom_changed':
                ok = z_bottom_changed
            elif kind == 'z_top_changed':
                ok = z_top_changed
            elif kind == 'colliders_mode_on':
                ok = bool(getattr(es, 'colliders_mode', False))
            elif kind == 'picker_visible':
                ok = bool(getattr(es, 'picker_active', False))

            if ok:
                done_set.add(iid)
