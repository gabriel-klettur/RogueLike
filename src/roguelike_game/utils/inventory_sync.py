from __future__ import annotations
from pathlib import Path
import json
import uuid
from typing import Any, Dict, Optional
import hashlib

ACTIVE_PATH = Path('data/inventory/active/inventory_player.json')


def _valid_uuid(x: Any) -> bool:
    try:
        uuid.UUID(str(x))
        return True
    except Exception:
        return False


def _read_active() -> Dict[str, Any]:
    if not ACTIVE_PATH.exists():
        return {}
    try:
        return json.loads(ACTIVE_PATH.read_text(encoding='utf-8'))
    except Exception:
        return {}


def _write_active(data: Dict[str, Any]) -> None:
    ACTIVE_PATH.parent.mkdir(parents=True, exist_ok=True)
    ACTIVE_PATH.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')

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
            old_hash = content_hash(old)
            new_hash = content_hash(new_entry)
            if old_hash == new_hash:
                return  # Sin cambios relevantes
        except Exception:
            pass

    data[key] = new_entry
    _write_active(data)


def read_active_for_player(entity_id: Any) -> Optional[Dict[str, Any]]:
    data = _read_active()
    return data.get(str(entity_id))
