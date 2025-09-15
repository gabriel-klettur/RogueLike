import sys
import os

os.system('cls' if os.name == 'nt' else 'clear')    #TODO Limpiamos la terminal mediante (clear), antes de ejecutar el main

sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'src'))

from roguelike_engine.log_config import init_logging, build_log_filepath
# Initialize logging: console and rotating file handler with standardized filename under logs/engine
init_logging(level="DEBUG", logfile=str(build_log_filepath("roguelike", directory="logs/engine")))

import logging
from roguelike_game.main import main

# DEV: auto-importar nuevos assets de buildings como plantillas si la bandera está activa
try:
    from roguelike_engine.config import config as _cfg
    if getattr(_cfg, 'DEV_AUTO_IMPORT_BUILDINGS', False):
        try:
            from roguelike_engine.buildings import auto_importer as _auto
            _auto.run(verbose=True)
        except Exception as _e:
            logging.warning(f"[AutoImporter] Error al auto-importar: {_e}")
except Exception as _e:
    logging.debug(f"[Launcher] Config no disponible para auto-import: {_e}")

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        logging.warning("SALIENDO MEDIANTE CTRL+C")
        sys.exit(0)