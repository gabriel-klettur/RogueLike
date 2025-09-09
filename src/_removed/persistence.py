from __future__ import annotations
from typing import Any, Dict
import logging

from .repository import JSONWorldRepository
from .models import WorldSnapshot, CURRENT_WORLD_SNAPSHOT_VERSION

logger = logging.getLogger(__name__)

_REPO = JSONWorldRepository()

def save_world_state(path: str, state: Dict[str, Any]) -> None:
    """
    DEPRECADO: usa JSONWorldRepository.save_to_path con WorldSnapshot.
    Adaptador fino para compatibilidad con código existente.
    """
    try:
        logger.warning("[persistence] save_world_state está DEPRECADO. Usa JSONWorldRepository.save_to_path.")
        snapshot = WorldSnapshot(
            version=state.get("version", CURRENT_WORLD_SNAPSHOT_VERSION),
            player=state.get("player"),
            npcs=state.get("npcs", {}),
            levels=state.get("levels", {}),
            player_inventory=state.get("player_inventory"),
            npc_inventories=state.get("npc_inventories"),
            meta=state.get("meta"),
        )
        _REPO.save_to_path(path, snapshot)
    except Exception as e:
        logger.warning("[persistence] save_world_state deprecated adapter failed: %s", e)
        # Best-effort: reintentar con dict crudo usando la misma repo (no público)
        try:
            # fallback minimalista: convertir a bytes con json estándar
            import json
            from pathlib import Path
            p = Path(path)
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
        except Exception:
            pass

def load_world_state(path: str) -> Dict[str, Any]:
    """
    DEPRECADO: usa JSONWorldRepository.load_from_path.
    Devuelve un diccionario compatible.
    """
    try:
        logger.warning("[persistence] load_world_state está DEPRECADO. Usa JSONWorldRepository.load_from_path.")
        return _REPO.load_from_path(path)
    except Exception as e:
        logger.warning("[persistence] load_world_state deprecated adapter failed: %s", e)
        return {}