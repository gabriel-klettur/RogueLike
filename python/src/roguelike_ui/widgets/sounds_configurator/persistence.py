from __future__ import annotations

from pathlib import Path
import json
from typing import Any, Dict, List


class AudioJsonRepository:
    """Thin repository around data/config/audio.json.

    Responsible only for reading/writing the raw JSON payload. Schema decisions
    are handled at upper layers (model).
    """

    def __init__(self, path: Path | str | None = None) -> None:
        self.path: Path | None = Path(path) if path else Path("data/config/audio.json")

    def load(self) -> Dict[str, Any]:
        if not self.path:
            return {}
        try:
            if self.path.exists():
                return json.loads(self.path.read_text(encoding="utf-8"))
            return {}
        except Exception:
            return {}

    def save(self, data: Dict[str, Any]) -> None:
        if not self.path:
            return
        try:
            self.path.write_text(json.dumps(data, indent=2), encoding="utf-8")
        except Exception:
            # Persist errors are non-fatal for the UI; ignore to avoid crashing the menu.
            pass


class ZonesRepository:
    """Reader for zones list at data/map/zones/zones.json."""

    def __init__(self, path: Path | str | None = None) -> None:
        self.path: Path | None = Path(path) if path else Path("data/map/zones/zones.json")

    def load_zones(self) -> List[str]:
        if not self.path:
            return []
        try:
            if not self.path.exists():
                return []
            payload = json.loads(self.path.read_text(encoding="utf-8"))
            if isinstance(payload, dict):
                return sorted(str(k) for k in payload.keys())
            return []
        except Exception:
            return []
