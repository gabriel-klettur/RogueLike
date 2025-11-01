from __future__ import annotations

from pathlib import Path
from typing import Dict
import logging

from roguelike_engine.config.config import DATA_DIR
from roguelike_engine.config import config as cfg
from roguelike_engine.config.map_config import global_map_settings
from .profile import WorldProfile

logger = logging.getLogger(__name__)


class WorldService:
    """Servicio de mundos: descubre perfiles y activa el mundo actual.

    Nota: es deliberadamente ligero; delega en MapSettings para invalidar caches
    y refrescar offsets.
    """
    def __init__(self, worlds_root: Path | None = None):
        self.worlds_root: Path = Path(worlds_root) if worlds_root is not None else (Path(DATA_DIR) / "worlds")
        self.registry: Dict[str, WorldProfile] = {}
        self.current: WorldProfile = WorldProfile(global_map_settings.current_world, self.worlds_root)
        self.discover()

    def discover(self) -> Dict[str, WorldProfile]:
        self.registry.clear()
        if not self.worlds_root.exists():
            try:
                self.worlds_root.mkdir(parents=True, exist_ok=True)
            except Exception:
                pass
            return self.registry
        for p in self.worlds_root.iterdir():
            if p.is_dir():
                self.registry[p.name] = WorldProfile(p.name, self.worlds_root)
        # Asegurar perfil actual
        cid = global_map_settings.current_world
        self.current = self.registry.get(cid, WorldProfile(cid, self.worlds_root))
        return self.registry

    def activate(self, world_id: str) -> None:
        if not world_id:
            return
        if world_id not in self.registry:
            # Crear entrada on-the-fly
            self.registry[world_id] = WorldProfile(world_id, self.worlds_root)
        self.current = self.registry[world_id]
        # Actualizar MapSettings
        try:
            global_map_settings.current_world = world_id
            # Invalidar offsets cacheados
            global_map_settings.refresh_zone_offsets()
        except Exception:
            pass
        # Redirigir rutas de edificios (fase transición)
        try:
            self._set_buildings_paths_for_world(self.current)
        except Exception as e:
            logger.debug(f"[WorldService] Skipped buildings path redirection: {e}")
        logger.info(f"[WorldService] Mundo activo: {world_id}")

    def _set_buildings_paths_for_world(self, profile: WorldProfile) -> None:
        """Redirige rutas de buildings a la carpeta del mundo activo (modo transición).
        Mantiene compatibilidad con módulos que cachearon constantes.
        """
        bdir = profile.buildings_dir
        try:
            bdir.mkdir(parents=True, exist_ok=True)
        except Exception:
            pass
        # Reasignar constantes en config
        try:
            cfg.BUILDINGS_TEMPLATES_PATH = str(bdir / "buildings_templates.json")
            cfg.BUILDINGS_INSTANCES_PATH = str(bdir / "buildings_instances.json")
            cfg.BUILDINGS_COLLISIONS_BY_IMAGE_PATH = str(bdir / "buildings_collisions_by_image.json")
            cfg.BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH = str(bdir / "buildings_collisions_by_spawn_id.json")
            cfg.BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH = str(bdir / "buildings_collisions_by_building_instance_id.json")
        except Exception:
            pass
        # Propagar (si módulos ayudantes capturaron a import-time). Best-effort.
        try:
            import roguelike_editors.buildings.utils.split_io as _splitio
            _splitio.BUILDINGS_TEMPLATES_PATH = cfg.BUILDINGS_TEMPLATES_PATH
            _splitio.BUILDINGS_INSTANCES_PATH = cfg.BUILDINGS_INSTANCES_PATH
        except Exception:
            pass
        try:
            import roguelike_editors.buildings.utils.collisions_io as _collio
            _collio.BUILDINGS_COLLISIONS_BY_IMAGE_PATH = cfg.BUILDINGS_COLLISIONS_BY_IMAGE_PATH
            _collio.BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH = cfg.BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH
            _collio.BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH = cfg.BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH
        except Exception:
            pass
        # Propagar a módulos públicos de save/load que importaron constantes por nombre
        try:
            import roguelike_editors.buildings.utils.save_buildings_to_json as _save_mod
            _save_mod.BUILDINGS_TEMPLATES_PATH = cfg.BUILDINGS_TEMPLATES_PATH
            _save_mod.BUILDINGS_INSTANCES_PATH = cfg.BUILDINGS_INSTANCES_PATH
        except Exception:
            pass
        try:
            import roguelike_editors.buildings.utils.load_buildings_from_json as _load_mod
            _load_mod.BUILDINGS_TEMPLATES_PATH = cfg.BUILDINGS_TEMPLATES_PATH
            _load_mod.BUILDINGS_INSTANCES_PATH = cfg.BUILDINGS_INSTANCES_PATH
        except Exception:
            pass
        # Propagar a panel de colisiones de edificios (usa from-import de constantes)
        try:
            import roguelike_editors.buildings.buildings_colliders_panel.building_colliders_panel_events as _panel
            if hasattr(_panel, 'BUILDINGS_COLLISIONS_BY_IMAGE_PATH'):
                _panel.BUILDINGS_COLLISIONS_BY_IMAGE_PATH = cfg.BUILDINGS_COLLISIONS_BY_IMAGE_PATH
            if hasattr(_panel, 'BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH'):
                _panel.BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH = cfg.BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH
            if hasattr(_panel, 'BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH'):
                _panel.BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH = cfg.BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH
        except Exception:
            pass


# Singleton de servicio de mundos
world_service = WorldService()
