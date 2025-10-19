from __future__ import annotations

def _get_editor(controller):
    try:
        return getattr(controller, 'editor_controller', None)
    except Exception:
        return None

def _clear_hold(editor) -> None:
    try:
        setattr(editor.model, 'hold_focus_active', False)
    except Exception:
        pass

def _compute_and_apply(editor) -> bool:
    try:
        from roguelike_editors.spawner.controller.ui_state import compute_ui_state, apply_ui_state
        state = compute_ui_state(editor)
        apply_ui_state(editor, state)
        return True
    except Exception:
        return False

def apply_ui_state_basic(controller) -> bool:
    editor = _get_editor(controller)
    if editor is None:
        return False
    _clear_hold(editor)
    return _compute_and_apply(editor)

def apply_ui_state_ensure_manager(controller) -> bool:
    editor = _get_editor(controller)
    if editor is None:
        return False
    _clear_hold(editor)
    try:
        editor.spawner_manager.set_visible(True)
    except Exception:
        pass
    return _compute_and_apply(editor)
