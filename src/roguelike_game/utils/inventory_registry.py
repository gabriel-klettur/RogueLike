from __future__ import annotations
from pathlib import Path
from typing import Any, Dict, Optional, Tuple
import json
from datetime import datetime

from roguelike_game.utils.inventory_sync import content_hash, _valid_uuid

REGISTRY_ROOT = Path('data/inventory/registry')


def publish_inventory(snapshot: Dict[str, Any]) -> Optional[Tuple[int, str, str]]:
    """
    Publica un snapshot de inventario en un registro versionado por player_id.
    Retorna (version, hash, path) o None si no se puede publicar.
    Estructura:
      data/inventory/registry/<player_id>/index.json
      data/inventory/registry/<player_id>/v<version>.json
    """
    pid = snapshot.get('player_id')
    if not _valid_uuid(pid):
        return None

    pid_dir = REGISTRY_ROOT / str(pid)
    pid_dir.mkdir(parents=True, exist_ok=True)
    index_path = pid_dir / 'index.json'

    # Cargar índice
    if index_path.exists():
        try:
            index = json.loads(index_path.read_text(encoding='utf-8'))
        except Exception:
            index = {}
    else:
        index = {}

    entries = index.get('entries') or []
    last_version = int(index.get('last_version') or 0)

    # Calcular hash del snapshot canónico
    h = content_hash(snapshot)

    # Si el último hash coincide, no crear nueva versión
    if entries and entries[-1].get('hash') == h:
        v = int(entries[-1].get('version') or last_version)
        v_path = str((pid_dir / f'v{v}.json').as_posix())
        return v, h, v_path

    # Crear nueva versión
    v = last_version + 1
    v_path = pid_dir / f'v{v}.json'
    try:
        v_path.write_text(json.dumps(snapshot, ensure_ascii=False, indent=2), encoding='utf-8')
    except Exception:
        return None

    # Actualizar índice
    entries.append({
        'version': v,
        'hash': h,
        'created_at': datetime.now().isoformat(timespec='seconds')
    })
    index['entries'] = entries
    index['last_version'] = v
    try:
        index_path.write_text(json.dumps(index, ensure_ascii=False, indent=2), encoding='utf-8')
    except Exception:
        pass

    return v, h, str(v_path.as_posix())


def resolve_inventory(player_id: str, version: Optional[int] = None) -> Optional[Dict[str, Any]]:
    """
    Resuelve y lee un snapshot desde el registro para un player_id dado.
    Si version es None, retorna la última.
    """
    if not _valid_uuid(player_id):
        return None
    pid_dir = REGISTRY_ROOT / str(player_id)
    index_path = pid_dir / 'index.json'
    if not index_path.exists():
        return None
    try:
        index = json.loads(index_path.read_text(encoding='utf-8'))
    except Exception:
        return None
    last_version = int(index.get('last_version') or 0)
    v = int(version or last_version)
    if v <= 0:
        return None
    v_path = pid_dir / f'v{v}.json'
    if not v_path.exists():
        return None
    try:
        return json.loads(v_path.read_text(encoding='utf-8'))
    except Exception:
        return None
