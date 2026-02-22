from .projectile import ProjectileResolver
from .aura import AuraResolver
from .beam import BeamResolver
from .dash import DashResolver
from .slash import SlashResolver
from .lightning import LightningResolver
from .arcane_flame import ArcaneFlameResolver
from .firework_launch import FireworkLaunchResolver
from .smoke import SmokeResolver
from .teleport import TeleportResolver
from .smoke_emitter import SmokeEmitterResolver
from .sphere_magic_shield import SphereMagicShieldResolver
from .puddle import PuddleResolver
from .mine import MineResolver
from .boomerang import BoomerangResolver
from .chain_lightning import ChainLightningResolver
from .vortex_field import VortexFieldResolver
from .cone_breath import ConeBreathResolver
from .meteor_shower import MeteorShowerResolver
from .summon import SummonResolver
from .totem import TotemResolver
from .wall import WallResolver


default_resolvers = {
    'teleport': TeleportResolver(),
    'sphere_magic_shield': SphereMagicShieldResolver(),
    'projectile': ProjectileResolver(),
    'aura': AuraResolver(),
    'beam': BeamResolver(),
    'dash': DashResolver(),
    'slash': SlashResolver(),
    'lightning': LightningResolver(),
    'arcane_flame': ArcaneFlameResolver(),
    'firework_launch': FireworkLaunchResolver(),
    'smoke': SmokeResolver(),
    'smoke_emitter': SmokeEmitterResolver(),
    'puddle': PuddleResolver(),
    'mine': MineResolver(),
    'boomerang': BoomerangResolver(),
    'chain_lightning': ChainLightningResolver(),
    'vortex_field': VortexFieldResolver(),
    'cone_breath': ConeBreathResolver(),
    'meteor_shower': MeteorShowerResolver(),
    'summon': SummonResolver(),
    'totem': TotemResolver(),
    'wall': WallResolver(),
}

SPELL_RESOLVERS = default_resolvers
