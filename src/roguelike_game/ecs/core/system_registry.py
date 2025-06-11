# Path: src/roguelike_game/ecs/core/system_registry.py

import roguelike_engine.config.config as config

# Importamos cada clase de sistema
from roguelike_game.ecs.systems.physics.movement_collision_system import MovementCollisionSystem
from roguelike_game.ecs.systems.rendering.animation_system import AnimationSystem
from roguelike_game.ecs.systems.rendering.health_bar_system import HealthBarSystem
from roguelike_game.ecs.systems.rendering.nameplate_system import NamePlateSystem
from roguelike_game.ecs.systems.physics.collision_debug_system import CollisionDebugSystem
from roguelike_game.ecs.systems.combat.melee.melee_combat_system import MeleeCombatSystem
from roguelike_game.ecs.systems.physics.facing_system import FacingSystem
from roguelike_game.ecs.systems.physics.player_facing_system import PlayerFacingSystem
from roguelike_game.ecs.systems.core.spawn_debug_system import SpawnDebugSystem
from roguelike_game.ecs.systems.core.spawn_system import SpawnSystem
from roguelike_game.ecs.systems.input.input_system import InputSystem
from roguelike_game.ecs.systems.combat.spells.spell_casting_system import SpellCastingSystem
from roguelike_game.ecs.systems.combat.spells.fireball_system import FireballSystem
from roguelike_game.ecs.systems.combat.spells.aura_system import AuraSystem
from roguelike_game.ecs.systems.particles.healing_aura_emitter_system import HealingAuraEmitterSystem
from roguelike_game.ecs.systems.particles.particle_system import ParticleSystem
from roguelike_game.ecs.systems.particles.laser_beam_emitter_system import LaserBeamEmitterSystem
from roguelike_game.ecs.systems.rendering.combat.spells.fireball_render_system import FireballRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.lightning_render_system import LightningRenderSystem
from roguelike_game.ecs.systems.rendering.particles.particle_render_system import ParticleRenderSystem
from roguelike_game.ecs.systems.rendering.player_debug_render_system import PlayerDebugRenderSystem
from roguelike_game.ecs.systems.rendering.death_timer_bar_system import DeathTimerBarSystem
from roguelike_game.ecs.systems.rendering.death_timer_debug_system import DeathTimerDebugSystem
from roguelike_game.ecs.systems.rendering.fsm.chase_debug_system import ChaseDebugSystem
from roguelike_game.ecs.systems.rendering.fsm.states_debug_render_system import StatesDebugRenderSystem
from roguelike_game.ecs.systems.rendering.hitbox_debug_system import HitboxDebugSystem
from roguelike_game.ecs.systems.rendering.flash_system import FlashSystem
from roguelike_game.ecs.systems.rendering.trail_system import TrailSystem
from roguelike_game.ecs.systems.fsm.fsm_system import FSMSystem
from roguelike_game.ecs.systems.combat.spells.dash_system import DashSystem
from roguelike_game.ecs.systems.combat.hitbox_system import HitboxSystem
from roguelike_game.ecs.systems.combat.spells.lightning_system import LightningSystem

def get_update_system_classes():
    """
    Devuelve la lista de clases de sistemas que se ejecutan en la fase de actualización.
    """
    return [
        FSMSystem,
        PlayerFacingSystem, FacingSystem, InputSystem,
        MovementCollisionSystem,
        MeleeCombatSystem, SpellCastingSystem, AuraSystem, ParticleSystem, LaserBeamEmitterSystem, HealingAuraEmitterSystem, FireballSystem, LightningSystem, DashSystem, HitboxSystem,
        TrailSystem,
        AnimationSystem, FlashSystem, SpawnSystem,
    ]

def get_render_system_classes():
    """
    Devuelve la lista de clases de sistemas que se ejecutan en la fase de renderizado.
    Se añade dinámicamente SpawnDebug y DeathTimerDebug si estamos en DEBUG.
    """
    base = [
        HealthBarSystem, NamePlateSystem,
        CollisionDebugSystem,
        FireballRenderSystem, ParticleRenderSystem, LightningRenderSystem,
        ChaseDebugSystem,
        PlayerDebugRenderSystem,
        DeathTimerBarSystem,
        StatesDebugRenderSystem,
    ]
    if config.DEBUG:
        base.append(SpawnDebugSystem)
        base.append(DeathTimerDebugSystem)
    # Always register HitboxDebugSystem; update() will early exit if DEBUG_HITBOX is False
    base.append(HitboxDebugSystem)
    return base
