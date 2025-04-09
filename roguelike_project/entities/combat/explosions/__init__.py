from .magic import MagicExplosion
from .fire import FireExplosion
from .dark import DarkExplosion
from .electric import ElectricExplosion

EXPLOSION_TYPES = {
    "magic": MagicExplosion,
    "fire": FireExplosion,
    "dark": DarkExplosion,
    "electric": ElectricExplosion
}