from __future__ import annotations

from typing import Any, Optional, Type
import json


def convert_text_to_type(text: str, target_type: Optional[Type[Any]]) -> Any:
    """Convert a string to the given target type.

    Supports: bool, int, float, str, dict, list. For dict/list uses JSON parsing
    with safe fallbacks.
    """
    if target_type is None or target_type is str:
        return text
    try:
        if target_type is bool:
            return text.lower() in ("true", "1", "yes")
        if target_type is int:
            return int(text)
        if target_type is float:
            return float(text)
        if target_type is dict:
            try:
                value = json.loads(text)
                return value if isinstance(value, dict) else {}
            except Exception:
                return {}
        if target_type is list:
            try:
                value = json.loads(text)
                return value if isinstance(value, list) else ([str(text)] if text else [])
            except Exception:
                return [str(text)] if text else []
    except Exception:
        # Fallback: if conversion fails, return original text
        return text
    # Unknown type -> str
    return text


def convert_like(old_value: Any, text: str) -> Any:
    """Convert text attempting to preserve the type of old_value when possible."""
    if isinstance(old_value, bool):
        return text.lower() in ("true", "1", "yes")
    if isinstance(old_value, int) and not isinstance(old_value, bool):
        try:
            return int(text)
        except Exception:
            return text
    if isinstance(old_value, float):
        try:
            return float(text)
        except Exception:
            return text
    # Default: keep as string
    return text
