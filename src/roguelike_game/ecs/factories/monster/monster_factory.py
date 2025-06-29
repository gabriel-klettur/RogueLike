
# Path: src/roguelike_game/ecs/factories/monster/monster_factory.py
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.components.transform.position import Position

from roguelike_game.ecs.components.combat.health import Health

from roguelike_game.ecs.fsm.states.monster.patrol_state import PatrolState
from roguelike_game.ecs.components.fsm.patrol_route import PatrolRoute
from roguelike_game.ecs.components.core.identity import Identity, Faction
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.combat.melee_weapon import MeleeWeapon
from roguelike_game.ecs.components.ai.aggro_range import AggroRange
from roguelike_game.ecs.components.combat.melee_range import MeleeRange
from roguelike_game.ecs.fsm.fsm import FiniteStateMachine
from roguelike_game.ecs.components.fsm.npc_state import NPCState
from roguelike_game.ecs.components.ai.damage_config import DamageConfig
import logging

from roguelike_game.ecs.factories.monster.config import MONSTER_DEFS
from roguelike_game.ecs.factories.monster.cache import _load_caches_once
from roguelike_game.ecs.factories.monster.sprite_loader import create_sprite_component, create_patrol_components
from roguelike_game.ecs.factories.monster.physics import calculate_position, create_physics_components, create_collider_components, create_zlayer_component

# Initialize logger
logger = logging.getLogger(__name__)

def spawn_monster(world, monster_type: str, tile_x: int, tile_y: int) -> int:
    """Create a monster entity based on the provided monster type and tile coordinates."""
    _load_caches_once()
    cfg = MONSTER_DEFS[monster_type]
    eid = world.create_entity()

    # Sprite & Death Image
    sprite, death_img = create_sprite_component(monster_type)
    if death_img:
        sprite.death_image = death_img
    world.components["Sprite"][eid] = sprite

    # Record spawn tile for debugging
    if not hasattr(world, "spawn_tiles"):
        world.spawn_tiles = []
    world.spawn_tiles.append((tile_x, tile_y, eid))

    # Position
    px, py = calculate_position(tile_x, tile_y, cfg, sprite)
    world.components["Position"][eid] = Position(px, py)

    # Patrol, MovementSpeed, Animator
    patrol, movement, animator = create_patrol_components(px, py, monster_type, cfg)
    world.components["Patrol"][eid] = patrol
    world.components["MovementSpeed"][eid] = movement
    world.components["Animator"][eid] = animator

    # Physics: Scale & Velocity
    scale_cmp, velocity_cmp = create_physics_components(cfg)
    world.components["Scale"][eid] = scale_cmp
    world.components["Velocity"][eid] = velocity_cmp

    # Colliders
    collider_cmp = create_collider_components(sprite, cfg)
    world.components["MultiCollider"][eid] = collider_cmp

    # Z-Layer
    zlayer_cmp = create_zlayer_component(cfg)
    world.components["ZLayer"][eid] = zlayer_cmp

    # Health & Identity
    health_cmp, identity_cmp = Health(cfg["hp"], cfg["hp"]), Identity(id=eid, name=monster_type.capitalize(), title="", faction=getattr(Faction, cfg.get("faction")))
    world.components["Health"][eid] = health_cmp
    world.components["Identity"][eid] = identity_cmp

    # CombatStats, MeleeWeapon, AggroRange
    world.components["CombatStats"][eid] = CombatStats(cfg["hp"], cfg["hp"], cfg["power"], cfg["defense"])
    world.components["MeleeWeapon"][eid] = MeleeWeapon(cfg["melee_damage"], cfg["melee_cooldown"])
    world.components["AggroRange"][eid] = AggroRange(cfg["aggro_range"])
    # Añadir componente de melee_range para lógica de combate
    world.components["MeleeRange"][eid] = MeleeRange(cfg["melee_range"])
    # Configurar duración de daño desde monsters.json
    world.components["DamageConfig"][eid] = DamageConfig(cfg["damage_duration"])

    # FSM component    
    # Ruta de patrulla (ejemplo), se puede cargar del config
    route_points = [(px, py), (px + 5 * TILE_SIZE, py)]
    world.components["PatrolRoute"][eid] = PatrolRoute(route_points)
    fsm = FiniteStateMachine(PatrolState())
    world.components["NPCState"][eid] = NPCState(fsm, "PatrolState")

    return eid