from __future__ import annotations
from pathlib import Path
from typing import Protocol, Optional, List, Dict, Any
import logging

logger = logging.getLogger(__name__)

# JSON backend (prefer orjson)
try:
    import orjson as _json
    _USE_ORJSON = True
except ImportError:  # pragma: no cover - fallback
    import json as _json  # type: ignore
    _USE_ORJSON = False

from .models import WorldSnapshot, SaveSlot


class IWorldRepository(Protocol):
    """
    Abstracción de persistencia de snapshots del mundo.
    Se trabaja en términos de rutas de archivo para compatibilidad retro.
    """
    def save_to_path(self, path: str, snapshot: WorldSnapshot) -> None: ...
    def load_from_path(self, path: str) -> Dict[str, Any]: ...
    def create_new_slot(self, base_dir: Path, filename: str, snapshot: WorldSnapshot) -> str: ...
    def list_slots(self, base_dir: Path) -> List[SaveSlot]: ...
    def get_index_path(self, base_dir: Path) -> Path: ...
    def get_current_path(self, base_dir: Path) -> Optional[str]: ...
    def set_current_path(self, base_dir: Path, path: str) -> None: ...


class JSONWorldRepository:
    """
    Implementación JSON con guardado atómico e índice de slots (index.json).
    
    index.json schema:
    {
      "current_path": str | null,
      "slots": [ {slot_dict}, ... ]
    }
    """

    INDEX_FILENAME = "index.json"

    def _atomic_write(self, path: Path, content: bytes) -> None:
        tmp = path.with_suffix(path.suffix + ".tmp")
        tmp.write_bytes(content)
        tmp.replace(path)

    def _dump_json_bytes(self, data: Any) -> bytes:
        if _USE_ORJSON:
            return _json.dumps(data)
        else:
            # Ensure pretty and UTF-8
            import json as _j
            return (_j.dumps(data, ensure_ascii=False, indent=2)).encode("utf-8")

    def _load_json(self, path: Path) -> Any:
        raw = path.read_bytes()
        if _USE_ORJSON:
            return _json.loads(raw)
        else:  # pragma: no cover - fallback
            import json as _j
            return _j.loads(raw.decode("utf-8"))

    def get_index_path(self, base_dir: Path) -> Path:
        return base_dir / self.INDEX_FILENAME

    def _ensure_index(self, base_dir: Path) -> Dict[str, Any]:
        base_dir.mkdir(parents=True, exist_ok=True)
        idx_path = self.get_index_path(base_dir)
        if not idx_path.exists():
            idx = {"current_path": None, "slots": []}
            self._atomic_write(idx_path, self._dump_json_bytes(idx))
            return idx
        try:
            return self._load_json(idx_path)
        except Exception as e:
            logger.warning(f"[WorldRepo] Índice corrupto en {idx_path}: {e}. Regenerando.")
            idx = {"current_path": None, "slots": []}
            self._atomic_write(idx_path, self._dump_json_bytes(idx))
            return idx

    def _update_index_slot(self, base_dir: Path, path: Path, created_at: Optional[str] = None) -> None:
        idx = self._ensure_index(base_dir)
        slots: List[Dict[str, Any]] = idx.get("slots", [])
        path_str = str(path)
        size = path.stat().st_size if path.exists() else None
        # upsert by path
        for s in slots:
            if s.get("path") == path_str:
                s["size_bytes"] = size
                if created_at:
                    s["created_at"] = created_at
                break
        else:
            slots.append({
                "slot_id": Path(path_str).stem,
                "path": path_str,
                "created_at": created_at or Path(path_str).stat().st_mtime,
                "size_bytes": size,
                "summary": None,
            })
        idx["slots"] = slots
        self._atomic_write(self.get_index_path(base_dir), self._dump_json_bytes(idx))

    # Public API
    def save_to_path(self, path: str, snapshot: WorldSnapshot) -> None:
        save_path = Path(path)
        save_path.parent.mkdir(parents=True, exist_ok=True)
        data = snapshot.to_dict()
        content = self._dump_json_bytes(data)
        self._atomic_write(save_path, content)
        # update index
        self._update_index_slot(save_path.parent, save_path, (snapshot.meta or {}).get("created_at") if isinstance(snapshot.meta, dict) else None)

    def load_from_path(self, path: str) -> Dict[str, Any]:
        load_path = Path(path)
        if not load_path.is_file():
            raise FileNotFoundError(f"No se encontró el archivo de estado del mundo: {load_path}")
        try:
            data = self._load_json(load_path)
            # asegurar version (legacy)
            if "version" not in data:
                data["version"] = 1
            return data
        except Exception as e:
            logger.warning(f"[WorldRepo] Error cargando JSON en {load_path}: {e}")
            return {}

    def create_new_slot(self, base_dir: Path, filename: str, snapshot: WorldSnapshot) -> str:
        base_dir.mkdir(parents=True, exist_ok=True)
        path = base_dir / filename
        self.save_to_path(str(path), snapshot)
        self.set_current_path(base_dir, str(path))
        return str(path)

    def list_slots(self, base_dir: Path) -> List[SaveSlot]:
        idx = self._ensure_index(base_dir)
        slots = []
        for s in idx.get("slots", []):
            try:
                slots.append(SaveSlot(
                    slot_id=s.get("slot_id") or Path(s["path"]).stem,
                    path=Path(s["path"]),
                    created_at=str(s.get("created_at") or ""),
                    size_bytes=s.get("size_bytes"),
                    summary=s.get("summary"),
                ))
            except Exception:
                continue
        # si no hay índice, intentar descubrir archivos legacy
        if not slots and base_dir.exists():
            for p in sorted(base_dir.glob("partida_*.json")):
                slots.append(SaveSlot(slot_id=p.stem, path=p, created_at=""))
        return slots

    def get_current_path(self, base_dir: Path) -> Optional[str]:
        idx = self._ensure_index(base_dir)
        cur = idx.get("current_path")
        return str(cur) if cur else None

    def set_current_path(self, base_dir: Path, path: str) -> None:
        idx = self._ensure_index(base_dir)
        idx["current_path"] = path
        self._atomic_write(self.get_index_path(base_dir), self._dump_json_bytes(idx))
