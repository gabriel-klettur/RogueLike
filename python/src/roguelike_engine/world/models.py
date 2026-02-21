from __future__ import annotations
from dataclasses import dataclass, field, asdict
from pathlib import Path
from typing import Dict, Optional, Any


CURRENT_WORLD_SNAPSHOT_VERSION = 1


@dataclass
class WorldSnapshot:
    """
    Modelo tipado de snapshot de mundo. Los campos son estables y versionados
    para permitir migraciones futuras.
    """
    version: int = CURRENT_WORLD_SNAPSHOT_VERSION
    player: Optional[Dict[str, Any]] = None
    npcs: Dict[str, Any] = field(default_factory=dict)
    levels: Dict[str, Dict[str, Any]] = field(default_factory=dict)
    player_inventory: Optional[Dict[str, Any]] = None
    npc_inventories: Optional[Dict[str, Any]] = None
    meta: Optional[Dict[str, Any]] = None
    # Persisted game time (minute of day)
    time: Optional[Dict[str, Any]] = None

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


@dataclass
class SaveSlot:
    """
    Representa un slot de guardado (entrada del índice).
    """
    slot_id: str
    path: Path
    created_at: str
    size_bytes: Optional[int] = None
    summary: Optional[str] = None

    def as_dict(self) -> Dict[str, Any]:
        return {
            "slot_id": self.slot_id,
            "path": str(self.path),
            "created_at": self.created_at,
            "size_bytes": self.size_bytes,
            "summary": self.summary,
        }
