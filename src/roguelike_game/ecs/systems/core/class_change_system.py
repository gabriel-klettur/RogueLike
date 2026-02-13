"""
Module: class_change_system.py
ECS system that processes ClassChangeRequest components on the player entity.
Replaces the procedural PlayerManager.change_class() logic.
"""
import time
import importlib
import logging

import pygame

logger = logging.getLogger(__name__)


class ClassChangeSystem:
    """
    Consumes ClassChangeRequest components and applies the class change
    (sprites, stats, colliders, FSM) entirely through ECS components.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, camera=None):
        requests = world.components.get('ClassChangeRequest', {})
        if not requests:
            return

        # Process all pending requests (typically only one — the player)
        for eid in list(requests):
            req = requests.pop(eid, None)
            if req is None:
                continue
            try:
                self._apply_class_change(world, eid, req.new_class)
            except Exception:
                logger.exception("[ClassChangeSystem] Failed to apply class change for eid=%s", eid)

    # ------------------------------------------------------------------
    @staticmethod
    def _apply_class_change(world, eid: int, new_class: str) -> None:
        import roguelike_game.factories.player.config as player_cfg
        importlib.reload(player_cfg)

        from roguelike_game.factories.player.loader import (
            load_and_scale_sprites, extract_initial_frame,
            build_animator_map, build_masks_map,
        )
        from roguelike_game.factories.player.collider import create_body_and_feet
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
        from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
        from roguelike_game.ecs.components.ai.damage_config import DamageConfig

        comps = world.components

        # Tag
        comps["PlayerTagComponent"][eid] = PlayerTagComponent(new_class)

        # Sprites & animations
        sprites = load_and_scale_sprites(new_class)
        frame = extract_initial_frame(sprites)
        if frame and isinstance(frame, pygame.Surface):
            img = frame
        else:
            size = player_cfg.ORIGINAL_SPRITE_SIZE
            img = pygame.Surface(size, pygame.SRCALPHA)
        comps["Sprite"][eid] = Sprite(img)
        comps["Animator"][eid] = Animator(
            animations=build_animator_map(sprites),
            current_state=player_cfg.INITIAL_ANIMATION_STATE,
            masks=build_masks_map(sprites),
        )
        comps["AnimationTimer"][eid] = AnimationTimer(
            last_time=time.time(),
            interval=player_cfg.ANIMATION_INTERVAL,
        )

        # Movement
        comps["MovementSpeed"][eid] = MovementSpeed(
            player_cfg.PLAYER_STATS[new_class]["basic_speed"]
        )
        comps["Velocity"][eid] = Velocity(0, 0)

        # Collider
        comps["MultiCollider"][eid] = create_body_and_feet(img)

        # Stats
        stats = player_cfg.PLAYER_STATS[new_class]
        max_hp = stats["max_strength"]
        comps["Health"][eid] = Health(max_hp, max_hp)

        dmg_duration = stats.get("damage_duration", player_cfg.DEFAULT_DAMAGE_DURATION)
        stop_prob = float(stats.get("damage_stop_probability",
                                    getattr(player_cfg, "DEFAULT_DAMAGE_STOP_PROBABILITY", 0.25)))
        comps["DamageConfig"][eid] = DamageConfig(float(dmg_duration), stop_probability=stop_prob)

        comps["CombatStats"][eid] = CombatStats(
            current_hp=max_hp, max_hp=max_hp,
            power=stats["basic_attack"],
            defense=stats["basic_armor"],
        )

        max_mana = stats["max_intelligence"]
        comps["Mana"][eid] = Mana(current_mana=max_mana, max_mana=max_mana)

        max_energy = stats["max_dexterity"]
        comps["Energy"][eid] = Energy(current_energy=max_energy, max_energy=max_energy)

        max_hunger = stats.get("max_hunger", 100)
        comps["Hunger"][eid] = Hunger(current_hunger=max_hunger, max_hunger=max_hunger)

        # Melee weapon
        comps["MeleeWeapon"][eid] = MeleeWeapon(
            damage=player_cfg.MELEE_WEAPON_CFG["damage"],
            cooldown=player_cfg.MELEE_WEAPON_CFG["cooldown"],
        )

        # Trail
        trail_params = stats.get("basic_trail", player_cfg.DEFAULT_TRAIL)
        trail_cfg = TrailConfig(
            interval=trail_params["interval"],
            life_time=trail_params["life_time"],
            max_trails=trail_params["max_trails"],
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

        logger.info("[ClassChangeSystem] Applied class '%s' to eid=%d", new_class, eid)
