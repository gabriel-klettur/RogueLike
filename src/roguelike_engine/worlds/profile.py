from __future__ import annotations

from pathlib import Path
from dataclasses import dataclass
from roguelike_engine.config.config import DATA_DIR


@dataclass(frozen=True)
class WorldProfile:
    """Perfil de mundo: resuelve rutas para datos del mundo activo.

    - base_dir: carpeta raíz del mundo (data/worlds/<world_id>)
    - zones_index: índice de zonas (zones.json)
    - overlays_dir: directorio de overlays por zona
    - collisions_dir: directorio de colisiones por zona
    - buildings_dir: directorio de persistencia de edificios
    """
    world_id: str
    worlds_root: Path | None = None

    @property
    def base_dir(self) -> Path:
        root = Path(self.worlds_root) if self.worlds_root is not None else Path(DATA_DIR) / "worlds"
        return root / self.world_id

    @property
    def zones_index(self) -> Path:
        return self.base_dir / "zones" / "zones.json"

    @property
    def overlays_dir(self) -> Path:
        return self.base_dir / "zones" / "overlays"

    @property
    def collisions_dir(self) -> Path:
        return self.base_dir / "collisions"

    @property
    def buildings_dir(self) -> Path:
        return self.base_dir / "buildings"
