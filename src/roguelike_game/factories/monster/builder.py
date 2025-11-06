"""
Builder para crear la entidad monstruo usando coordenadas en píxeles.
"""
from roguelike_game.factories.monster.cache import _load_caches_once
from roguelike_game.factories.monster.config import MONSTER_DEFS, MONSTER_DEFAULTS
from roguelike_game.factories.monster.sprite_loader import create_sprite_component, create_movement_components
from roguelike_game.factories.monster.physics import create_physics_components, create_collider_components, create_zlayer_component
from roguelike_game.factories.monster.calibrator import calibrate_tile_position
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.factories.monster.behaviour_loader import build_patrol_route, build_patrol_points
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
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_editors.fsm.services.fsm_runtime_bridge import build_fsm_for_archetype, get_set, build_fsm_from_set
from roguelike_game.ecs.components.fsm.npc_state import NPCState
from roguelike_game.ecs.components.core.npc_tag import NPCTagComponent
from roguelike_game.ecs.components.monster_instance_component import MonsterInstanceComponent
from roguelike_game.ecs.components.chat.chat_component import ChatComponent
from roguelike_game.ecs.components.chat.vendor_component import VendorComponent
from roguelike_game.ecs.components.monster_archetype import MonsterArchetype
from roguelike_game.ecs.components.ai.auto_cast_component import AutoCastComponent
from roguelike_game.ecs.components.combat.mana import Mana


