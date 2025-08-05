import sys
import os



sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'src'))

from roguelike_engine.log_config import init_logging
# Initialize logging: console and rotating file handler
init_logging(level="DEBUG", logfile="logs/roguelike.log")

from roguelike_game.main import main

if __name__ == "__main__":
    main()