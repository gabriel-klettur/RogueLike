import sys
import os

os.system('cls' if os.name == 'nt' else 'clear')    #TODO Limpiamos la terminal mediante (clear), antes de ejecutar el main

sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'src'))

from roguelike_engine.log_config import init_logging, build_log_filepath
# Initialize logging: console and rotating file handler with standardized filename under logs/engine
init_logging(level="DEBUG", logfile=str(build_log_filepath("roguelike", directory="logs/engine")))

import logging
from roguelike_game.main import main


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        logging.warning("SALIENDO MEDIANTE CTRL+C")
        sys.exit(0)