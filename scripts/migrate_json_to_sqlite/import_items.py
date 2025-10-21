from __future__ import annotations

"""Import items and item prices from JSON into SQLite.

- Sources:
  - data/items/items.json
  - data/items/items_price.json
- Idempotent via per-file SHA256 and `import_log` table
- Upserts using SQLite `ON CONFLICT DO UPDATE`
- If a price arrives for an unknown item id, create a stub item and set buy/sell = 0

Run:
    python -m scripts.migrate_json_to_sqlite.import_items
"""

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from typing import Any, Dict, Iterable, Iterator, Tuple

from sqlalchemy.dialects.sqlite import insert
from sqlalchemy import select

# Ensure src/ is importable
import sys
sys.path.append(str(Path(__file__).resolve().parents[1] / "src"))

from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import Item, ItemPrice, ImportLog


ITEMS_PATH = Path("data/items/items.json")
PRICES_PATH = Path("data/items/items_price.json")


@dataclass
class ImportOutcome:
    source_path: Path
    imported: bool
    row_count: int
    content_hash: str
    reason: str


def _content_hash(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def _json_load(p: Path) -> Any:
    return json.loads(p.read_text(encoding="utf-8"))


def _json_str(obj: Any) -> str:
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":"))


def _should_skip(s, source_path: str, h: str) -> bool:
    stmt = (
        select(ImportLog.content_hash)
        .where(ImportLog.source_path == source_path)
        .order_by(ImportLog.id.desc())
        .limit(1)
    )
    last = s.execute(stmt).scalar_one_or_none()
    return last == h


def _bool_or_none(v: Any) -> bool | None:
    return bool(v) if isinstance(v, bool) else None


def _int_or_none(v: Any) -> int | None:
    return int(v) if isinstance(v, (int, float)) else None


def _str_or_none(v: Any) -> str | None:
    return str(v) if isinstance(v, str) else None


def _extract_icons(payload: Dict[str, Any]) -> tuple[str | None, str | None, str | None]:
    """Return (icon_small, icon_large, icon_json) normalized from mixed inputs."""
    icon_small = _str_or_none(payload.get("icon_small"))
    icon_large = _str_or_none(payload.get("icon_large"))
    icon_json: str | None = None

    if "icon" in payload:
        icon_val = payload.get("icon")
        if isinstance(icon_val, str):
            # Prefer not to overwrite existing explicit small/large
            if not icon_small:
                icon_small = icon_val
        elif isinstance(icon_val, list):
            # Store full list in icon_json
            icon_json = _json_str(icon_val)

    return icon_small, icon_large, icon_json


def _insert_item_stmt(item_id: str, data: Dict[str, Any]):
    name = _str_or_none(data.get("name")) or item_id
    description = _str_or_none(data.get("description"))
    stackable = _bool_or_none(data.get("stackable"))
    max_stack = _int_or_none(data.get("max_stack"))
    z_layer = _int_or_none(data.get("z_layer"))
    despawn_time = _int_or_none(data.get("despawn_time"))
    equip_slot = _str_or_none(data.get("equip_slot"))
    rarity = _str_or_none(data.get("rarity"))
    level_requirement = _int_or_none(data.get("level_requirement"))

    icon_small, icon_large, icon_json = _extract_icons(data)

    stmt = insert(Item).values(
        id=item_id,
        name=name,
        description=description,
        stackable=stackable,
        max_stack=max_stack,
        z_layer=z_layer,
        despawn_time=despawn_time,
        equip_slot=equip_slot,
        rarity=rarity,
        level_requirement=level_requirement,
        icon_small=icon_small,
        icon_large=icon_large,
        icon_json=icon_json,
        extra_json=_json_str(data),
    )

    stmt = stmt.on_conflict_do_update(
        index_elements=[Item.id],
        set_={
            "name": stmt.excluded.name,
            "description": stmt.excluded.description,
            "stackable": stmt.excluded.stackable,
            "max_stack": stmt.excluded.max_stack,
            "z_layer": stmt.excluded.z_layer,
            "despawn_time": stmt.excluded.despawn_time,
            "equip_slot": stmt.excluded.equip_slot,
            "rarity": stmt.excluded.rarity,
            "level_requirement": stmt.excluded.level_requirement,
            "icon_small": stmt.excluded.icon_small,
            "icon_large": stmt.excluded.icon_large,
            "icon_json": stmt.excluded.icon_json,
            "extra_json": stmt.excluded.extra_json,
        },
    )
    return stmt


