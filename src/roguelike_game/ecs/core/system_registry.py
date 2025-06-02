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
from roguelike_game.ecs.systems.rendering.combat.spells.fireball_render_system import FireballRenderSystem
from roguelike_game.ecs.systems.rendering.player_debug_render_system import PlayerDebugRenderSystem
from roguelike_game.ecs.systems.rendering.death_timer_bar_system import DeathTimerBarSystem
from roguelike_game.ecs.systems.rendering.death_timer_debug_system import DeathTimerDebugSystem
from roguelike_game.ecs.systems.rendering.fsm.chase_debug_system import ChaseDebugSystem
from roguelike_game.ecs.systems.rendering.fsm.states_debug_render_system import StatesDebugRenderSystem
from roguelike_game.ecs.systems.rendering.flash_system import FlashSystem
from roguelike_game.ecs.systems.rendering.trail_system import TrailSystem
from roguelike_game.ecs.systems.fsm.fsm_system import FSMSystem

def get_update_system_classes():
    """
    Devuelve la lista de clases de sistemas que se ejecutan en la fase de actualización.
    """
    return [
        FSMSystem,
        PlayerFacingSystem, FacingSystem, InputSystem,
        MovementCollisionSystem,
        MeleeCombatSystem, SpellCastingSystem, FireballSystem,
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
        FireballRenderSystem,
        ChaseDebugSystem,
        PlayerDebugRenderSystem,
        DeathTimerBarSystem,
        StatesDebugRenderSystem,
    ]
    if config.DEBUG:
        base.append(SpawnDebugSystem)
        base.append(DeathTimerDebugSystem)
    return base
