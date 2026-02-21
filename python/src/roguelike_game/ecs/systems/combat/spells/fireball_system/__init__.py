from roguelike_game.config.spells_config import SPELLS  # default mapping, monkeypatch-friendly
from .fireball_system import FireballSystem
from .collisions.walls import WallCacheEntry

__all__ = ["FireballSystem", "WallCacheEntry", "SPELLS"]
