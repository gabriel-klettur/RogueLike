from .magic_explosion import MagicExplosion
from .fire_explosion import FireExplosion
from .dark_explosion import DarkExplosion
from .electric_explosion import ElectricExplosion

EXPLOSION_TYPES = {
    "magic": MagicExplosion,
    "fire": FireExplosion,
    "dark": DarkExplosion,
    "electric": ElectricExplosion
}