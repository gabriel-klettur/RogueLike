import sys
import os

os.system('cls' if os.name == 'nt' else 'clear')    #TODO Limpiamos la terminal mediante (clear), antes de ejecutar el main

sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'src'))

from roguelike_engine.log_config import init_logging
# Initialize logging: console and rotating file handler
init_logging(level="INFO", logfile="logs/roguelike.log")

import logging
from roguelike_game.main import main

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        logging.warning("SALIENDO MEDIANTE CTRL+C")
        sys.exit(0)