import time
import pygame
import importlib
from roguelike_game.factories.player.loader import (
    load_and_scale_sprites, extract_initial_frame, build_animator_map, build_masks_map
)
import roguelike_game.factories.player.config as player_cfg
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
from roguelike_game.ecs.components.rendering.trail_component import (
    TrailComponent, TrailConfig
)
from roguelike_game.factories.player.collider import create_body_and_feet
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
from roguelike_game.ecs.components.ai.damage_config import DamageConfig


class PlayerManager:
    """Manage runtime player operations such as class change."""
    def __init__(self, ecs_world):
        self.ecs_world = ecs_world

    def change_class(self, new_class: str):
        """Update player's class: reload assets and stats."""
        # Reload player config so classes/stats reflect latest JSON after editor renames
        importlib.reload(player_cfg)
        ecs_world = self.ecs_world
        eid = ecs_world.player_entity
        comps = ecs_world.components
        # Update tag
        comps["PlayerTagComponent"][eid] = PlayerTagComponent(new_class)
        # Reload sprites and animations
        sprites = load_and_scale_sprites(new_class)
        frame = extract_initial_frame(sprites)
        # Asignar sprite: frame válido o placeholder
        if frame and isinstance(frame, pygame.Surface):
            img = frame
        else:
                # Transparent placeholder (invisible)
                size = player_cfg.ORIGINAL_SPRITE_SIZE
                placeholder = pygame.Surface(size, pygame.SRCALPHA)
                img = placeholder
        comps["Sprite"][eid] = Sprite(img)
        comps["Animator"][eid] = Animator(
            animations=build_animator_map(sprites),
            current_state=player_cfg.INITIAL_ANIMATION_STATE,
            masks=build_masks_map(sprites),
        )
        comps["AnimationTimer"][eid] = AnimationTimer(
            last_time=time.time(),
            interval=player_cfg.ANIMATION_INTERVAL
        )
        # Update movement speed and reset velocity
        comps["MovementSpeed"][eid] = MovementSpeed(
            player_cfg.PLAYER_STATS[new_class]["basic_speed"]
        )
        comps["Velocity"][eid] = Velocity(0, 0)
        # Update collider
        # Crear collider basado en sprite (frame o placeholder)
        comps["MultiCollider"][eid] = create_body_and_feet(img)
        # Update stats: health, combat, mana, energy, hunger
        stats = player_cfg.PLAYER_STATS[new_class]
        max_hp = stats["max_strength"]
        comps["Health"][eid] = Health(max_hp, max_hp)
        # Update damage config: duration and stop_probability based on class stats (fallback to defaults)
        dmg_duration = stats.get("damage_duration", player_cfg.DEFAULT_DAMAGE_DURATION)
        stop_prob = float(stats.get("damage_stop_probability", getattr(player_cfg, "DEFAULT_DAMAGE_STOP_PROBABILITY", 0.25)))
        comps["DamageConfig"][eid] = DamageConfig(float(dmg_duration), stop_probability=stop_prob)
        comps["CombatStats"][eid] = CombatStats(
            current_hp=max_hp,
            max_hp=max_hp,
            power=stats["basic_attack"],
            defense=stats["basic_armor"]
        )
        max_mana = stats["max_intelligence"]
        comps["Mana"][eid] = Mana(current_mana=max_mana, max_mana=max_mana)
        max_energy = stats["max_dexterity"]
        comps["Energy"][eid] = Energy(
            current_energy=max_energy,
            max_energy=max_energy
        )
        max_hunger = stats.get("max_hunger", 100)
        comps["Hunger"][eid] = Hunger(
            current_hunger=max_hunger,
            max_hunger=max_hunger
        )
        # Update melee weapon and trail
        comps["MeleeWeapon"][eid] = MeleeWeapon(
            damage=player_cfg.MELEE_WEAPON_CFG["damage"],
            cooldown=player_cfg.MELEE_WEAPON_CFG["cooldown"]
        )
        trail_params = stats.get("basic_trail", player_cfg.DEFAULT_TRAIL)
        trail_cfg = TrailConfig(
            interval=trail_params["interval"],
            life_time=trail_params["life_time"],
            max_trails=trail_params["max_trails"]
        )
        comps["TrailComponent"][eid] = TrailComponent(config=trail_cfg)
        # Refresh FSM context (attack_duration) if NPCState is present
        try:
            npc_state = comps["NPCState"].get(eid)
        except Exception:
            npc_state = None
        if npc_state and hasattr(npc_state, "fsm") and hasattr(npc_state.fsm, "context"):
            attack_duration = stats.get("attack_duration")
            if attack_duration is None:
                attack_duration = player_cfg.MELEE_WEAPON_CFG.get("cooldown")
            if attack_duration is not None:
                npc_state.fsm.context["attack_duration"] = float(attack_duration)
