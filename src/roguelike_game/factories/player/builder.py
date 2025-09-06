"""
Builder para crear la entidad jugador usando coordenadas en píxeles.
"""
import time
import pygame
from roguelike_game.factories.player.loader import load_and_scale_sprites, extract_initial_frame, build_animator_map, build_masks_map
from roguelike_game.factories.player.config import DEFAULT_CLASS, ANIMATION_INTERVAL, INITIAL_ANIMATION_STATE, PLAYER_STATS, MELEE_WEAPON_CFG, DEFAULT_TRAIL, DEFAULT_DAMAGE_DURATION
from roguelike_game.factories.player.collider import create_body_and_feet
from roguelike_engine.config.config_z_layer import Z_LAYERS
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
from roguelike_game.ecs.components.core.camera_follow import CameraFollowComponent
from roguelike_game.ecs.components.input_component import InputComponent
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.ecs.components.rendering.animation_timer import AnimationTimer
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.combat.mana import Mana
from roguelike_game.ecs.components.combat.energy import Energy
from roguelike_game.ecs.components.combat.hunger import Hunger
from roguelike_game.ecs.components.combat.melee_weapon import MeleeWeapon
from roguelike_game.ecs.components.rendering.trail_component import TrailComponent, TrailConfig
from roguelike_game.ecs.components.ai.damage_config import DamageConfig
from roguelike_game.ecs.components.fsm.npc_state import NPCState
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_game.ecs.systems.fsm.fsm import FiniteStateMachine
from roguelike_editors.fsm.services.fsm_runtime_bridge import build_fsm_for_archetype
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.abilities.dash_meter_component import DashMeterComponent


class PlayerBuilder:
    """Build pixel-based player entity."""

    def __init__(self, world):
        self.world = world

    def build(self, x: int, y: int, class_player: str) -> int:
        world = self.world
        eid = world.create_entity()
        comps = world.components

        # Posición
        comps["Position"][eid] = Position(x, y)
        # Etiquetas y cámara
        comps["PlayerTagComponent"][eid] = PlayerTagComponent(class_player)
        comps["CameraFollowComponent"][eid] = CameraFollowComponent()
        # Input
        comps["InputComponent"][eid] = InputComponent()
        # Capa Z
        comps["ZLayer"][eid] = ZLayer(Z_LAYERS["player"])
        # Sprites y animaciones
        sprites = load_and_scale_sprites(class_player)
        frame = extract_initial_frame(sprites)
        # Solo añadir Sprite si es una Surface de pygame
        if frame and isinstance(frame, pygame.Surface):
            comps["Sprite"][eid] = Sprite(frame)
        comps["Animator"][eid] = Animator(
            animations=build_animator_map(sprites),
            current_state=INITIAL_ANIMATION_STATE,
            masks=build_masks_map(sprites),
        )
        comps["AnimationTimer"][eid] = AnimationTimer(last_time=time.time(), interval=ANIMATION_INTERVAL)
        # Movimiento
        speed_value = PLAYER_STATS[class_player]["basic_speed"]
        comps["MovementSpeed"][eid] = MovementSpeed(speed_value)
        comps["Velocity"][eid] = Velocity(0, 0)
        # Colisiones
        # Solo crear colisión si el frame es un Surface de pygame
        if frame and isinstance(frame, pygame.Surface):
            comps["MultiCollider"][eid] = create_body_and_feet(frame)
        # Salud y combate
        max_hp = PLAYER_STATS[class_player]["max_strength"]
        comps["Health"][eid] = Health(max_hp, max_hp)
        # Daño configurable (duración del estado Damage) desde JSON
        dmg_duration = PLAYER_STATS[class_player].get("damage_duration", DEFAULT_DAMAGE_DURATION)
        comps["DamageConfig"][eid] = DamageConfig(float(dmg_duration))
        comps["CombatStats"][eid] = CombatStats(current_hp=max_hp, max_hp=max_hp,
                                                power=PLAYER_STATS[class_player]["basic_attack"],
                                                defense=PLAYER_STATS[class_player]["basic_armor"])
        # Inicializar maná, energía y hambre
        max_mana = PLAYER_STATS[class_player]["max_intelligence"]
        comps["Mana"][eid] = Mana(current_mana=max_mana, max_mana=max_mana)
        max_energy = PLAYER_STATS[class_player]["max_dexterity"]
        comps["Energy"][eid] = Energy(current_energy=max_energy, max_energy=max_energy)
        # Hambre predeterminada
        max_hunger = PLAYER_STATS[class_player].get("max_hunger", 100)
        comps["Hunger"][eid] = Hunger(current_hunger=max_hunger, max_hunger=max_hunger)
        # Arma cuerpo a cuerpo
        comps["MeleeWeapon"][eid] = MeleeWeapon(damage=MELEE_WEAPON_CFG["damage"], cooldown=MELEE_WEAPON_CFG["cooldown"])
        # Trail visual
        trail_params = PLAYER_STATS[class_player].get("basic_trail", DEFAULT_TRAIL)
        trail_cfg = TrailConfig(interval=trail_params["interval"], life_time=trail_params["life_time"], max_trails=trail_params["max_trails"])
        comps["TrailComponent"][eid] = TrailComponent(config=trail_cfg)
        # Dash charges (sequential policy): total y recarga por carga
        try:
            dash_total = int(PLAYER_STATS[class_player].get("dash_charges", 1))
        except Exception:
            dash_total = 1
        # Fallback de recarga: stats.dash_recharge_s o cooldown del spell 'dash' desde spells.json
        dash_recharge_s = PLAYER_STATS[class_player].get("dash_recharge_s")
        if dash_recharge_s is None:
            try:
                dash_recharge_s = float(SPELLS.get('dash', {}).get('cooldown_duration', 1.0))
            except Exception:
                dash_recharge_s = 1.0
        comps.setdefault("DashMeterComponent", {})[eid] = DashMeterComponent(
            total=max(1, dash_total),
            current=max(1, dash_total),
            recharge_s=float(dash_recharge_s),
            policy='sequential'
        )
        # FSM (JSON-driven with fallback)
        built = None
        try:
            built = build_fsm_for_archetype('player', eid=eid)
        except Exception:
            built = None
        if built is not None:
            fsm, initial_name = built
            # Inject attack duration from player JSON into FSM context
            attack_duration = PLAYER_STATS[class_player].get("attack_duration")
            if attack_duration is None:
                # Fallback to global weapon cooldown from JSON if class-specific duration is absent
                attack_duration = MELEE_WEAPON_CFG.get("cooldown")
            if attack_duration is not None:
                fsm.context["attack_duration"] = float(attack_duration)
            comps["NPCState"][eid] = NPCState(fsm, initial_name)
        else:
            fsm = FiniteStateMachine(IdleState())
            comps["NPCState"][eid] = NPCState(fsm, "IdleState")
            # Ensure attack_duration exists in context even with fallback FSM
            attack_duration = PLAYER_STATS[class_player].get("attack_duration")
            if attack_duration is None:
                attack_duration = MELEE_WEAPON_CFG.get("cooldown")
            if attack_duration is not None:
                fsm.context["attack_duration"] = float(attack_duration)

        return eid
