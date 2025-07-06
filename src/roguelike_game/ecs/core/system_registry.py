
# Path: src/roguelike_game/ecs/core/system_registry.py
import roguelike_engine.config.config as config

# Importamos cada clase de sistema
from roguelike_game.ecs.systems.physics.movement_collision_system import MovementCollisionSystem
from roguelike_game.ecs.systems.rendering.animation_system import AnimationSystem
from roguelike_game.ecs.systems.rendering.health_bar_system import HealthBarSystem
from roguelike_game.ecs.systems.rendering.nameplate_system import NamePlateSystem
from roguelike_game.ecs.systems.combat.melee.melee_combat_system import MeleeCombatSystem
from roguelike_game.ecs.systems.physics.facing_system import FacingSystem
from roguelike_game.ecs.systems.physics.player_facing_system import PlayerFacingSystem
from roguelike_game.ecs.systems.core.spawn_system import SpawnSystem
from roguelike_game.ecs.systems.input.input_system import InputSystem
from roguelike_game.ecs.systems.combat.spells.spell_casting_system import SpellCastingSystem
from roguelike_game.ecs.systems.combat.spells.fireball_system import FireballSystem
from roguelike_game.ecs.systems.combat.spells.arcane_flame_system import ArcaneFlameSystem
from roguelike_game.ecs.systems.combat.spells.firework_launch_system import FireworkLaunchSystem
from roguelike_game.ecs.systems.combat.spells.aura_system import AuraSystem
from roguelike_game.ecs.systems.particles.healing_aura_emitter_system import HealingAuraEmitterSystem
from roguelike_game.ecs.systems.particles.particle_system import ParticleSystem
from roguelike_game.ecs.systems.rendering.particles.particle_render_system import ParticleRenderSystem
from roguelike_game.ecs.systems.particles.laser_beam_emitter_system import LaserBeamEmitterSystem
# from roguelike_game.ecs.systems.particles.arcane_flame_emitter_system import ArcaneFlameEmitterSystem
from roguelike_game.ecs.systems.particles.slash_emitter_system import SlashEmitterSystem
from roguelike_game.ecs.systems.particles.dash_emitter_system import DashEmitterSystem
from roguelike_game.ecs.systems.particles.lightning_emitter_system import LightningEmitterSystem
from roguelike_game.ecs.systems.rendering.combat.spells.fireball_render_system import FireballRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.lightning_render_system import LightningRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.arcane_flame_render_system import ArcaneFlameRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.firework_launch_render_system import FireworkLaunchRenderSystem
from roguelike_game.ecs.systems.combat.spells.smoke_system import SmokeSystem
from roguelike_game.ecs.systems.combat.spells.smoke_emitter_system import SmokeEmitterSystem
from roguelike_game.ecs.systems.combat.spells.teleport_system import TeleportSystem
from roguelike_game.ecs.systems.combat.explosion_system import ExplosionSystem
from roguelike_game.ecs.systems.combat.spells.sphere_magic_shield_system import SphereMagicShieldSystem
from roguelike_game.ecs.systems.rendering.combat.spells.smoke_render_system import SmokeRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.smoke_emitter_render_system import SmokeEmitterRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.teleport_render_system import TeleportRenderSystem
from roguelike_game.ecs.systems.rendering.combat.explosions.explosion_render_system import ExplosionRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.sphere_magic_shield_render_system import SphereMagicShieldRenderSystem

from roguelike_game.ecs.systems.rendering.particles.particle_render_system import ParticleRenderSystem
from roguelike_game.ecs.systems.rendering.death_timer_bar_system import DeathTimerBarSystem
from roguelike_game.ecs.systems.rendering.flash_system import FlashSystem
from roguelike_game.ecs.systems.rendering.trail_system import TrailSystem
from roguelike_game.ecs.systems.fsm.fsm_system import FSMSystem
from roguelike_game.ecs.systems.combat.spells.dash_system import DashSystem
from roguelike_game.ecs.systems.combat.hitbox_system import HitboxSystem
from roguelike_game.ecs.systems.combat.spells.lightning_system import LightningSystem
from roguelike_game.ecs.systems.debug.entities_debug_system import EntitiesDebugSystem
from roguelike_game.ecs.systems.expansion_system import ExpansionSystem
from roguelike_game.ecs.systems.inventory.map_load_drops_system import MapLoadDropsSystem
# from roguelike_game.ecs.systems.inventory.drop_render_system import DropRenderSystem  # drops handled by RenderSystem
from roguelike_game.ecs.systems.rendering.grayscale_render_system import GrayscaleRenderSystem
from roguelike_game.ecs.systems.rendering.resurrection_area_system import ResurrectionAreaSystem

def get_update_system_classes():
    """
    Devuelve la lista de clases de sistemas que se ejecutan en la fase de actualización.
    """
    return [
        FSMSystem,
        PlayerFacingSystem, FacingSystem, InputSystem,
        MovementCollisionSystem,
        MeleeCombatSystem, SpellCastingSystem, ArcaneFlameSystem, SmokeSystem, SmokeEmitterSystem, SphereMagicShieldSystem, TeleportSystem, FireworkLaunchSystem, AuraSystem, ParticleSystem, ExplosionSystem, LaserBeamEmitterSystem, HealingAuraEmitterSystem, SlashEmitterSystem, DashEmitterSystem, LightningEmitterSystem, FireballSystem, LightningSystem, DashSystem, HitboxSystem,
        TrailSystem,
        AnimationSystem, FlashSystem, SpawnSystem, MapLoadDropsSystem, ExpansionSystem,
    ]

def get_render_system_classes():
    """
    Devuelve la lista de clases de sistemas que se ejecutan en la fase de renderizado.
    Se añade dinámicamente SpawnDebug y DeathTimerDebug si estamos en DEBUG.
    """
    base = [
        HealthBarSystem, NamePlateSystem,
        FireballRenderSystem, ArcaneFlameRenderSystem, FireworkLaunchRenderSystem, SmokeRenderSystem, SmokeEmitterRenderSystem, SphereMagicShieldRenderSystem, TeleportRenderSystem, ParticleRenderSystem, LightningRenderSystem,
        DeathTimerBarSystem,
        # DropRenderSystem removed: drops rendered via RenderSystem
    ]
    # Render systems comunes
    # Overlay unificado de debug de entidades (se activa/desactiva internamente con F12)
    base.append(EntitiesDebugSystem)
    base.append(GrayscaleRenderSystem)
    base.append(ResurrectionAreaSystem)
    # Otros sistemas de render (eliminados FlashSystem y TrailSystem de render)
    # FlashSystem y TrailSystem son sistemas de update, no deben ir en render
    return base