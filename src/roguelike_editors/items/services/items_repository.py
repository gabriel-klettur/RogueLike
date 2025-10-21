from __future__ import annotations

"""ItemsRepository: CRUD helpers for Items editor backed by SQLite.

Bridges editor UI actions to DB models (`Item`, `ItemPrice`).
Keeps a JSON-like dict interface to minimize changes in panel code.
"""

from typing import Any, Dict, Tuple
import json

from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import Item as ItemRow, ItemPrice as ItemPriceRow


# --------------------------- Helpers ---------------------------

def _safe_int(x: Any) -> int | None:
    try:
        return int(x) if x is not None else None
    except Exception:
        return None


def _safe_float(x: Any) -> float | None:
    try:
        return float(x) if x is not None else None
    except Exception:
        return None


def _json_dumps(obj: Any) -> str:
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":"))


def _json_loads(text: str | None) -> Dict[str, Any]:
    if not text:
        return {}
    try:
        v = json.loads(text)
        return v if isinstance(v, dict) else {}
    except Exception:
        return {}


def _merge_row_payload(row: ItemRow) -> Dict[str, Any]:
    """Return a JSON-like item entry merging DB columns into extra_json."""
    payload = _json_loads(getattr(row, "extra_json", None))
    # Overlay stable columns (prefer explicit DB fields when present)
    def _put(key: str, value: Any) -> None:
        if value is not None:
            payload[key] = value
    _put("id", row.id)
    _put("name", getattr(row, "name", None))
    _put("description", getattr(row, "description", None))
    _put("stackable", getattr(row, "stackable", None))
    _put("max_stack", getattr(row, "max_stack", None))
    _put("z_layer", getattr(row, "z_layer", None))
    _put("despawn_time", getattr(row, "despawn_time", None))
    _put("equip_slot", getattr(row, "equip_slot", None))
    _put("rarity", getattr(row, "rarity", None))
    _put("level_requirement", getattr(row, "level_requirement", None))
    _put("icon_small", getattr(row, "icon_small", None))
    _put("icon_large", getattr(row, "icon_large", None))
    # icon list stored in icon_json
    try:
        icon_json = getattr(row, "icon_json", None)
        if icon_json:
            payload["icon"] = json.loads(icon_json)
    except Exception:
        pass
    return payload


# --------------------------- Queries ---------------------------

def all_items_dict() -> Dict[str, Dict[str, Any]]:
    """Return items dict id->entry (merged) for editors."""
    out: Dict[str, Dict[str, Any]] = {}
    with session_scope() as s:
        rows = s.query(ItemRow).all()
        for r in rows:
            out[r.id] = _merge_row_payload(r)
    return out


# --------------------------- Mutations -------------------------

def upsert_entry(entry: Dict[str, Any]) -> None:
    """Insert or update an item entry from a JSON-like dict.

    Stable columns are mapped; the full entry is preserved in extra_json.
    Icons handling:
      - icon (str|list) -> icon_json if list; if str and icon_small missing -> set icon_small.
    """
    if not isinstance(entry, dict):
        return
    item_id = str(entry.get("id") or "").strip()
    if not item_id:
        return
    # Prepare columns
    name = entry.get("name")
    description = entry.get("description")
    stackable = bool(entry.get("stackable")) if entry.get("stackable") is not None else None
    max_stack = _safe_int(entry.get("max_stack"))
    z_layer = _safe_int(entry.get("z_layer"))
    despawn_time = _safe_int(entry.get("despawn_time"))
    equip_slot = entry.get("equip_slot")
    rarity = entry.get("rarity")
    level_requirement = _safe_int(entry.get("level_requirement"))

    icon_small = entry.get("icon_small")
    icon_large = entry.get("icon_large")
    icon = entry.get("icon")
    icon_json = None
    if isinstance(icon, list):
        icon_json = _json_dumps(icon)
    elif isinstance(icon, str) and not icon_small:
        icon_small = icon

    extra_json = _json_dumps(entry)

    with session_scope() as s:
        row = s.get(ItemRow, item_id)
        if row is None:
            row = ItemRow(id=item_id)
            s.add(row)
        # Assign mapped fields
        row.name = name
        row.description = description
        row.stackable = stackable
        row.max_stack = max_stack
        row.z_layer = z_layer
        row.despawn_time = despawn_time
        row.equip_slot = equip_slot
        row.rarity = rarity
        row.level_requirement = level_requirement
        row.icon_small = icon_small
        row.icon_large = icon_large
        row.icon_json = icon_json
        row.extra_json = extra_json
        # Committed by session_scope


def update_field(item_id: str, key: str, value: Any) -> None:
    data = all_items_dict()
    entry = data.get(item_id, {"id": item_id})
    # Preserve list behavior for assets like in legacy editor
    if key == "icon":
        if isinstance(entry.get("icon"), list):
            lst = entry.get("icon") or []
            if lst:
                lst[0] = value
            else:
                entry["icon"] = [value]
        else:
            entry["icon"] = value
    else:
        entry[key] = value
    upsert_entry(entry)


def save_asset_field(item_id: str, key: str, value: Any) -> None:
    update_field(item_id, key, value)


def rename_item_id(old_id: str, new_id: str) -> Tuple[bool, str]:
    new_id = (new_id or "").strip()
    if not new_id:
        return False, "empty_new_id"
    with session_scope() as s:
        old = s.get(ItemRow, old_id)
        if old is None:
            return False, "old_id_missing"
        if s.get(ItemRow, new_id) is not None:
            return False, "id_collision"
        # Create new row copying fields
        new_row = ItemRow(
            id=new_id,
            name=old.name,
            description=old.description,
            stackable=old.stackable,
            max_stack=old.max_stack,
            z_layer=old.z_layer,
            despawn_time=old.despawn_time,
            equip_slot=old.equip_slot,
            rarity=old.rarity,
            level_requirement=old.level_requirement,
            icon_small=old.icon_small,
            icon_large=old.icon_large,
            icon_json=old.icon_json,
            extra_json=old.extra_json,
        )
        s.add(new_row)
        # Move price if exists
        price = s.get(ItemPriceRow, old_id)
        if price is not None:
            new_price = ItemPriceRow(id_item=new_id, buy_price=price.buy_price, sell_price=price.sell_price)
            s.add(new_price)
            s.delete(price)
        # Remove old row
        s.delete(old)
    return True, "ok"


def delete_item(item_id: str) -> bool:
    try:
        with session_scope() as s:
            p = s.get(ItemPriceRow, item_id)
            if p is not None:
                s.delete(p)
            r = s.get(ItemRow, item_id)
            if r is None:
                return False
            s.delete(r)
        return True
    except Exception:
        return False
