from __future__ import annotations

from typing import Any, Dict, List, Tuple

from roguelike_ui.services.formatting import format_key_label
from .utils import format_action_name


Spec = Dict[str, Any]


def _label_for(bindings: Dict[str, str], name: str) -> str:
    val = bindings.get(name, "")
    return format_key_label(val) if isinstance(val, str) and val else "—"


def _categorize(base: str) -> str:
    if base == "dash" or base.startswith("move_"):
        return "movements"
    if base in ("fireball", "laser_beam") or base.startswith("spell_"):
        return "spells"
    if base.startswith("toggle_") and base.endswith("_editor"):
        return "editors"
    if base in ("pause", "toggle_inventory", "select_class"):
        return "general"
    return "general"


def build_row_specs(bindings: Dict[str, str], *, category: str | None = None) -> Tuple[List[Spec], List[List[str]]]:
    """Build tri-slot row specs and renderer rows from input bindings.

    Returns (row_specs, rows):
    - row_specs: list of dicts with 'kind' ('tri'), 'display', and underlying keys.
    - rows: list of string rows for the renderer.
    """
    # Discover base actions from bindings keys
    base_actions: set[str] = set()
    for k in bindings.keys():
        if k.startswith("kb_"):
            body = k[len("kb_"):]
            if body.endswith("_a") or body.endswith("_b"):
                body = body[:-2]
            base_actions.add(body)
        elif k.startswith("mouse_"):
            base_actions.add(k[len("mouse_"):])
        else:
            base_actions.add(k)

    # Construct tri-slot specs
    all_specs: List[Spec] = []
    for base in sorted(base_actions):
        kb_a_key = f"kb_{base}_a"
        kb_b_key = f"kb_{base}_b"
        mouse_key = f"mouse_{base}"

        kb_a_label = _label_for(bindings, kb_a_key)
        if kb_a_label == "—":
            raw_base = bindings.get(base, "")
            if isinstance(raw_base, str) and raw_base:
                kb_a_label = format_key_label(raw_base)
        kb_b_label = _label_for(bindings, kb_b_key)
        mouse_label = _label_for(bindings, mouse_key)

        all_specs.append({
            "kind": "tri",
            "display": format_action_name(base),
            "kb_a_key": kb_a_key,
            "kb_b_key": kb_b_key,
            "mouse_key": mouse_key,
            "base_key": base,
            "labels": (kb_a_label, kb_b_label, mouse_label),
        })

    # Filter by category
    if category:
        all_specs = [s for s in all_specs if _categorize(s.get("base_key", "")) == category]

    # Sort and produce rows
    all_specs.sort(key=lambda s: s["display"])
    rows = [[s["display"], s["labels"][0], s["labels"][1], s["labels"][2]] for s in all_specs]
    return all_specs, rows
