from __future__ import annotations

from datetime import datetime, timezone
from typing import Any, Dict, Optional


def now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def extract_game_context(game: Optional[Any]) -> Dict[str, Any]:
    ctx: Dict[str, Any] = {}
    try:
        if game is None:
            return ctx
        try:
            ctx["map_name"] = getattr(game, "map_name", None)
        except Exception:
            pass
        try:
            w = getattr(game, "ecs", None)
            if w and hasattr(w, "world"):
                ctx["world_level"] = getattr(w.world, "current_level", None)
        except Exception:
            pass
        try:
            cam = getattr(game, "camera", None)
            if cam:
                ctx["camera"] = {
                    "zoom": float(getattr(cam, "zoom", 0.0) or 0.0),
                    "offset_x": float(getattr(cam, "offset_x", 0.0) or 0.0),
                    "offset_y": float(getattr(cam, "offset_y", 0.0) or 0.0),
                    "screen_w": int(getattr(cam, "screen_width", 0) or 0),
                    "screen_h": int(getattr(cam, "screen_height", 0) or 0),
                }
        except Exception:
            pass
    except Exception:
        pass
    return ctx
