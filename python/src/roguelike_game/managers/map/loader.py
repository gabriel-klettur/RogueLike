"""
Módulo de carga de mapas con caching y profiling.
"""
from pathlib import Path
import time
import logging
import pickle
import cProfile
import pstats
from datetime import datetime
from roguelike_engine.log_config import build_log_filepath
from typing import Any, Optional

from roguelike_engine.map.controller.map_controller import build_map
from roguelike_engine.config.map_config import global_map_settings

logger = logging.getLogger(__name__)
logger.setLevel(logging.INFO)

class MapLoader:
    """
    Gestiona la carga de mapas con cache y profiling.
    """
    def __init__(self, cache_dir: Optional[str | Path] = None):
        if cache_dir is None:
            root = Path(__file__).resolve().parents[4]
            self.cache_dir = root / 'data' / 'cache'
        else:
            self.cache_dir = Path(cache_dir)
        self.cache_dir.mkdir(parents=True, exist_ok=True)

    def load(self, map_name: str) -> Any:
        # Configuración de zones JSON
        global_map_settings.use_zones_json = True
        global_map_settings.__dict__.pop('zone_offsets', None)

        # Cache por mundo
        try:
            world_id = getattr(global_map_settings, 'current_world', 'base')
        except Exception:
            world_id = 'base'
        cache_file = self.cache_dir / f'map_{world_id}_{map_name}.pkl'
        overlays_dir = global_map_settings.overlays_dir
        # Si zones.json está vacío, no reutilizar cache (mundo en blanco editable)
        try:
            zindex = global_map_settings.ZONES_INDEX
            if zindex.exists():
                import json as _json
                txt = zindex.read_text(encoding='utf-8').strip()
                is_empty = False
                if txt == "{}" or txt == "":
                    is_empty = True
                else:
                    try:
                        data = _json.loads(txt)
                        is_empty = isinstance(data, dict) and len(data) == 0
                    except Exception:
                        pass
                if is_empty and cache_file.exists():
                    cache_file.unlink(missing_ok=True)
                    logger.info(" Cache invalidated: empty zones.json (blank world)")
        except Exception:
            pass
        # Invalidar cache si hay overlays más recientes
        try:
            cache_mtime = cache_file.stat().st_mtime
            for f in overlays_dir.glob('*.overlay.json'):
                if f.stat().st_mtime > cache_mtime:
                    cache_file.unlink()
                    logger.info(f" Cache invalidated: {f.name}")
                    break
            # Invalidar cache si el índice de zonas cambió (alta/baja/movimiento de zonas)
            try:
                zones_mtime = global_map_settings.ZONES_INDEX.stat().st_mtime
                if zones_mtime > cache_mtime:
                    cache_file.unlink()
                    logger.info(" Cache invalidated: zones.json updated")
            except FileNotFoundError:
                # Si no existe zones.json, no invalidamos por este motivo
                pass
        except Exception:
            pass

        # Intentar cargar cache
        if cache_file.exists():
            try:
                t0 = time.perf_counter()
                with open(cache_file, 'rb') as f:
                    result = pickle.load(f)
                t1 = time.perf_counter()
                logger.info(f"Loaded cache in {t1-t0:.4f}s")
                return result
            except Exception as e:
                logger.warning(f"Cache load failed: {e}")
                cache_file.unlink(missing_ok=True)

        # Generar mapa
        profile = cProfile.Profile()
        profile.enable()
        t0 = time.perf_counter()
        result = build_map(map_name)
        t1 = time.perf_counter()
        profile.disable()
        logger.info(f"Built map in {t1-t0:.4f}s")

        # Guardar cache
        try:
            with open(cache_file, 'wb') as f:
                pickle.dump(result, f)
        except TypeError as e:
            logger.warning(f" Skipping cache dump: {e}")

        # Dump profiling stats
        logs_dir = Path('logs')
        (logs_dir / 'profile').mkdir(parents=True, exist_ok=True)
        profile_log = build_log_filepath(f'build_map_profile_{map_name}', directory=str(logs_dir / 'profile'), extension='log', now_dt=datetime.now())
        with open(profile_log, 'w') as pf:
            stats = pstats.Stats(profile, stream=pf)
            stats.sort_stats('tottime').print_stats(30)
        logger.info(f"Profile stats saved to {profile_log}")

        return result