def _insert_item_stub_stmt(item_id: str):
    """Create a minimal item row only if absent (for FK integrity)."""
    stmt = insert(Item).values(
        id=item_id,
        name=item_id,
        description=None,
        stackable=None,
        max_stack=None,
        z_layer=None,
        despawn_time=None,
        equip_slot=None,
        rarity=None,
        level_requirement=None,
        icon_small=None,
        icon_large=None,
        icon_json=None,
        extra_json=None,
    )
    return stmt.on_conflict_do_nothing(index_elements=[Item.id])


def _upsert_price_stmt(item_id: str, buy: int, sell: int):
    stmt = insert(ItemPrice).values(
        id_item=item_id,
        buy_price=int(buy),
        sell_price=int(sell),
    )
    stmt = stmt.on_conflict_do_update(
        index_elements=[ItemPrice.id_item],
        set_={
            "buy_price": stmt.excluded.buy_price,
            "sell_price": stmt.excluded.sell_price,
        },
    )
    return stmt


def import_items_catalog() -> ImportOutcome:
    if not ITEMS_PATH.exists():
        return ImportOutcome(ITEMS_PATH, False, 0, "", "missing")

    h = _content_hash(ITEMS_PATH)
    doc = _json_load(ITEMS_PATH)

    if not isinstance(doc, dict):
        raise ValueError("items.json root must be an object mapping id -> item payload")

    row_count = 0
    with session_scope() as s:
        if _should_skip(s, str(ITEMS_PATH), h):
            return ImportOutcome(ITEMS_PATH, False, 0, h, "unchanged: hash matches")

        for item_id, payload in doc.items():
            if not isinstance(payload, dict):
                continue
            s.execute(_insert_item_stmt(item_id, payload))
            row_count += 1

        s.add(
            ImportLog(
                source_path=str(ITEMS_PATH),
                content_hash=h,
                imported_at=datetime.now(timezone.utc).isoformat(),
                row_count=row_count,
                version="items_v1",
            )
        )

    return ImportOutcome(ITEMS_PATH, True, row_count, h, "imported")


def import_item_prices() -> ImportOutcome:
    if not PRICES_PATH.exists():
        return ImportOutcome(PRICES_PATH, False, 0, "", "missing")

    h = _content_hash(PRICES_PATH)
    doc = _json_load(PRICES_PATH)

    if not isinstance(doc, dict):
        raise ValueError("items_price.json root must be an object mapping id -> {buy,sell}")

    row_count = 0
    with session_scope() as s:
        if _should_skip(s, str(PRICES_PATH), h):
            return ImportOutcome(PRICES_PATH, False, 0, h, "unchanged: hash matches")

        for item_id, payload in doc.items():
            if not isinstance(payload, dict):
                continue
            buy = _int_or_none(payload.get("buy")) or 0
            sell = _int_or_none(payload.get("sell")) or 0

            # Ensure item exists; if not, create stub and force price 0/0 as per requirement
            exists = s.get(Item, item_id) is not None
            if not exists:
                s.execute(_insert_item_stub_stmt(item_id))
                buy, sell = 0, 0

            s.execute(_upsert_price_stmt(item_id, buy, sell))
            row_count += 1

        s.add(
            ImportLog(
                source_path=str(PRICES_PATH),
                content_hash=h,
                imported_at=datetime.now(timezone.utc).isoformat(),
                row_count=row_count,
                version="item_prices_v1",
            )
        )

    return ImportOutcome(PRICES_PATH, True, row_count, h, "imported")


def run() -> None:
    out1 = import_items_catalog()
    status1 = "imported" if out1.imported else "skipped"
    print(f"[items] {ITEMS_PATH.name}: {status1} rows={out1.row_count} hash={out1.content_hash} reason={out1.reason}")

    out2 = import_item_prices()
    status2 = "imported" if out2.imported else "skipped"
    print(f"[item_prices] {PRICES_PATH.name}: {status2} rows={out2.row_count} hash={out2.content_hash} reason={out2.reason}")


if __name__ == "__main__":
    run()
