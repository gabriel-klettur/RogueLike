import logging

logger = logging.getLogger("building_editor.events")


def undo_delete(editor, buildings) -> None:
    if hasattr(editor, "undo_stack") and editor.undo_stack:
        try:
            building, idx = editor.undo_stack.pop()
        except Exception:
            logger.info("⚠️ Undo: pila corrupta o elemento inválido")
            return
        try:
            buildings.insert(idx, building)
        except Exception:
            buildings.append(building)
        logger.info(f"✅ Undo: edificio restaurado en índice {idx}")
        editor.hovered_building = building
        try:
            t = getattr(editor, "tutorial", None)
            if not (t and t.is_active()):
                editor.selected_building = building
        except Exception:
            editor.selected_building = building
        try:
            setattr(editor, "tutorial_undo_delete_pulse", True)
        except Exception:
            pass
    else:
        logger.info("ℹ️ Undo: no hay operaciones de eliminación para deshacer")
