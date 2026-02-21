from __future__ import annotations
from typing import Dict, List, Optional, Tuple

# Categorías canónicas para pestañas/archivos
CATEGORY_KEYS = [
    "inventory",   # todo (no se persiste individualmente)
    "equipment",
    "materials",
    "consumables",
]


def pick_category_for_item(model: object) -> str:
    """Retorna la categoría canónica para un modelo de ítem.

    - equipment: tiene equip_slot o durability
    - consumables: tiene effect
    - materials: stackeable sin effect, no equipable y sin quest_id
    - por defecto: inventory
    """
    if model is None:
        return "inventory"
    try:
        if getattr(model, "equip_slot", None) is not None or getattr(model, "durability", None) is not None:
            return "equipment"
        if getattr(model, "effect", None) is not None:
            return "consumables"
        if getattr(model, "quest_id", None) is None and bool(getattr(model, "stackable", False)) is True:
            return "materials"
    except Exception:
        pass
    return "inventory"


def split_slots_by_category(
    slots: List[object],
    items: Dict[str, object],
) -> Dict[str, List[Tuple[int, str, int]]]:
    """Divide los slots en categorías.

    Retorna dict category_key -> lista de tuplas (slot_index, item_id, quantity).
    """
    out: Dict[str, List[Tuple[int, str, int]]] = {
        "equipment": [],
        "materials": [],
        "consumables": [],
    }
    for idx, st in enumerate(slots):
        if not st:
            continue
        item_id = getattr(st, "item_id", None)
        qty = int(getattr(st, "quantity", 0) or 0)
        if not item_id or qty <= 0:
            continue
        model = items.get(item_id)
        cat = pick_category_for_item(model)
        if cat in out:
            out[cat].append((idx, item_id, qty))
    return out
