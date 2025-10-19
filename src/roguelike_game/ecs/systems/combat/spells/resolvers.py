import logging
logger = logging.getLogger(__name__)
try:
    logger.setLevel(logging.INFO)
except Exception:
    pass

from .resolvers_pkg.base import BaseSpellResolver
from .resolvers_pkg.projectile import ProjectileResolver
from .resolvers_pkg.aura import AuraResolver
from .resolvers_pkg.beam import BeamResolver
from .resolvers_pkg.dash import DashResolver
from .resolvers_pkg.slash import SlashResolver
from .resolvers_pkg.lightning import LightningResolver
from .resolvers_pkg.arcane_flame import ArcaneFlameResolver
from .resolvers_pkg.firework_launch import FireworkLaunchResolver
from .resolvers_pkg.smoke import SmokeResolver
from .resolvers_pkg.teleport import TeleportResolver
from .resolvers_pkg.smoke_emitter import SmokeEmitterResolver
from .resolvers_pkg.sphere_magic_shield import SphereMagicShieldResolver
from .resolvers_pkg.registry import default_resolvers, SPELL_RESOLVERS

__all__ = [
    'BaseSpellResolver',
    'ProjectileResolver',
    'AuraResolver',
    'BeamResolver',
    'DashResolver',
    'SlashResolver',
    'LightningResolver',
    'ArcaneFlameResolver',
    'FireworkLaunchResolver',
    'SmokeResolver',
    'TeleportResolver',
    'SmokeEmitterResolver',
    'SphereMagicShieldResolver',
    'default_resolvers',
    'SPELL_RESOLVERS',
]