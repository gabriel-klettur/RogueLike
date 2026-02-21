"""Editor highlight context storage for FSM editor panels."""
from __future__ import annotations
from typing import Any, Dict, Optional, Tuple

_HIGHLIGHT_SET_ID: Optional[str] = None
_HIGHLIGHT_PARAMS: Optional[Dict[str, Any]] = None


def set_editor_highlight_set(set_id: Optional[str]) -> None:
    """Store the set_id for UI highlight. Pass None to clear."""
    global _HIGHLIGHT_SET_ID, _HIGHLIGHT_PARAMS
    _HIGHLIGHT_SET_ID = set_id
    _HIGHLIGHT_PARAMS = None


def get_editor_highlight_set() -> Optional[str]:
    """Return the currently highlighted set id, if any."""
    return _HIGHLIGHT_SET_ID


def set_editor_highlight_context(set_id: Optional[str], params: Optional[Dict[str, Any]]) -> None:
    """Set highlight context (set_id + params). Pass None to clear."""
    global _HIGHLIGHT_SET_ID, _HIGHLIGHT_PARAMS
    _HIGHLIGHT_SET_ID = set_id
    _HIGHLIGHT_PARAMS = dict(params) if isinstance(params, dict) else None


def get_editor_highlight_context() -> Tuple[Optional[str], Optional[Dict[str, Any]]]:
    """Return (set_id, params) of the current highlight context, if any."""
    return _HIGHLIGHT_SET_ID, (_HIGHLIGHT_PARAMS if isinstance(_HIGHLIGHT_PARAMS, dict) else None)
