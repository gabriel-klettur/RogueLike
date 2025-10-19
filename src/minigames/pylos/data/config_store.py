from __future__ import annotations

import json
import os
from typing import Any, Dict, Optional, Tuple

import pygame

try:
    # Typed imports for callers
    from pylos.view.view import ViewConfig, PylosView  # type: ignore
except Exception:  # pragma: no cover - for static analysis / loose import
    ViewConfig = object  # type: ignore
    PylosView = object  # type: ignore


FILENAME = "pylos_view_prefs.json"


def _config_path() -> str:
    """Return absolute path to the persistent config JSON under data/.

    The file lives next to this module, which is inside `pylos/data/`.
    """
    return os.path.join(os.path.dirname(os.path.abspath(__file__)), FILENAME)


def _clamp(v: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, v))


def load_view_prefs(cfg: ViewConfig, view: Optional[PylosView] = None) -> Dict[str, Any]:
    """Load persisted view preferences into `cfg` and optional `view`.

    Persisted fields:
    - grid_norm_left/top/width/height (floats 0..1)
    - marble_scale (float within cfg.min/max)
    - board_manual_norm: [x, y, w, h] normalized to window size
    - show_labels: bool (UI flag)
    - show_holes: bool (UI flag)
    - show_indices: bool (UI flag)

    Returns a dict with any loaded keys (e.g., {"show_labels": True}).
    """
    path = _config_path()
    loaded: Dict[str, Any] = {}
    if not os.path.exists(path):
        return loaded
    try:
        with open(path, "r", encoding="utf-8") as f:
            data: Dict[str, Any] = json.load(f)
    except Exception:
        return loaded

    # Grid rect
    for key in ("grid_norm_left", "grid_norm_top", "grid_norm_width", "grid_norm_height"):
        if isinstance(data.get(key), (int, float)):
            val = float(_clamp(float(data[key]), 0.0, 1.0))
            setattr(cfg, key, val)
            loaded[key] = val

    # Marble scale
    ms = data.get("marble_scale")
    if isinstance(ms, (int, float)):
        msv = float(_clamp(float(ms), getattr(cfg, "marble_scale_min", 0.5), getattr(cfg, "marble_scale_max", 1.5)))
        cfg.marble_scale = msv
        loaded["marble_scale"] = msv

    # Board manual rect
    bm = data.get("board_manual_norm")
    if view is not None and isinstance(bm, (list, tuple)) and len(bm) == 4:
        win_w, win_h = cfg.width, cfg.height
        x = int(float(bm[0]) * win_w)
        y = int(float(bm[1]) * win_h)
        w = max(80, int(float(bm[2]) * win_w))
        h = max(80, int(float(bm[3]) * win_h))
        rect = pygame.Rect(x, y, w, h)
        try:
            view.set_board_manual_rect(rect)
            # Recompute layout to reflect the loaded values
            view._compute_layout()  # type: ignore[attr-defined]
            loaded["board_manual_norm"] = [bm[0], bm[1], bm[2], bm[3]]
        except Exception:
            pass

    # UI flags
    if isinstance(data.get("show_labels"), bool):
        loaded["show_labels"] = bool(data["show_labels"])
    if isinstance(data.get("show_holes"), bool):
        loaded["show_holes"] = bool(data["show_holes"])
    if isinstance(data.get("show_indices"), bool):
        loaded["show_indices"] = bool(data["show_indices"])

    return loaded


def save_view_prefs(
    cfg: ViewConfig,
    view: Optional[PylosView] = None,
    *,
    show_labels: Optional[bool] = None,
    show_holes: Optional[bool] = None,
    show_indices: Optional[bool] = None,
) -> Tuple[bool, Optional[str]]:
    """Persist view preferences to JSON under data/.

    Returns (ok, error_message). If ok is True, error_message is None.
    """
    path = _config_path()
    try:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        # Start from existing data to preserve keys we don't explicitly overwrite
        payload: Dict[str, Any] = {}
        if os.path.exists(path):
            try:
                with open(path, "r", encoding="utf-8") as f:
                    payload = json.load(f)
            except Exception:
                payload = {}
        # Overwrite core fields from cfg
        payload.update({
            "grid_norm_left": float(cfg.grid_norm_left),
            "grid_norm_top": float(cfg.grid_norm_top),
            "grid_norm_width": float(cfg.grid_norm_width),
            "grid_norm_height": float(cfg.grid_norm_height),
            "marble_scale": float(cfg.marble_scale),
        })
        # Optional board manual rect normalized to window size
        if view is not None:
            rect = None
            try:
                rect = getattr(view, "_board_rect_manual", None)
                if rect is None:
                    # fall back to current computed rect if exists
                    rect = view.get_board_rect()
            except Exception:
                rect = None
            if rect is not None:
                win_w, win_h = cfg.width, cfg.height
                payload["board_manual_norm"] = [
                    rect.x / max(1, win_w),
                    rect.y / max(1, win_h),
                    rect.width / max(1, win_w),
                    rect.height / max(1, win_h),
                ]
        # UI flags
        if show_labels is not None:
            payload["show_labels"] = bool(show_labels)
        if show_holes is not None:
            payload["show_holes"] = bool(show_holes)
        if show_indices is not None:
            payload["show_indices"] = bool(show_indices)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(payload, f, indent=2)
        return True, None
    except Exception as e:  # pragma: no cover
        return False, str(e)
