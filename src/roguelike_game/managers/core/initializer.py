import time
import logging
from pathlib import Path
from datetime import datetime

from roguelike_engine.utils.loading_screen import LoadingScreen
from roguelike_engine.log_config import build_log_filepath
from roguelike_game.managers.core.initialization import (
    InitContext,
    run_stages,
    stage_funcs,
)

logger = logging.getLogger(__name__)

class GameInitializer:

    @classmethod
    def create_and_initialize(
        cls,
        game,
        screen,
        perf_log=None,
        map_name: str = None,
        loading_bg: str | None = None,
        extra_stages: list[tuple] | None = None,
        extra_systems_stages: list[tuple] | None = None
    ) -> "GameInitializer":
        """
        Fábrica estática que construye y ejecuta la inicialización completa.
        """
        inst = cls(
            game=game,
            screen=screen,
            perf_log=perf_log,
            map_name=map_name,
            loading_bg=loading_bg,
            extra_stages=extra_stages,
            extra_systems_stages=extra_systems_stages
        )
        inst.initialize()
        return inst

    def __init__(self, game, screen, perf_log, map_name, loading_bg,
                 extra_stages, extra_systems_stages):
        self.game                   = game
        self.screen                 = screen
        self.perf_log               = perf_log
        self.map_name               = map_name
        self.loading_bg             = loading_bg
        self.extra_stages           = extra_stages or []
        self.extra_systems_stages   = extra_systems_stages or []

        # Preparar archivo de log de etapas (nombre estandarizado)
        logs_dir = Path('logs')
        logs_dir.mkdir(parents=True, exist_ok=True)
        (logs_dir / 'init').mkdir(parents=True, exist_ok=True)
        (logs_dir / 'profile').mkdir(parents=True, exist_ok=True)
        self._ts_dt = datetime.now()
        self.stage_log_path = build_log_filepath('stage_times', directory=str(logs_dir / 'init'), extension='log', now_dt=self._ts_dt)
        with open(self.stage_log_path, 'w', encoding='utf-8') as f:
            f.write(f"[{self._ts_dt.isoformat()}] Inicio de inicialización\n")
        # Añadir FileHandler para log de etapas
        fh = logging.FileHandler(self.stage_log_path, mode='a', encoding='utf-8')
        fh.setLevel(logging.INFO)
        fh.setFormatter(logging.Formatter('%(asctime)s %(message)s', datefmt='[%Y-%m-%dT%H:%M:%S]'))
        logging.getLogger().addHandler(fh)

    def initialize(self):
        # Crear loader temprano para feedback visual inmediato
        self.game.loader = LoadingScreen(self.screen, self.loading_bg)

        # Construir contexto y pipeline modular
        ctx = InitContext(
            game=self.game,
            screen=self.screen,
            perf_log=self.perf_log,
            map_name=self.map_name,
            loading_bg=self.loading_bg,
            stage_log_path=self.stage_log_path,
            ts_dt=self._ts_dt,
        )
        self._ctx = ctx

        stages: list[tuple[str, object]] = []
        stages.append(("Pantalla, reloj y fuente", stage_funcs.setup_display))
        stages.append(("Mundo (sin estado)", stage_funcs.setup_world))
        stages.append(("Cargando estado de mundo", stage_funcs.load_world_state))
        stages.append(("Creando loader", stage_funcs.create_loader))

        for msg, fn in self.extra_systems_stages:
            stages.append((msg, fn))

        defaults = [
            ("Inicializando audio", stage_funcs.init_audio),
            ("Inicializando estado Principal", stage_funcs.init_state),
            ("Cargando mapa", stage_funcs.init_map),
            ("Auto-importar edificios (DEV)", stage_funcs.dev_auto_import_buildings),
            ("Cargando edificios", stage_funcs.init_buildings),
            ("Inicializando ECS", stage_funcs.init_ecs),
            ("Cargando catálogo de ítems", stage_funcs.init_items),
            ("Cargando editor de ítems", stage_funcs.init_item_editor),
            ("Cargando Z-layer", stage_funcs.init_z_layer),
            ("Cargando editor de edificios", stage_funcs.init_buildings_editor),
            ("Cargando editor de tiles", stage_funcs.init_tile_editor),
            ("Cargando editor de mapa", stage_funcs.init_map_editor),
            ("Cargando editor de inventario", stage_funcs.init_inventory_editor),
            ("Cargando editor de entidades", stage_funcs.init_entities_editor),
            ("Cargando editor de hechizos", stage_funcs.init_spells_editor),
            ("Cargando editor de partículas", stage_funcs.init_particles_editor),
            ("Cargando editor de spawner", stage_funcs.init_spawner_editor),
            ("Cargando editor de iluminación", stage_funcs.init_lighting_editor),
            ("Cargando minimapa", stage_funcs.init_minimap),
            ("Inicializando renderizador", stage_funcs.init_renderer),
            ("Inicializando menú", stage_funcs.init_menu),
        ]
        stages.extend(defaults)

        for msg, fn in self.extra_stages:
            stages.append((msg, fn))

        def _on_stage_completed(msg: str, fn, elapsed: float) -> None:
            # Después de cargar el estado del mundo, emplazar niveles diferidos
            if msg == "Cargando estado de mundo":
                try:
                    stage_funcs.handle_deferred_levels(self._ctx)
                except Exception:
                    pass

        run_stages(ctx, stages, on_stage_completed=_on_stage_completed)