from __future__ import annotations
from pathlib import Path
import json
import uuid
from typing import Any, Dict, Optional
import hashlib

ACTIVE_PATH = Path('data/inventory/active/inventory_player.json')
_CACHE: Optional[Dict[str, Any]] = None
_CACHE_PATH: Optional[Path] = None
_ENSURED_PARENT_PATH: Optional[Path] = None


def _valid_uuid(x: Any) -> bool:
    try:
        uuid.UUID(str(x))
        return True
    except Exception:
        return False


def _read_active() -> Dict[str, Any]:
    global _CACHE, _CACHE_PATH
    if _CACHE is not None and _CACHE_PATH == ACTIVE_PATH:
        return _CACHE
    if not ACTIVE_PATH.exists():
        _CACHE = {}
        _CACHE_PATH = ACTIVE_PATH
        return _CACHE
    try:
        data = json.loads(ACTIVE_PATH.read_text(encoding='utf-8'))
        if not isinstance(data, dict):
            data = {}
    except Exception:
        data = {}
    _CACHE = data
    _CACHE_PATH = ACTIVE_PATH
    return _CACHE


def _write_active(data: Dict[str, Any]) -> None:
    global _CACHE, _CACHE_PATH, _ENSURED_PARENT_PATH
    _CACHE = data
    _CACHE_PATH = ACTIVE_PATH
    parent = ACTIVE_PATH.parent
    if _ENSURED_PARENT_PATH != parent:
        parent.mkdir(parents=True, exist_ok=True)
        _ENSURED_PARENT_PATH = parent
    ACTIVE_PATH.write_text(
        json.dumps(data, ensure_ascii=False, separators=(',', ':'), sort_keys=True),
        encoding='utf-8',
    )

def _canonical_snapshot(snapshot: Dict[str, Any]) -> Dict[str, Any]:
    """Reduce snapshot a un dict canónico para hashing/comparación."""
    return {
        'player_id': snapshot.get('player_id'),
        'capacity': snapshot.get('capacity', 20),
        'slots': snapshot.get('slots', []),
        'schema_version': snapshot.get('schema_version') or '1.0.0',
    }

def content_hash(snapshot: Dict[str, Any]) -> str:
    """Calcula SHA256 estable del snapshot canónico."""
    canon = _canonical_snapshot(snapshot)
    payload = json.dumps(canon, sort_keys=True, separators=(',', ':'), ensure_ascii=False)
    return hashlib.sha256(payload.encode('utf-8')).hexdigest()


def write_active_for_player(entity_id: Any, snapshot: Dict[str, Any]) -> None:
    """
    Escribe/actualiza el inventario activo del jugador identificado por entity_id
    en data/inventory/active/inventory_player.json.

    snapshot esperado: {
      'player_id': str(uuid),
      'capacity': int,
      'slots': list,
      'schema_version': str
    }
    """
    data = _read_active()
    key = str(entity_id)

    player_id = snapshot.get('player_id')
    if not _valid_uuid(player_id):
        player_id = str(uuid.uuid4())
    capacity = snapshot.get('capacity', 20)
    slots = snapshot.get('slots', [])
    schema_version = snapshot.get('schema_version') or '1.0.0'

    new_entry = {
        'player_id': player_id,
        'capacity': capacity,
        'slots': slots,
        'schema_version': schema_version,
    }
    # Evitar escrituras si no hay cambios
    old = data.get(key)
    if isinstance(old, dict):
        try:
            if _canonical_snapshot(old) == _canonical_snapshot(new_entry):
                return
        except Exception:
            pass

    data[key] = new_entry
    _write_active(data)


def read_active_for_player(entity_id: Any) -> Optional[Dict[str, Any]]:
    data = _read_active()
    return data.get(str(entity_id))