class MonsterBuilder:
    """Build pixel-based monster entity."""

    def __init__(self, world):
        self.world = world

    def build(self, x: int, y: int, monster_type: str, instance_id: str | None = None) -> int:
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

        # MovementSpeed, Animator (Patrol component removed)
        movement, animator = create_movement_components(x, y, monster_type, cfg)
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
        # Usar default_name si está disponible en el JSON; si no, usar el id de clase
        display_name = cfg.get("default_name") or monster_type
        world.components["Identity"][eid] = Identity(
            id=eid,
            name=str(display_name),
            title="",
            faction=getattr(Faction, cfg.get("faction"), None)
        )
        # Etiqueta NPC para gestión de inventario
        world.components["NPCTagComponent"][eid] = NPCTagComponent()
        # Identificador único de instancia para persistencia de inventario
        # Guardar tipo de arquetipo y respetar instance_id si se provee
        world.components["MonsterInstanceComponent"][eid] = MonsterInstanceComponent(instance_id=instance_id)
        world.components["MonsterArchetype"][eid] = MonsterArchetype(type=str(monster_type))

        # Chat & Vendor: si el JSON define chat_range (>0), añadimos ChatComponent.
        # Interpretamos chat_range como tiles y lo convertimos a píxeles (coordinado con Position/distancias en px).
        try:
            chat_range_tiles = float(cfg.get("chat_range", 0) or 0)
        except Exception:
            chat_range_tiles = 0.0
        if chat_range_tiles > 0:
            chat_range_px = float(chat_range_tiles) * float(TILE_SIZE)
            # Heurística para rol vendor: nombre que contenga 'vendor'
            lower_name = str(monster_type).lower()
            is_vendor = ("vendor" in lower_name)
            role = "vendor" if is_vendor else "generic"
            world.components["ChatComponent"][eid] = ChatComponent(
                chat_range=chat_range_px,
                role=role,
                greeting=None,
            )
            if is_vendor:
                # Precios por defecto definidos en VendorComponent: {"wood": 1} usando moneda "gold".
                world.components["VendorComponent"][eid] = VendorComponent()

        # Combat & CombatStats
        world.components["CombatStats"][eid] = CombatStats(current_hp=cfg["hp"], max_hp=cfg["hp"], power=cfg["power"], defense=cfg["defense"])
        world.components["MeleeWeapon"][eid] = MeleeWeapon(cfg["melee_damage"], cfg["melee_cooldown"])
        world.components["AggroRange"][eid] = AggroRange(cfg["aggro_range"])
        world.components["MeleeRange"][eid] = MeleeRange(cfg["melee_range"])
        # Configuración de daño: duración y probabilidad de detenerse al recibir daño
        stop_prob = float(cfg.get("damage_stop_probability", MONSTER_DEFAULTS.get("damage_stop_probability", 0.25)))
        world.components["DamageConfig"][eid] = DamageConfig(cfg["damage_duration"], stop_probability=stop_prob)

        # Mana (opcional). Si el JSON define 'mana' o 'max_mana', registrar componente.
        mana_val = cfg.get("mana")
        max_mana = cfg.get("max_mana")
        if mana_val is not None or max_mana is not None:
            max_m = int(max_mana if max_mana is not None else mana_val)
            cur_m = int(mana_val if mana_val is not None else max_m)
            world.components["Mana"][eid] = Mana(current_mana=cur_m, max_mana=max_m)

        # Auto-cast de hechizos: soporta modo legado (auto_cast) y lista (auto_cast_list)
        ac_added = False
        ac_cfg = cfg.get("auto_cast")
        ac_list = cfg.get("auto_cast_list")
        if isinstance(ac_list, list) and len(ac_list) > 0:
            try:
                entries = []
                for item in ac_list:
                    if not isinstance(item, dict):
                        continue
                    e = {
                        'spell': str(item.get('spell') or 'fireball'),
                    }
                    # Aceptar periodo fijo o rango [min,max]
                    if 'min_period_s' in item or 'max_period_s' in item:
                        if 'min_period_s' in item: e['min_period_s'] = float(item.get('min_period_s'))
                        if 'max_period_s' in item: e['max_period_s'] = float(item.get('max_period_s'))
                    elif 'period_s' in item:
                        e['period_s'] = float(item.get('period_s'))
                    # Canalizado y wire
                    if 'channel_s' in item: e['channel_s'] = float(item.get('channel_s'))
                    if 'wire_from' in item: e['wire_from'] = list(item.get('wire_from'))
                    if 'wire_to' in item: e['wire_to'] = list(item.get('wire_to'))
                    if 'target' in item: e['target'] = str(item.get('target'))
                    # Meta opcional
                    meta = {k: v for k, v in item.items() if k not in ('spell','period_s','min_period_s','max_period_s','channel_s','wire_from','wire_to','target')}
                    if meta: e['meta'] = meta
                    entries.append(e)
                if entries:
                    world.components.setdefault("AutoCastComponent", {})[eid] = AutoCastComponent(entries=entries)
                    ac_added = True
            except Exception:
                pass
        if not ac_added and isinstance(ac_cfg, dict):
            try:
                spell = str(ac_cfg.get("spell") or "fireball")
                period_s = float(ac_cfg.get("period_s", 2.0))
                # Copiar claves extra como metadatos (e.g., scale, scale_multiplier)
                meta = {k: v for k, v in ac_cfg.items() if k not in ("spell", "period_s")}
            except Exception:
                spell, period_s, meta = "fireball", 2.0, {}
            world.components.setdefault("AutoCastComponent", {})[eid] = AutoCastComponent(spell=spell, period_s=period_s, meta=meta)

        # FSM: PatrolRoute & NPCState
        patrol_cfg = cfg.get("patrol")
        # Distinguir entre 'patrol' ausente (usar comportamiento previo) y 'patrol': null explícito
        explicit_null_patrol = ("patrol" in cfg) and (patrol_cfg is None)
        route = None
        if not explicit_null_patrol:
            route = build_patrol_route(x, y, patrol_cfg, TILE_SIZE)
            world.components["PatrolRoute"][eid] = PatrolRoute(
                points=route.get("points", []),
                dwell_times=route.get("dwell_times"),
            )
        # Try per-class FSM via fsm_set in new_hostiles.json, then fallback to assignments.json, then Patrol
        fsm_set_id = cfg.get("fsm_set")
        if fsm_set_id:
            try:
                set_def = get_set(fsm_set_id)
                if set_def:
                    fsm, initial_name = build_fsm_from_set(set_def)
                    # Inject attack duration into FSM context from monster JSON (damage_duration)
                    attack_duration = cfg.get("damage_duration")
                    if attack_duration is not None:
                        fsm.context["attack_duration"] = float(attack_duration)
                    # Inject configurable wind-up delay before attacking
                    windup = cfg.get("attack_windup_s")
                    if windup is not None:
                        fsm.context["attack_windup_s"] = float(windup)
                    else:
                        # Default global wind-up for hostiles if not specified
                        fsm.context.setdefault("attack_windup_s", 1.0)
                    # Inject optional per-class telegraph flag
                    try:
                        tele = cfg.get("use_attack_telegraph")
                        if tele is not None:
                            fsm.context["use_attack_telegraph"] = bool(tele)
                    except Exception:
                        pass
                    world.components["NPCState"][eid] = NPCState(fsm, initial_name)
                    return eid
            except Exception:
                # Ignore and fallback to assignment-based build
                pass

        # Fallback: JSON-driven FSM by archetype assignment; if none, go Patrol
        built = None
        try:
            archetype = str(monster_type).lower()
            built = build_fsm_for_archetype(archetype, eid=eid)
        except Exception:
            built = None
        if built is not None:
            fsm, initial_name = built
            # Inject attack duration into FSM context from monster JSON (damage_duration)
            attack_duration = cfg.get("damage_duration")
            if attack_duration is not None:
                fsm.context["attack_duration"] = float(attack_duration)
            # Inject configurable wind-up delay before attacking
            windup = cfg.get("attack_windup_s")
            if windup is not None:
                fsm.context["attack_windup_s"] = float(windup)
            else:
                fsm.context.setdefault("attack_windup_s", 1.0)
            # Inject optional per-class telegraph flag
            try:
                tele = cfg.get("use_attack_telegraph")
                if tele is not None:
                    fsm.context["use_attack_telegraph"] = bool(tele)
            except Exception:
                pass
            world.components["NPCState"][eid] = NPCState(fsm, initial_name)
        else:
            if explicit_null_patrol:
                # Si el JSON especifica "patrol": null, arrancar en Idle
                fsm = FiniteStateMachine(IdleState())
                world.components["NPCState"][eid] = NPCState(fsm, "IdleState")
            else:
                # Comportamiento previo: patrulla por defecto
                fsm = FiniteStateMachine(PatrolState())
                world.components["NPCState"][eid] = NPCState(fsm, "PatrolState")
            # Also make attack_duration available even in Idle/Patrol fallback
            attack_duration = cfg.get("damage_duration")
            if attack_duration is not None:
                fsm.context["attack_duration"] = float(attack_duration)
            # Provide wind-up default or per-class override in fallback path as well
            windup = cfg.get("attack_windup_s")
            if windup is not None:
                fsm.context["attack_windup_s"] = float(windup)
            else:
                fsm.context.setdefault("attack_windup_s", 1.0)
            # Inject optional per-class telegraph flag
            try:
                tele = cfg.get("use_attack_telegraph")
                if tele is not None:
                    fsm.context["use_attack_telegraph"] = bool(tele)
            except Exception:
                pass

        return eid
