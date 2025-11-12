"""Refactored AttackState orchestrating melee behavior."""

from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Any

from roguelike_game.ecs.systems.fsm.anim_bridge import (
    primary_direction_from_vector,
    set_mapped_anim,
)
from roguelike_game.ecs.systems.fsm.state import State

from .context import AttackEnvironment, AttackFSMContext
from .monster_profile import MonsterProfile
from .transitions import (
    cleanup_attack_effects,
    cooldown_ready,
    handle_windup_phase,
    perform_attack,
    prepare_chase_target,
    reset_velocity,
    should_abort_attack,
    should_cancel_due_to_player,
    should_exit_after_firing,
    within_melee_range,
)


@dataclass
class AttackState(State):
    """NPC melee attack state with explicit orchestration steps."""

    windup_default: float = 1.0

    def enter(self, entity: Any) -> None:
        env = AttackEnvironment(world=entity.world, entity_id=entity.id, now=time.time())
        context = AttackFSMContext(env.world, env.entity_id)
        profile = MonsterProfile.from_world(env.world, env.entity_id)

        prepare_chase_target(env, profile.is_final_boss)
        reset_velocity(env)
        context.pop("attack_fired", None)
        context.mark_attack_start(env.now)
        context.ensure_attack_duration(self._derive_duration(env))
        self._update_animation(entity, env)

    def execute(self, entity: Any, dt: float) -> None:  # noqa: D401 -  part of State API
        env = AttackEnvironment(world=entity.world, entity_id=entity.id, now=time.time())
        context = AttackFSMContext(env.world, env.entity_id)
        profile = MonsterProfile.from_world(env.world, env.entity_id)

        if should_abort_attack(env):
            self._to_unconscious(env, entity)
            return

        if should_cancel_due_to_player(env):
            self._to_patrol(env, entity)
            return

        snapshot = env.compute_positions()
        if snapshot is None:
            return

        if profile.is_final_boss:
            if self._process_final_boss(entity, env, context, profile, snapshot):
                return
        else:
            if self._process_regular(entity, env, context, profile, snapshot):
                return

        cleanup_attack_effects(env)
        env.remove_component("ChaseTarget")
        self._to_chase(env, entity)

    def exit(self, entity: Any) -> None:
        env = AttackEnvironment(world=entity.world, entity_id=entity.id, now=time.time())
        cleanup_attack_effects(env)
        env.remove_component("ChaseTarget")

    # ----- Helper methods -------------------------------------------------

    def _derive_duration(self, env: AttackEnvironment) -> float:
        melee_map = env.world.components.get("MeleeWeapon", {})
        weapon = melee_map.get(env.entity_id)
        cooldown = getattr(weapon, "cooldown", 0.0)
        try:
            cooldown = float(cooldown)
        except Exception:
            cooldown = 0.5
        return cooldown if cooldown > 0 else 0.5

    def _lookup_melee_range(self, env: AttackEnvironment) -> float:
        melee_map = env.world.components.get("MeleeRange", {})
        melee_component = melee_map.get(env.entity_id)
        return float(getattr(melee_component, "range", 0.0) or 0.0)

    def _update_animation(self, entity: Any, env: AttackEnvironment) -> None:
        snapshot = env.compute_positions()
        if snapshot is None:
            direction = None
        else:
            direction = primary_direction_from_vector(*snapshot.delta)
        set_mapped_anim(entity, "AttackState", direction, reset_frame=True)

    def _process_regular(
        self,
        entity: Any,
        env: AttackEnvironment,
        context: AttackFSMContext,
        profile: MonsterProfile,
        snapshot,
    ) -> bool:
        melee_range = self._lookup_melee_range(env)
        if not within_melee_range(melee_range, snapshot.distance_sq):
            return False
        start_time = context.mark_attack_start(env.now)
        windup_s = 0.0 if not context.has_context else context.get_float("attack_windup_s", self.windup_default)
        elapsed = env.now - start_time
        if handle_windup_phase(env, context, profile, snapshot, elapsed, windup_s):
            return True
        if not cooldown_ready(env):
            return True
        perform_attack(env, context, profile, snapshot)
        return True

    def _process_final_boss(
        self,
        entity: Any,
        env: AttackEnvironment,
        context: AttackFSMContext,
        profile: MonsterProfile,
        snapshot,
    ) -> bool:
        if context.get_bool("attack_fired"):
            if should_exit_after_firing(context, env):
                cleanup_attack_effects(env)
                env.remove_component("ChaseTarget")
                self._to_chase(env, entity)
            else:
                reset_velocity(env)
                env.remove_component("ChaseTarget")
            return True

        start_time = context.mark_attack_start(env.now)
        windup_s = 0.0 if not context.has_context else context.get_float("attack_windup_s", self.windup_default)
        elapsed = env.now - start_time
        if handle_windup_phase(env, context, profile, snapshot, elapsed, windup_s):
            return True
        if not cooldown_ready(env):
            return True
        perform_attack(env, context, profile, snapshot)
        env.remove_component("ChaseTarget")
        return True

    def _to_unconscious(self, env: AttackEnvironment, entity: Any) -> None:
        from roguelike_game.ecs.systems.fsm.states.unconscious_state import UnconsciousState

        npc_state = env.world.components["NPCState"][env.entity_id]
        npc_state.fsm.change_state(UnconsciousState(), entity)

    def _to_patrol(self, env: AttackEnvironment, entity: Any) -> None:
        from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState

        npc_state = env.world.components["NPCState"][env.entity_id]
        npc_state.fsm.change_state(PatrolState(), entity)

    def _to_chase(self, env: AttackEnvironment, entity: Any) -> None:
        from roguelike_game.ecs.systems.fsm.states.monster.chase_state import ChaseState

        npc_state = env.world.components["NPCState"][env.entity_id]
        npc_state.fsm.change_state(ChaseState(), entity)
