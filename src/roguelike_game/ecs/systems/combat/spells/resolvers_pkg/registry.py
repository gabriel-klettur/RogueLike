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
}

SPELL_RESOLVERS = default_resolvers
