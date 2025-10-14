from __future__ import annotations

import logging
from typing import Any, Optional

from .typing_utils import convert_text_to_type, convert_like
from .persistence import (
    get_items_json_path,
    save_field,
    save_entry,
    rename_item_id,
    save_asset_field,
    validate_and_normalize_entry,
)
from .schema import ensure_schema
from .assets import (
    pick_icon_key_from_data,
    pick_icon_key_from_schema,
    normalize_asset_path,
    apply_asset_to_draft,
    apply_asset_to_item,
)
from .item_data import get_item_data

logger = logging.getLogger(__name__)


def commit_edit(controller: Any) -> None:
    """Commit the current inline edit for the active item or draft."""
    if not controller.model.editing_property:
        return
    item_id: Optional[str] = controller._selected_id or controller._hovered_id
    key: str = controller.model.editing_property
    new_text: str = controller.model.editing_text
    if item_id and item_id in controller._items:
        item = controller._items[item_id]
        old_val = getattr(item, key, None)
        converted = convert_like(old_val, new_text)
        if key == 'id':
            new_id = str(converted)
            path = get_items_json_path()
            if not new_id:
                logger.warning("[ItemsPropertiesPanel] Empty id not allowed; ignoring change")
            elif new_id == item_id:
                try:
                    setattr(item, 'id', new_id)
                except Exception:
                    pass
                try:
                    save_field(path, item_id, 'id', new_id)
                except Exception:
                    logger.exception("[ItemsPropertiesPanel] Failed to persist same-id assignment")
                try:
                    if callable(controller.on_after_commit_edit):
                        controller.on_after_commit_edit('id', item_id, item_id, new_id)
                except Exception:
                    logger.exception("[ItemsPropertiesPanel] on_after_commit_edit callback failed")
            else:
                ok, msg = rename_item_id(path, item_id, new_id)
                if ok:
                    try:
                        setattr(item, 'id', new_id)
                    except Exception:
                        pass
                    try:
                        controller._items[new_id] = controller._items.pop(item_id)
                    except Exception:
                        pass
                    if controller._selected_id == item_id:
                        controller._selected_id = new_id
                    if controller._hovered_id == item_id:
                        controller._hovered_id = new_id
                    try:
                        if callable(controller.on_after_commit_edit):
                            controller.on_after_commit_edit('id', item_id, new_id, new_id)
                    except Exception:
                        logger.exception("[ItemsPropertiesPanel] on_after_commit_edit callback failed")
                else:
                    logger.error(f"[ItemsPropertiesPanel] Cannot rename id to '{new_id}': {msg}")
        else:
            try:
                setattr(item, key, converted)
            except Exception as e:
                logger.error(
                    f"[ItemsPropertiesPanel] Invalid assignment for {key}: '{converted}', error: {e}"
                )
                controller.text_input.deactivate()
                controller.model.editing_property = None
                controller.model.editing_text = ""
                controller.model.editing_cursor = 0
                return
            path = get_items_json_path()
            try:
                save_field(path, item_id, key, converted)
            except Exception:
                logger.exception("[ItemsPropertiesPanel] Failed to persist field update")
            try:
                if callable(controller.on_after_commit_edit):
                    controller.on_after_commit_edit(key, item_id, None, converted)
            except Exception:
                logger.exception("[ItemsPropertiesPanel] on_after_commit_edit callback failed")
    else:
        t = getattr(controller.model, 'schema_types', {}).get(key)
        converted = convert_text_to_type(new_text, t)
        controller.model.new_item_draft[key] = converted
    controller.text_input.deactivate()
    controller.model.editing_property = None
    controller.model.editing_text = ""
    controller.model.editing_cursor = 0


def confirm_add_item_on_system(controller: Any) -> None:
    """Persist selected item or draft and exit add-on-system mode."""
    path = get_items_json_path()
    item_id: Optional[str] = controller._selected_id or controller._hovered_id
    entry: Optional[dict] = None
    if item_id:
        item = controller._items.get(item_id)
        if item is None:
            return
        if hasattr(item, 'model_dump'):
            entry = item.model_dump()
        else:
            try:
                entry = item.dict()
            except Exception:
                entry = vars(item)
    else:
        draft = dict(controller.model.new_item_draft)
        new_id = str(draft.get('id', '')).strip()
        if not new_id:
            logger.error("[ItemsPropertiesPanel] Cannot confirm: missing 'id' in new item draft")
            return
        entry = draft
        entry['id'] = new_id
        item_id = new_id
    try:
        ok, normalized = validate_and_normalize_entry(entry)
        if not ok or normalized is None:
            logger.error("[ItemsPropertiesPanel] Validation/normalization failed before save")
            return
    except Exception:
        logger.exception("[ItemsPropertiesPanel] Exception during validation/normalization")
        return
    try:
        save_entry(path, item_id, normalized)
    except Exception:
        logger.exception("[ItemsPropertiesPanel] Failed to save item entry on confirm")
    # UI cleanup and editor refresh
    try:
        controller.model.show_add_system_selector = False
        try:
            controller.model.new_item_draft.clear()
        except Exception:
            pass
        controller.model.editing_property = None
        controller.model.editing_text = ""
        controller.model.editing_cursor = 0
        controller._selected_id = None
        controller._hovered_id = None
        if controller.editor_controller is not None:
            try:
                arm = getattr(controller.editor_controller, 'items_add_remove_model', None)
                if arm and getattr(arm, 'active_tool', None) == 'add_item_on_system':
                    arm.active_tool = None
            except Exception:
                pass
            try:
                if hasattr(controller.editor_controller, 'exit_add_items_on_system_mode'):
                    controller.editor_controller.exit_add_items_on_system_mode()
            except Exception:
                pass
            try:
                controller.editor_controller.picker_controller.model.visible = True
            except Exception:
                pass
            try:
                controller.editor_controller._refresh_items_catalog()
            except Exception:
                logger.exception("[ItemsPropertiesPanel] Failed to refresh items catalog after confirm")
            try:
                setattr(controller.editor_controller.model, 'tutorial_add_system_confirm_pulse', True)
            except Exception:
                pass
    except Exception:
        pass


def on_asset_chosen(controller: Any, cell_key: str, path: str) -> None:
    """Handle asset chosen callback and persist changes."""
    try:
        item_id = controller._selected_id or controller._hovered_id
        asset_value = normalize_asset_path(path)
        if not item_id or item_id not in controller._items:
            target_key = pick_icon_key_from_schema(controller.model)
            apply_asset_to_draft(controller.model, target_key, asset_value)
            return
        item = controller._items[item_id]
        data = get_item_data(item)
        target_key = pick_icon_key_from_data(data)
        apply_asset_to_item(item, target_key, asset_value)
        path_json = get_items_json_path()
        save_asset_field(path_json, item_id, target_key, asset_value)
        try:
            if callable(controller.on_asset_changed):
                controller.on_asset_changed(item_id, asset_value)
        except Exception:
            logger.exception("[ItemsPropertiesPanel] on_asset_changed callback failed")
        try:
            if callable(controller.on_after_commit_edit):
                controller.on_after_commit_edit(target_key, item_id, None, asset_value)
        except Exception:
            logger.exception("[ItemsPropertiesPanel] on_after_commit_edit callback failed tras cambio de asset")
        try:
            editor = getattr(controller, 'editor_controller', None)
            if editor is not None:
                setattr(editor.model, 'tutorial_asset_changed_pulse', True)
        except Exception:
            pass
    finally:
        try:
            controller.assets_picker.hide()
        except Exception:
            pass
