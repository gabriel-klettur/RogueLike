"""State transition helpers for the NPC attack state."""

from __future__ import annotations

from typing import Dict, Tuple

from roguelike_engine.config.config_tiles import TILE_SIZE

from roguelike_game.ecs.components.ai.chase_target import ChaseTarget
from roguelike_game.ecs.components.combat.hitbox import HitboxComponent
from roguelike_game.ecs.components.combat.npc_attack_cooldown import NPCAttackCooldown
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.systems.combat.spells.resolvers import SPELL_RESOLVERS

from .context import AttackEnvironment, AttackFSMContext, PositionSnapshot, normalize_vector
from .monster_profile import MonsterProfile
from .telegraph import apply_telegraph, build_telegraph_config, clear_telegraph


def should_abort_attack(env: AttackEnvironment) -> bool:
    """Return True when the NPC should exit the attack state immediately."""
    health = env.get_component("Health")
    return health is None or getattr(health, "current_hp", 0) <= 0


def should_cancel_due_to_player(env: AttackEnvironment) -> bool:
    return env.is_player_defeated() or env.player_has_death_timer()


def within_melee_range(range_tiles: float, distance_sq: float) -> bool:
    if range_tiles <= 0:
        return False
    melee_dist_sq = (range_tiles * TILE_SIZE) ** 2
    return distance_sq <= melee_dist_sq


def prepare_chase_target(env: AttackEnvironment, is_final_boss: bool) -> None:
    chase_map = env.world.components.setdefault("ChaseTarget", {})
    if is_final_boss:
        chase_map.pop(env.entity_id, None)
        return
    if env.player_id is None:
        chase_map.pop(env.entity_id, None)
        return
    chase_map[env.entity_id] = ChaseTarget(env.player_id)


def reset_velocity(env: AttackEnvironment) -> None:
    velocity_map: Dict[int, Velocity] = env.world.components.setdefault("Velocity", {})
    velocity_map[env.entity_id] = Velocity(0, 0)


def cleanup_attack_effects(env: AttackEnvironment) -> None:
    clear_telegraph(env.world, env.entity_id)
    env.remove_component("WindupOutline")


def update_windup_visuals(
    env: AttackEnvironment,
    context: AttackFSMContext,
    profile: MonsterProfile,
    direction: Tuple[float, float],
    progress: float,
) -> None:
    show_telegraph = profile.is_final_boss or context.get_bool("use_attack_telegraph")
    outline_map = env.world.components.setdefault("WindupOutline", {})
    outline_map[env.entity_id] = outline_map.get(env.entity_id) or _new_windup_outline()
    if not show_telegraph:
        return
    config = build_telegraph_config(profile.resolve_spell_id(), direction, progress)
    apply_telegraph(env.world, env.entity_id, config)


def clear_windup_visuals(env: AttackEnvironment) -> None:
    cleanup_attack_effects(env)


def handle_windup_phase(
    env: AttackEnvironment,
    context: AttackFSMContext,
    profile: MonsterProfile,
    snapshot: PositionSnapshot,
    elapsed: float,
    windup_s: float,
) -> bool:
    if elapsed < windup_s:
        ndx, ndy, _ = normalize_vector(*snapshot.delta)
        reset_velocity(env)
        env.remove_component("ChaseTarget")
        progress = elapsed / max(1e-6, windup_s)
        update_windup_visuals(env, context, profile, (ndx, ndy), progress)
        return True
    return False


def _new_windup_outline():
    from roguelike_game.ecs.components.combat.windup_outline import WindupOutline

    return WindupOutline()


def perform_attack(env: AttackEnvironment, context: AttackFSMContext, profile: MonsterProfile, snapshot: PositionSnapshot) -> None:
    ndx, ndy, _ = normalize_vector(*snapshot.delta)
    direction = (ndx, ndy)
    spell_cfg = _resolve_spell_config(profile.resolve_spell_id())
    cleanup_attack_effects(env)
    _spawn_slash(env, spell_cfg, direction)
    lock_duration = context.ensure_attack_duration(float(spell_cfg.get("cooldown_duration", 1.0)))
    if profile.is_final_boss:
        context.mark_attack_fired(env.now, lock_duration)
    reset_velocity(env)
    context.set("attack_start", env.now)


def _resolve_spell_config(spell_id: str):
    from roguelike_game.config.spells_config import SPELLS

    return SPELLS.get(spell_id) or SPELLS.get("hostile_slash") or SPELLS.get("slash") or {}


def _spawn_slash(env: AttackEnvironment, cfg, direction: Tuple[float, float]) -> None:
    hit_map = env.world.components.setdefault("HitboxComponent", {})
    before_count = len(hit_map)
    spawn_meta = {
        "target_eid": int(env.player_id) if env.player_id is not None else 0,
        "rotate_with_owner": False,
    }
    resolver = SPELL_RESOLVERS.get("slash")
    if resolver is not None:
        try:
            resolver.resolve(env.world, env.entity_id, spawn_meta, cfg, None)
        except Exception:
            pass
    if len(hit_map) == before_count:
        _manual_hitbox(env, cfg, direction)
    cooldown_map = env.world.components.setdefault("NPCAttackCooldown", {})
    cooldown_map[env.entity_id] = NPCAttackCooldown(next_time=env.now + float(cfg.get("cooldown_duration", 1.0)))


def _manual_hitbox(env: AttackEnvironment, cfg, direction: Tuple[float, float]) -> None:
    hit_map = env.world.components.setdefault("HitboxComponent", {})
    hb_id = _create_hitbox_entity(env)
    radius = float(cfg.get("hit_radius", 40.0))
    arc_degrees = float(cfg.get("hit_arc_degrees", 90.0))
    arc_radians = __import__("math").radians(arc_degrees)
    lifespan = int(cfg.get("lifetime", 15))
    damage = float(cfg.get("damage", 0.0))
    offset = float(cfg.get("offset", 0.0))
    origin = _npc_origin(env)
    env.world.components.setdefault("Position", {})[hb_id] = Position(
        origin[0] + direction[0] * offset,
        origin[1] + direction[1] * offset,
    )
    hit_map[hb_id] = HitboxComponent(
        owner=env.entity_id,
        offset=offset,
        radius=radius,
        arc_angle=arc_radians,
        direction=direction,
        lifespan=lifespan,
        damage=damage,
        follow_owner=True,
        rotate_with_owner=False,
    )


def _npc_origin(env: AttackEnvironment) -> Tuple[float, float]:
    snapshot = env.compute_positions()
    if snapshot is None:
        position = env.get_component("Position")
        return float(getattr(position, "x", 0.0)), float(getattr(position, "y", 0.0))
    return snapshot.origin


def _create_hitbox_entity(env: AttackEnvironment) -> int:
    try:
        return env.world.create_entity()
    except Exception:
        return env.entity_id * 100000 + 1


def should_exit_after_firing(context: AttackFSMContext, env: AttackEnvironment) -> bool:
    return context.get_bool("attack_fired") and context.lock_expired(env.now)


def cooldown_ready(env: AttackEnvironment) -> bool:
    cooldown_map = env.world.components.get("NPCAttackCooldown", {})
    cooldown = cooldown_map.get(env.entity_id)
    if cooldown is None:
        return True
    return env.now >= getattr(cooldown, "next_time", env.now)

