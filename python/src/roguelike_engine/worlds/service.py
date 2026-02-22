from __future__ import annotations

from pathlib import Path
import json
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
        # Asegurar estructura mínima del mundo antes de activar
        try:
            self.scaffold_world_if_missing(world_id)
        except Exception:
            pass
        # Crear archivo vacío de instancias en spawners (solo per-world instances)
        try:
            sdir = self.worlds_root / world_id / 'spawners'
            try:
                sdir.mkdir(parents=True, exist_ok=True)
            except Exception:
                pass
            p = sdir / 'spawners_instances.json'
            if not p.exists():
                with p.open('w', encoding='utf-8') as f:
                    json.dump([], f, indent=2)
        except Exception:
            pass
        # Crear archivo vacío de instancias de partículas por mundo
        try:
            pdir = self.worlds_root / world_id / 'particles'
            try:
                pdir.mkdir(parents=True, exist_ok=True)
            except Exception:
                pass
            p = pdir / 'particles_instances.json'
            if not p.exists():
                with p.open('w', encoding='utf-8') as f:
                    json.dump([], f, indent=2)
        except Exception:
            pass

        # Si el mundo destino es "en blanco" (zones.json vacío), limpiar instancias de buildings
        try:
            wdir = self.worlds_root / world_id
            zindex = wdir / 'zones' / 'zones.json'
            blank = False
            if zindex.exists():
                try:
                    txt = zindex.read_text(encoding='utf-8').strip()
                    blank = (not txt) or (json.loads(txt) == {})
                except Exception:
                    blank = False
            else:
                # No hay índice aún: trátalo como en blanco
                blank = True
            if blank:
                bdir = wdir / 'buildings'
                try:
                    bdir.mkdir(parents=True, exist_ok=True)
                except Exception:
                    pass
                inst_path = bdir / 'buildings_instances.json'
                with inst_path.open('w', encoding='utf-8') as f:
                    json.dump([], f, indent=2)
                logger.info(f"[WorldService] Blank world detected; cleared buildings instances: {inst_path}")
        except Exception:
            pass
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
        # Redirigir rutas de partículas (instances por mundo; templates globales)
        try:
            self._set_particles_paths_for_world(self.current)
        except Exception as e:
            logger.debug(f"[WorldService] Skipped particles path redirection: {e}")
        # Reinicializar overlay store para usar el overlays_dir del mundo activo
        try:
            from roguelike_engine.map.model.overlay import overlay_manager as _ovmgr
            from roguelike_engine.map.model.overlay.json_store import JsonOverlayStore as _JS
            # Reinstanciar el store con la ruta actual del mundo
            _ovmgr.set_overlay_store(_JS())
        except Exception as e:
            logger.debug(f"[WorldService] Could not reset overlay store for world '{world_id}': {e}")
        logger.info(f"[WorldService] Mundo activo: {world_id}")
        # Bootstrap layout for brand-new empty worlds: create a lobby zone at (0,0)
        # and a default ground overlay so the player does not see an entirely
        # black canvas on first teleport. This only applies when the world has
        # no user-defined zones yet.
        try:
            self._ensure_default_lobby_for_empty_world()
        except Exception as e:
            logger.debug(f"[WorldService] Skipped default lobby bootstrap for world '{world_id}': {e}")

    # ---------------------------------------------------------------------
    # Scaffolding
    # ---------------------------------------------------------------------
    def scaffold_world_if_missing(self, world_id: str) -> None:
        """Crea estructura mínima para un mundo nuevo para que el editor funcione.

        Estructura:
        - worlds/<world_id>/zones/zones.json (si no existe)
        - worlds/<world_id>/zones/overlays/
        - worlds/<world_id>/collisions/
        - worlds/<world_id>/buildings/
        """
        if not world_id:
            return
        wdir = self.worlds_root / world_id
        zdir = wdir / 'zones'
        odir = zdir / 'overlays'
        cdir = wdir / 'collisions'
        bdir = wdir / 'buildings'
        sdir = wdir / 'spawners'
        try:
            zdir.mkdir(parents=True, exist_ok=True)
            odir.mkdir(parents=True, exist_ok=True)
            cdir.mkdir(parents=True, exist_ok=True)
            bdir.mkdir(parents=True, exist_ok=True)
            sdir.mkdir(parents=True, exist_ok=True)
        except Exception:
            pass
        zindex = zdir / 'zones.json'
        if not zindex.exists():
            # Crear índice de zonas vacío; el editor creará zonas luego
            try:
                with zindex.open('w', encoding='utf-8') as f:
                    json.dump({}, f, indent=2)
            except Exception:
                pass

        # Crear archivos vacíos en buildings para compatibilidad inmediata con el editor
        try:
            # Solo instances por mundo; templates permanecen globales en data/buildings/
            empty_list_files = [
                bdir / 'buildings_instances.json',
            ]
            empty_dict_files = [
                bdir / 'buildings_collisions_by_spawn_id.json',
                bdir / 'buildings_collisions_by_building_instance_id.json',
            ]
            for p in empty_list_files:
                if not p.exists():
                    with p.open('w', encoding='utf-8') as f:
                        json.dump([], f, indent=2)
            for p in empty_dict_files:
                if not p.exists():
                    with p.open('w', encoding='utf-8') as f:
                        json.dump({}, f, indent=2)
        except Exception:
            pass

    def _ensure_default_lobby_for_empty_world(self) -> None:
        """Ensure a freshly created world has a minimal playable layout.

        Policy:
        - If ZONES_INDEX has no user-defined zones (only blank / sentinels),
          create a 'lobby' zone at tile offset (0, 0).
        - Persist a Ground overlay for 'lobby' using the default floor tile so
          the player sees a basic floor instead of a fully black world.

        This helper is intentionally conservative: if the world already has any
        user zones, it does nothing to avoid overriding designer-authored data.
        """
        from roguelike_engine.config.map_config import global_map_settings

        try:
            zindex = global_map_settings.ZONES_INDEX
        except Exception:
            return

        # Load current zones configuration
        import json as _json
        try:
            if not zindex.exists():
                data = {}
            else:
                txt = zindex.read_text(encoding="utf-8").strip()
                data = _json.loads(txt) if txt else {}
        except Exception:
            return

        user_keys = [k for k in data.keys() if str(k).lower() not in ("no zone", "no-zone", "no_zone")]
        if user_keys:
            # World already has user-defined zones; do not auto-bootstrap
            return

        # Create a simple lobby zone at (0,0) in tiles
        data = {"lobby": [0, 0]}
        try:
            zindex.parent.mkdir(parents=True, exist_ok=True)
        except Exception:
            pass
        try:
            zindex.write_text(_json.dumps(data, indent=2), encoding="utf-8")
        except Exception:
            return

        # Refresh cached offsets so 'lobby' becomes a first-class zone
        try:
            global_map_settings.refresh_zone_offsets()
        except Exception:
            pass

        # Build a default Ground overlay for the lobby using the configured
        # default floor tile. If overlay codes are not available, we skip
        # silently to avoid hard failures in unusual asset setups.
        try:
            from roguelike_engine.map.model.layer import Layer
            import roguelike_engine.map.model.overlay.overlay_manager as _ovmgr
            from roguelike_engine.config.config_tiles import OVERLAY_CODE_MAP, DEFAULT_TILE_MAP
        except Exception:
            return

        try:
            floor_asset = DEFAULT_TILE_MAP.get(".", "floor")
        except Exception:
            floor_asset = "floor"

        default_code = None
        try:
            for code, name in OVERLAY_CODE_MAP.items():
                if name == floor_asset:
                    default_code = code
                    break
        except Exception:
            default_code = None

        if not default_code:
            # No suitable overlay code found; rely on existing rendering fallback
            return

        try:
            zone_w, zone_h = global_map_settings.zone_size
        except Exception:
            zone_w = zone_h = 50

        grid = [[default_code for _ in range(zone_w)] for _ in range(zone_h)]
        layers = {Layer.Ground: grid}
        try:
            _ovmgr.save_layers("lobby", layers)
        except Exception:
            pass

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
            cfg.BUILDINGS_INSTANCES_PATH = str(bdir / "buildings_instances.json")
            cfg.BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH = str(bdir / "buildings_collisions_by_spawn_id.json")
            cfg.BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH = str(bdir / "buildings_collisions_by_building_instance_id.json")
        except Exception:
            pass
        # Propagar (si módulos ayudantes capturaron a import-time). Best-effort.
        try:
            import roguelike_editors.buildings.utils.split_io as _splitio
            _splitio.BUILDINGS_INSTANCES_PATH = cfg.BUILDINGS_INSTANCES_PATH
        except Exception:
            pass
        try:
            import roguelike_editors.buildings.utils.collisions_io as _collio
            _collio.BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH = cfg.BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH
            _collio.BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH = cfg.BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH
        except Exception:
            pass
        # Propagar a módulos públicos de save/load que importaron constantes por nombre
        try:
            import roguelike_editors.buildings.utils.save_buildings_to_json as _save_mod
            _save_mod.BUILDINGS_INSTANCES_PATH = cfg.BUILDINGS_INSTANCES_PATH
        except Exception:
            pass
        try:
            import roguelike_editors.buildings.utils.load_buildings_from_json as _load_mod
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

    def _set_particles_paths_for_world(self, profile: WorldProfile) -> None:
        """Redirige rutas de partículas a la carpeta del mundo activo.
        - Templates siguen siendo globales en data/particles/particles.json
        - Instances son por mundo: worlds/<world_id>/particles/particles_instances.json
        """
        pdir = profile.particles_dir
        try:
            pdir.mkdir(parents=True, exist_ok=True)
        except Exception:
            pass
        try:
            cfg.PARTICLES_INSTANCES_PATH = str(pdir / "particles_instances.json")
        except Exception:
            pass


# Singleton de servicio de mundos
world_service = WorldService()
