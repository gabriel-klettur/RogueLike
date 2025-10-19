from typing import Any


def clear_hover_highlight(editor_state: Any, editor_view: Any) -> None:
    """Limpia el estado de hover y caches relacionadas en la vista.
    No toca la selección activa persistente (active_building).
    """
    try:
        setattr(editor_state, 'hovered_buildings', [])
        setattr(editor_state, 'hovered_building', None)
        if hasattr(editor_state, 'hovered_building_index'):
            setattr(editor_state, 'hovered_building_index', 0)
    except Exception:
        pass
    try:
        if editor_view is not None:
            setattr(editor_view, '_last_hovered_building_rect', None)
    except Exception:
        pass


def clear_all_tutorial_pulses(editor_state: Any) -> None:
    """Set all tutorial pulse flags to False, safely."""
    pulse_attrs = [
        # resize/reset
        'tutorial_resized_pulse',
        'tutorial_reset_pulse',
        # delete/undo
        'tutorial_deleted_pulse',
        'tutorial_undo_delete_pulse',
        # colliders
        'tutorial_colliders_choice_pulse',
        'tutorial_colliders_painted_pulse',
        'tutorial_colliders_painted_on_selected_pulse',
        'tutorial_colliders_picker_moved_pulse',
        'tutorial_colliders_saved_button_pulse',
        # picker
        'tutorial_picker_placed_pulse',
    ]
    for name in pulse_attrs:
        try:
            setattr(editor_state, name, False)
        except Exception:
            pass


def reset_runtime_metrics(model: Any) -> None:
    """Reset runtime tracking metrics on the model for the new session/step."""
    model.last_active_building_id = None
    model.last_active_pos = None
    model.last_split_ratio = None
    model.last_z_bottom = None
    model.last_z_top = None
    model.last_image_size = None
    model.last_collider_scope = None
    model.last_picker_dragging = None
    model.last_picker_dir = None
    model.last_picker_history_len = None


def init_picker_tracking_from_state(model: Any, editor_state: Any) -> None:
    """Initialize picker navigation tracking using the current editor_state."""
    try:
        model.last_picker_dir = getattr(editor_state, 'current_dir', None)
    except Exception:
        model.last_picker_dir = None
    try:
        hist = getattr(editor_state, 'history', None)
        model.last_picker_history_len = (len(hist) if hist is not None else None)
    except Exception:
        model.last_picker_history_len = None
