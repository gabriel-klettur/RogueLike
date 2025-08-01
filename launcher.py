import logging
import sys, os
logging.basicConfig(level=logging.DEBUG, format='%(message)s')
sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'src'))

from roguelike_game.main import main

if __name__ == "__main__":
    main()