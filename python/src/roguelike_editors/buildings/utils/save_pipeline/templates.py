"""Template catalog utilities for the split save pipeline."""
from __future__ import annotations

import json
from typing import Dict, Iterable, Tuple

from roguelike_editors.buildings.utils.asset_paths import normalize_asset_path
from .builders import build_template_entry

__all__ = ["TemplateCatalog"]

Signature = str
TemplateSignatureCache = Dict[Signature, int]


def _signature_from_entry(entry: dict) -> Signature:
    assets = entry.get("assets") if isinstance(entry, dict) else None
    img = None
    if isinstance(assets, dict):
        img = normalize_asset_path(assets.get("idle"))
    solid = bool(entry.get("solid", True))
    split_ratio = round(float(entry.get("split_ratio", 0.5)), 3)
    collider_scope = entry.get("collider_scope", "CG")
    original_scale = entry.get("original_scale") if isinstance(entry.get("original_scale"), (list, tuple)) else None
    signature_payload = {
        "img": img,
        "solid": solid,
        "split_ratio": split_ratio,
        "collider_scope": collider_scope,
        "original_scale": list(original_scale) if original_scale else None,
    }
    return json.dumps(signature_payload, sort_keys=True, ensure_ascii=False)


def _signature_from_building(building: object) -> Signature:
    img = normalize_asset_path(getattr(building, "image_path", None))
    solid = bool(getattr(building, "solid", True))
    split_ratio = round(float(getattr(building, "split_ratio", 0.5)), 3)
    collider_scope = getattr(building, "collider_scope", "CG")
    original_scale = getattr(building, "original_scale", None)
    signature_payload = {
        "img": img,
        "solid": solid,
        "split_ratio": split_ratio,
        "collider_scope": collider_scope,
        "original_scale": list(original_scale) if isinstance(original_scale, (list, tuple)) else None,
    }
    return json.dumps(signature_payload, sort_keys=True, ensure_ascii=False)


class TemplateCatalog:
    """Maintains a mapping between template signatures and IDs."""

    def __init__(self, existing_templates: Iterable[dict]) -> None:
        self._signature_to_id: TemplateSignatureCache = {}
        self._entries: Dict[int, dict] = {}
        self._max_id = 0
        for entry in existing_templates:
            try:
                tid_raw = entry.get("id") if isinstance(entry, dict) else None
                tid = int(tid_raw) if tid_raw is not None and str(tid_raw).isdigit() else None
            except Exception:
                tid = None
            if tid is None:
                continue
            signature = _signature_from_entry(entry)
            self._signature_to_id[signature] = tid
            self._entries[tid] = entry
            self._max_id = max(self._max_id, tid)

    @property
    def entries(self) -> Dict[int, dict]:
        return self._entries

    def get_or_create(self, building: object) -> Tuple[int, dict | None]:
        signature = _signature_from_building(building)
        existing_id = self._signature_to_id.get(signature)
        if existing_id is not None:
            return existing_id, None
        new_id = self._max_id + 1
        self._max_id = new_id
        entry = build_template_entry(building)
        entry["id"] = new_id
        self._entries[new_id] = entry
        self._signature_to_id[signature] = new_id
        return new_id, entry

    def as_sorted_list(self) -> list[dict]:
        return [self._entries[tid] for tid in sorted(self._entries.keys())]
