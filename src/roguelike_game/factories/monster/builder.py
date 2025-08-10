"""
Builder para crear la entidad monstruo usando coordenadas en píxeles.
"""
from roguelike_game.factories.monster.cache import _load_caches_once
from roguelike_game.factories.monster.config import MONSTER_DEFS
from roguelike_game.factories.monster.sprite_loader import create_sprite_component, create_patrol_components
from roguelike_game.factories.monster.physics import create_physics_components, create_collider_components, create_zlayer_component
from roguelike_game.factories.monster.calibrator import calibrate_tile_position
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.core.identity import Identity, Faction
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.combat.melee_weapon import MeleeWeapon
from roguelike_game.ecs.components.ai.aggro_range import AggroRange
from roguelike_game.ecs.components.combat.melee_range import MeleeRange
from roguelike_game.ecs.components.ai.damage_config import DamageConfig
from roguelike_game.ecs.components.fsm.patrol_route import PatrolRoute
from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState
from roguelike_game.ecs.systems.fsm.fsm import FiniteStateMachine
from roguelike_game.ecs.components.fsm.npc_state import NPCState
from roguelike_game.ecs.components.core.npc_tag import NPCTagComponent
from roguelike_game.ecs.components.monster_instance_component import MonsterInstanceComponent


class MonsterBuilder:
    """Build pixel-based monster entity."""

    def __init__(self, world):
        self.world = world

    def build(self, x: int, y: int, monster_type: str) -> int:
        world = self.world

        _load_caches_once()
        cfg = MONSTER_DEFS[monster_type]
        eid = world.create_entity()

        # Sprite & DeathImage
        sprite, death_img = create_sprite_component(monster_type)
        if death_img:
            sprite.death_image = death_img
        world.components["Sprite"][eid] = sprite

        # Debug spawn tiles
        if not hasattr(world, "spawn_tiles"):
            world.spawn_tiles = []
        world.spawn_tiles.append((x, y, eid))

        # Position
        world.components["Position"][eid] = Position(x, y)

        # Patrol, MovementSpeed, Animator
        patrol, movement, animator = create_patrol_components(x, y, monster_type, cfg)
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
        world.components["Health"][eid] = Health(cfg["hp"], cfg["hp"])
        world.components["Identity"][eid] = Identity(id=eid, name=monster_type.capitalize(), title="", faction=getattr(Faction, cfg.get("faction"), None))
        # Etiqueta NPC para gestión de inventario
        world.components["NPCTagComponent"][eid] = NPCTagComponent()
        # Identificador único de instancia para persistencia de inventario
        world.components["MonsterInstanceComponent"][eid] = MonsterInstanceComponent()

        # Combat & CombatStats
        world.components["CombatStats"][eid] = CombatStats(current_hp=cfg["hp"], max_hp=cfg["hp"], power=cfg["power"], defense=cfg["defense"])
        world.components["MeleeWeapon"][eid] = MeleeWeapon(cfg["melee_damage"], cfg["melee_cooldown"])
        world.components["AggroRange"][eid] = AggroRange(cfg["aggro_range"])
        world.components["MeleeRange"][eid] = MeleeRange(cfg["melee_range"])
        world.components["DamageConfig"][eid] = DamageConfig(cfg["damage_duration"])

        # FSM: PatrolRoute & NPCState
        route_points = [(x, y), (x + 5 * TILE_SIZE, y)]
        world.components["PatrolRoute"][eid] = PatrolRoute(route_points)
        fsm = FiniteStateMachine(PatrolState())
        world.components["NPCState"][eid] = NPCState(fsm, "PatrolState")

        return eid
