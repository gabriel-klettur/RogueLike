"""
Controlador del panel de Tutorial (Buildings Editor).
"""
import logging
import traceback
from .buildings_tutorial_panel_model import BuildingsTutorialPanelModel
from .buildings_tutorial_panel_view import BuildingsTutorialPanelView
from .buildings_tutorial_panel_events import BuildingsTutorialPanelEventHandler

logger = logging.getLogger("buildings.tutorial")

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
        # Tracking del índice de paso para detectar cambios externos y limpiar hover
        self._last_step_index = None

    # Estado
    def is_active(self, _tool: str | None = None) -> bool:
        return bool(getattr(self.model, 'active', False))

    def activate(self) -> None:
        # Log activation intent with context
        try:
            logger.info(
                "[Tutorial] activate() called; colliders_mode=%s, picker_active=%s, step_index=%s\n%s",
                getattr(self.editor_state, 'colliders_mode', None),
                getattr(self.editor_state, 'picker_active', None),
                getattr(self.model, 'step_index', None),
                self._short_stack()
            )
        except Exception:
            pass
        self.model.active = True
        self.model.reset_runtime()
        # Comenzar desde el primer paso si deseamos
        if self.model.step_index < 0:
            self.model.step_index = 0
        # Sincronizar tracking de paso actual
        self._last_step_index = self.model.step_index
        # Resetear progreso de checklist y métricas runtime
        try:
            self.model.checklist_done_by_step.clear()
            self.model.last_active_building_id = None
            self.model.last_active_pos = None
            self.model.last_split_ratio = None
            self.model.last_z_bottom = None
            self.model.last_z_top = None
            self.model.last_image_size = None
            self.model.last_collider_scope = None
            # Limpiar pulsos tutorial (resize/reset)
            try:
                setattr(self.editor_state, 'tutorial_resized_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_reset_pulse', False)
            except Exception:
                pass
            # Limpiar pulsos tutorial (delete/undo)
            try:
                setattr(self.editor_state, 'tutorial_deleted_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_undo_delete_pulse', False)
            except Exception:
                pass
            # Limpiar pulsos tutorial (colliders)
            try:
                setattr(self.editor_state, 'tutorial_colliders_choice_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_colliders_painted_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_colliders_painted_on_selected_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_colliders_picker_moved_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_colliders_saved_button_pulse', False)
            except Exception:
                pass
            # (limpieza) duplicados de reseteo de colliders eliminados
        except Exception:
            pass
        # Limpiar cualquier highlight por hover al iniciar el tutorial
        try:
            self._clear_hover_highlight()
        except Exception:
            pass

    def deactivate(self) -> None:
        # Log deactivation intent with context
        try:
            logger.info(
                "[Tutorial] deactivate() called; colliders_mode=%s, picker_active=%s, step_index=%s\n%s",
                getattr(self.editor_state, 'colliders_mode', None),
                getattr(self.editor_state, 'picker_active', None),
                getattr(self.model, 'step_index', None),
                self._short_stack()
            )
        except Exception:
            pass
        self.model.active = False
        self.model.reset_runtime()
        # Reset tracking
        self._last_step_index = None
        # Limpiar progreso/metricas para próxima sesión
        try:
            self.model.checklist_done_by_step.clear()
            self.model.last_active_building_id = None
            self.model.last_active_pos = None
            self.model.last_split_ratio = None
            self.model.last_z_bottom = None
            self.model.last_z_top = None
            self.model.last_image_size = None
            # Limpiar pulsos tutorial (resize/reset)
            try:
                setattr(self.editor_state, 'tutorial_resized_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_reset_pulse', False)
            except Exception:
                pass
            # Limpiar pulsos tutorial (delete/undo)
            try:
                setattr(self.editor_state, 'tutorial_deleted_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_undo_delete_pulse', False)
            except Exception:
                pass
            # Limpiar pulsos tutorial (colliders)
            try:
                setattr(self.editor_state, 'tutorial_colliders_choice_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_colliders_painted_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_colliders_painted_on_selected_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_colliders_picker_moved_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_colliders_saved_button_pulse', False)
            except Exception:
                pass
        except Exception:
            pass
        # Limpiar cualquier highlight por hover al cerrar el tutorial
        try:
            self._clear_hover_highlight()
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

    def _short_stack(self, depth: int = 6) -> str:
        """Return a short formatted call stack (excluding this helper)."""
        try:
            # Exclude the current frame (this function) from the stack
            frames = traceback.extract_stack(limit=depth + 2)[:-2]
            lines = []
            for fr in frames:
                # Keep file tail for brevity
                file_tail = fr.filename.replace('\\', '/').split('/')[-1]
                lines.append(f"  at {file_tail}:{fr.lineno} in {fr.name}")
            return "Call stack:\n" + "\n".join(lines)
        except Exception:
            return "Call stack: <unavailable>"

    # Integración
    def handle_event(self, event) -> bool:
        return self.events.handle(event)

    def render(self, screen) -> None:
        if self.is_active():
            # Si el paso cambió por fuera de on_step_changed, limpiar hover y sincronizar
            try:
                cur_idx = int(getattr(self.model, 'step_index', 0) or 0)
            except Exception:
                cur_idx = 0
            if self._last_step_index is None or cur_idx != self._last_step_index:
                try:
                    self._clear_hover_highlight()
                except Exception:
                    pass
                self._last_step_index = cur_idx
            # Actualizar progreso del checklist antes de renderizar
            try:
                self._update_checklist_progress()
            except Exception:
                pass
            self.view.render(screen)

    # Llamado al cambiar de paso (Prev/Next)
    def on_step_changed(self, new_idx: int) -> None:
        try:
            # Limpiar progreso del paso destino para que el usuario lo rehaga
            self.model.checklist_done_by_step[new_idx] = set()
            # Resetear métricas runtime para evitar arrastre de estado entre pasos
            self.model.last_active_building_id = None
            self.model.last_active_pos = None
            self.model.last_split_ratio = None
            self.model.last_z_bottom = None
            self.model.last_z_top = None
            self.model.last_image_size = None
            self.model.last_collider_scope = None
            # Limpiar pulsos tutorial (resize/reset)
            try:
                setattr(self.editor_state, 'tutorial_resized_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_reset_pulse', False)
            except Exception:
                pass
            # Limpiar pulsos tutorial (delete/undo)
            try:
                setattr(self.editor_state, 'tutorial_deleted_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_undo_delete_pulse', False)
            except Exception:
                pass
            # Limpiar pulsos tutorial (colliders)
            try:
                setattr(self.editor_state, 'tutorial_colliders_choice_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_colliders_painted_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_colliders_painted_on_selected_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_colliders_picker_moved_pulse', False)
            except Exception:
                pass
            try:
                setattr(self.editor_state, 'tutorial_colliders_saved_button_pulse', False)
            except Exception:
                pass
        except Exception:
            pass
        # Siempre limpiar resaltado por hover entre pasos para profesionalizar la UX
        try:
            self._clear_hover_highlight()
        except Exception:
            pass
        # Sincronizar tracking con el nuevo paso
        self._last_step_index = new_idx

    # --- Utilidades internas ---
    def _clear_hover_highlight(self) -> None:
        """Limpia el estado de hover y cualquier rect cacheado de hover en la vista.
        No toca la selección activa persistente (active_building)."""
        # Estado del editor
        try:
            setattr(self.editor_state, 'hovered_buildings', [])
            setattr(self.editor_state, 'hovered_building', None)
            if hasattr(self.editor_state, 'hovered_building_index'):
                setattr(self.editor_state, 'hovered_building_index', 0)
        except Exception:
            pass
        # Caches de la vista
        try:
            ev = getattr(self, 'editor_view', None)
            if ev is not None:
                setattr(ev, '_last_hovered_building_rect', None)
                # Mantener el rect del activo para no interferir con selección persistente
        except Exception:
            pass

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
        selected_changed = False
        size_changed = False
        # Pulsos explícitos de acciones
        reset_done_pulse = False
        resized_pulse = False
        deleted_pulse = False
        undo_delete_pulse = False
        # Pulsos del panel de colisiones
        colliders_choice_pulse = False
        colliders_painted_pulse = False
        colliders_painted_on_selected_pulse = False
        colliders_picker_moved_pulse = False
        colliders_saved_button_pulse = False

        # Guardar previos para detectar direcciones
        prev_bid = self.model.last_active_building_id
        prev_pos = self.model.last_active_pos
        prev_split = self.model.last_split_ratio
        prev_zb = self.model.last_z_bottom
        prev_zt = self.model.last_z_top
        prev_size = self.model.last_image_size

        if active is not None:
            bid = getattr(active, 'id', None)
            pos = (getattr(active, 'x', None), getattr(active, 'y', None))
            split_ratio = getattr(active, 'split_ratio', None)
            zb = getattr(active, 'z_bottom', None)
            zt = getattr(active, 'z_top', None)
            try:
                current_size = tuple(getattr(active, 'image', None).get_size()) if getattr(active, 'image', None) is not None else None
            except Exception:
                current_size = None

            # Movimiento: requiere posición anterior existente y mismo edificio
            if prev_bid == bid and prev_pos is not None and pos != prev_pos:
                moved = True
            # Cambio de selección activa (clic izquierdo típico)
            if bid is not None and prev_bid is not None and bid != prev_bid:
                selected_changed = True
            elif bid is not None and prev_bid is None:
                # Primera selección también cuenta
                selected_changed = True
            # Split/Z cambios
            if prev_split is not None and split_ratio is not None and split_ratio != prev_split:
                split_changed = True
            if prev_bid == bid and prev_zb is not None and zb is not None and zb != prev_zb:
                z_bottom_changed = True
            if prev_bid == bid and prev_zt is not None and zt is not None and zt != prev_zt:
                z_top_changed = True
            # Cambio de tamaño por Resize o Reset (solo mismo edificio)
            if (
                prev_bid == bid
                and prev_size is not None
                and current_size is not None
                and current_size != prev_size
            ):
                size_changed = True

            # Actualizar métricas para siguiente frame
            self.model.last_active_building_id = bid
            self.model.last_active_pos = pos
            self.model.last_split_ratio = split_ratio
            self.model.last_z_bottom = zb
            self.model.last_z_top = zt
            self.model.last_image_size = current_size

        # Consumir pulsos del editor_state para distinguir resize/reset y delete/undo
        try:
            if bool(getattr(es, 'tutorial_reset_pulse', False)):
                reset_done_pulse = True
                setattr(es, 'tutorial_reset_pulse', False)
        except Exception:
            pass
        try:
            if bool(getattr(es, 'tutorial_resized_pulse', False)):
                resized_pulse = True
                setattr(es, 'tutorial_resized_pulse', False)
        except Exception:
            pass
        try:
            if bool(getattr(es, 'tutorial_deleted_pulse', False)):
                deleted_pulse = True
                setattr(es, 'tutorial_deleted_pulse', False)
        except Exception:
            pass
        try:
            if bool(getattr(es, 'tutorial_undo_delete_pulse', False)):
                undo_delete_pulse = True
                setattr(es, 'tutorial_undo_delete_pulse', False)
        except Exception:
            pass
        # Pulsos del panel de colisiones
        try:
            if bool(getattr(es, 'tutorial_colliders_choice_pulse', False)):
                colliders_choice_pulse = True
                setattr(es, 'tutorial_colliders_choice_pulse', False)
        except Exception:
            pass
        try:
            if bool(getattr(es, 'tutorial_colliders_painted_pulse', False)):
                colliders_painted_pulse = True
                setattr(es, 'tutorial_colliders_painted_pulse', False)
        except Exception:
            pass
        try:
            if bool(getattr(es, 'tutorial_colliders_painted_on_selected_pulse', False)):
                colliders_painted_on_selected_pulse = True
                setattr(es, 'tutorial_colliders_painted_on_selected_pulse', False)
        except Exception:
            pass
        try:
            if bool(getattr(es, 'tutorial_colliders_picker_moved_pulse', False)):
                colliders_picker_moved_pulse = True
                setattr(es, 'tutorial_colliders_picker_moved_pulse', False)
        except Exception:
            pass
        try:
            if bool(getattr(es, 'tutorial_colliders_saved_button_pulse', False)):
                colliders_saved_button_pulse = True
                setattr(es, 'tutorial_colliders_saved_button_pulse', False)
        except Exception:
            pass

        # Estado actual de alcance de colisiones (CG/CU)
        try:
            current_scope = getattr(es, 'collider_scope', 'CG')
        except Exception:
            current_scope = 'CG'
        scope_toggled = False
        if self.model.last_collider_scope is None:
            # Inicializar sin marcar toggle
            self.model.last_collider_scope = current_scope
        else:
            if current_scope != self.model.last_collider_scope:
                scope_toggled = True
                self.model.last_collider_scope = current_scope

        # Evaluar condiciones
        try:
            is_resizing = bool(getattr(es, 'resizing', False))
        except Exception:
            is_resizing = False
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
            elif kind == 'z_bottom_plus':
                ok = z_bottom_changed and (active is not None) and (prev_zb is not None) and (getattr(active, 'z_bottom', None) is not None) and (getattr(active, 'z_bottom', None) > prev_zb)
            elif kind == 'z_bottom_minus':
                ok = z_bottom_changed and (active is not None) and (prev_zb is not None) and (getattr(active, 'z_bottom', None) is not None) and (getattr(active, 'z_bottom', None) < prev_zb)
            elif kind == 'z_top_changed':
                ok = z_top_changed
            elif kind == 'z_top_plus':
                ok = z_top_changed and (active is not None) and (prev_zt is not None) and (getattr(active, 'z_top', None) is not None) and (getattr(active, 'z_top', None) > prev_zt)
            elif kind == 'z_top_minus':
                ok = z_top_changed and (active is not None) and (prev_zt is not None) and (getattr(active, 'z_top', None) is not None) and (getattr(active, 'z_top', None) < prev_zt)
            elif kind == 'active_selected_changed':
                ok = selected_changed
            elif kind == 'size_changed':
                ok = size_changed
            elif kind == 'resized':
                # Preferir pulso explícito; fallback: solo si estamos en modo resizing y no fue un reset
                ok = resized_pulse or (is_resizing and size_changed and not reset_done_pulse)
            elif kind == 'reset_done':
                ok = reset_done_pulse
            elif kind == 'colliders_mode_on':
                ok = bool(getattr(es, 'colliders_mode', False))
            elif kind == 'picker_visible':
                ok = bool(getattr(es, 'picker_active', False))
            elif kind == 'deleted_building':
                ok = deleted_pulse
            elif kind == 'undo_delete':
                ok = undo_delete_pulse
            # -- Colliders panel conditions --
            elif kind == 'colliders_choice_selected':
                ok = colliders_choice_pulse
            elif kind == 'colliders_painted':
                ok = colliders_painted_pulse
            elif kind == 'colliders_painted_on_selected':
                ok = colliders_painted_on_selected_pulse
            elif kind == 'colliders_picker_moved':
                ok = colliders_picker_moved_pulse
            elif kind == 'colliders_saved_button':
                ok = colliders_saved_button_pulse
            elif kind == 'colliders_scope_toggled':
                ok = scope_toggled
            elif kind == 'colliders_scope_cg':
                # Requiere acción explícita del usuario: solo cuenta si se cambió el alcance en esta sesión
                ok = scope_toggled and (current_scope == 'CG')
            elif kind == 'colliders_scope_cu':
                # Requiere acción explícita del usuario: solo cuenta si se cambió el alcance en esta sesión
                ok = scope_toggled and (current_scope == 'CU')

            if ok:
                done_set.add(iid)
