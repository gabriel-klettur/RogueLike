import logging
from typing import Any

logger = logging.getLogger("buildings.tutorial")


class BuildingsTutorialChecklistService:
    """Service to evaluate and update checklist progress for the current step.

    Mirrors the original controller's _update_checklist_progress behavior without changes
    in semantics, but moved here for separation of concerns.
    """

    @staticmethod
    def update(model: Any, editor_state: Any, editor_view: Any) -> None:
        idx = int(getattr(model, 'step_index', 0) or 0)
        steps = getattr(model, 'steps', [])
        if not steps or idx < 0 or idx >= len(steps):
            return
        step = steps[idx]
        checklist = step.get('checklist', []) or []
        if not checklist:
            return

        done_set = model.checklist_done_by_step.get(idx)
        if done_set is None:
            done_set = set()
            model.checklist_done_by_step[idx] = done_set

        # Datos de entorno/editor
        es = editor_state
        ev = editor_view
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
        # Pulsos del picker (colocación)
        picker_placed_pulse = False
        # Pulsos del panel de colisiones
        colliders_choice_pulse = False
        colliders_painted_pulse = False
        colliders_painted_on_selected_pulse = False
        colliders_picker_moved_pulse = False
        colliders_saved_button_pulse = False

        # Guardar previos para detectar direcciones
        prev_bid = model.last_active_building_id
        prev_pos = model.last_active_pos
        prev_split = model.last_split_ratio
        prev_zb = model.last_z_bottom
        prev_zt = model.last_z_top
        prev_size = model.last_image_size
        prev_picker_dragging = model.last_picker_dragging
        prev_picker_dir = model.last_picker_dir
        prev_picker_hist_len = model.last_picker_history_len

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
            model.last_active_building_id = bid
            model.last_active_pos = pos
            model.last_split_ratio = split_ratio
            model.last_z_bottom = zb
            model.last_z_top = zt
            model.last_image_size = current_size

        # Consumir pulsos del editor_state para distinguir resize/reset, delete/undo y picker
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
        # Pulso del picker (colocar edificio con RMB)
        try:
            if bool(getattr(es, 'tutorial_picker_placed_pulse', False)):
                picker_placed_pulse = True
                setattr(es, 'tutorial_picker_placed_pulse', False)
        except Exception:
            pass

        # Estado de navegación del picker
        try:
            current_picker_dir = getattr(es, 'current_dir', None)
        except Exception:
            current_picker_dir = None
        try:
            current_picker_hist_len = len(getattr(es, 'history', []) or [])
        except Exception:
            current_picker_hist_len = None
        picker_nav_into = False
        picker_nav_back = False
        if prev_picker_dir is None:
            # Inicializar tracking en la primera pasada
            model.last_picker_dir = current_picker_dir
            model.last_picker_history_len = current_picker_hist_len
        else:
            if current_picker_dir is not None and current_picker_dir != prev_picker_dir:
                # Determinar dirección por tamaño del historial
                if (
                    current_picker_hist_len is not None and prev_picker_hist_len is not None
                    and current_picker_hist_len > prev_picker_hist_len
                ):
                    picker_nav_into = True
                elif (
                    current_picker_hist_len is not None and prev_picker_hist_len is not None
                    and current_picker_hist_len < prev_picker_hist_len
                ):
                    picker_nav_back = True
                # Actualizar tracking
                model.last_picker_dir = current_picker_dir
                model.last_picker_history_len = current_picker_hist_len

        # Estado actual de alcance de colisiones (CG/CU)
        try:
            current_scope = getattr(es, 'collider_scope', 'CG')
        except Exception:
            current_scope = 'CG'
        scope_toggled = False
        if model.last_collider_scope is None:
            # Inicializar sin marcar toggle
            model.last_collider_scope = current_scope
        else:
            if current_scope != model.last_collider_scope:
                scope_toggled = True
                model.last_collider_scope = current_scope

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
            elif kind == 'picker_navigate_into':
                ok = picker_nav_into
            elif kind == 'picker_navigate_back':
                ok = picker_nav_back
            elif kind == 'picker_drag_started':
                try:
                    current_dragging = bool(getattr(es, 'dragging_building', False))
                except Exception:
                    current_dragging = False
                # marcar solo el flanco de subida (inicio de drag)
                ok = (prev_picker_dragging is not True) and current_dragging is True
                # actualizar tracking para siguiente iteración
                model.last_picker_dragging = current_dragging
            elif kind == 'picker_building_placed':
                ok = picker_placed_pulse
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
