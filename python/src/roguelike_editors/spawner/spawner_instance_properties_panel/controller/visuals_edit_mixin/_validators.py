from __future__ import annotations

from typing import Optional, Tuple


def parse_int(t: str) -> Optional[int]:
    try:
        s = (t or "").strip()
        if s == "":
            return None
        return int(s)
    except (ValueError, TypeError):
        return None


def validate_template_text(owner, text: str) -> Tuple[bool, Optional[str], Optional[int]]:
    t = (text or "").strip()
    if t == "":
        return True, None, None

    try:
        tpl_id = parse_int(t)
    except Exception:
        tpl_id = None

    if tpl_id is None:
        return False, "Debe ser un número de template", None

    try:
        owner._ensure_building_templates()
    except Exception:
        pass

    try:
        valid_ids = getattr(owner, "_building_template_ids", None) or set()
    except Exception:
        valid_ids = set()

    if valid_ids and tpl_id not in valid_ids:
        return False, "Template no existe", tpl_id

    return True, None, tpl_id
