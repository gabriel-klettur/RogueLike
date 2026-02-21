"""Projectile spell release handler."""

from __future__ import annotations

import logging
from typing import Any, Dict, Tuple

from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.transform.velocity import Velocity

from ..release_utils import coerce_float, coerce_int, enqueue_audio_event, normalise_vector, radial_directions
from ..spell_release_context import SpellReleaseContext
from .base import SpellReleaseHandler, SupportsSpellContext
from .projectile.geometry import compute_direction, compute_spawn_position
from .projectile.limits import exceeds_instance_limit
from .projectile.params import ProjectileParams, build_projectile_params

logger = logging.getLogger(__name__)


class ProjectileReleaseHandler(SpellReleaseHandler):
    """Handle projectile-style spell releases."""

    def handle(self, context: SupportsSpellContext) -> None:  # noqa: C901 - complex by nature
        real_context = self._ensure_context(context)
        world = real_context.world
        if world is None:
            return

        if exceeds_instance_limit(real_context):
            return

        spawn = compute_spawn_position(real_context)
        direction = compute_direction(real_context, spawn)
        params = build_projectile_params(real_context)
        component_maps = self._collect_component_maps(real_context)

        try:
            if self._handle_burst(real_context, component_maps, spawn, params):
                return

            fireball_id = self._spawn_central(real_context, component_maps, spawn, direction, params)
            real_context.mark_fireball_id(fireball_id)
            enqueue_audio_event(world, {"type": "play_sfx", "sfx_id": "fireball", "group": "sfx"})
            self._spawn_parallel(real_context, component_maps, spawn, direction, params)
        except Exception:
            logger.exception("Failed to release projectile spell", extra={"spell": real_context.spell_key})

    # ------------------------------------------------------------------
    @staticmethod
    def _ensure_context(context: SupportsSpellContext) -> SpellReleaseContext:
        if isinstance(context, SpellReleaseContext):
            return context
        return SpellReleaseContext(entity=context.entity, fsm=getattr(context, "fsm", None))

    @staticmethod
    def _collect_component_maps(context: SpellReleaseContext) -> Dict[str, Dict[Any, Any]]:
        return {
            "Position": context.get_component_map("Position"),
            "Velocity": context.get_component_map("Velocity"),
            "FireballComponent": context.get_component_map("FireballComponent"),
            "Sprite": context.get_component_map("Sprite"),
            "Scale": context.get_component_map("Scale"),
        }

    def _spawn_central(
        self,
        context: SpellReleaseContext,
        maps: Dict[str, Dict[Any, Any]],
        spawn: Tuple[float, float],
        direction: Tuple[float, float],
        params: ProjectileParams,
    ) -> Any:
        forward_offset = coerce_float(
            context.context.get(
                "central_forward_offset",
                context.cfg_value("central_forward_offset", 0.0),
            ),
            default=0.0,
        )
        spawn_pos = (
            spawn[0] + direction[0] * forward_offset,
            spawn[1] + direction[1] * forward_offset,
        )
        velocity = (direction[0] * params.speed, direction[1] * params.speed)
        return self._spawn_projectile(context, maps, spawn_pos, velocity, params)

    def _handle_burst(
        self,
        context: SpellReleaseContext,
        maps: Dict[str, Dict[Any, Any]],
        spawn: Tuple[float, float],
        params: ProjectileParams,
    ) -> bool:
        directions = context.context.get("burst_directions")
        if not isinstance(directions, list) or not directions:
            radial_count = coerce_int(
                context.context.get("radial_count", context.cfg_value("radial_count", 0)),
                default=0,
            )
            if radial_count >= 3:
                start_deg = coerce_float(
                    context.context.get("radial_start_deg", context.cfg_value("radial_start_deg", 0.0)),
                    default=0.0,
                )
                directions = radial_directions(radial_count, start_deg)
            else:
                directions = []

        if not directions:
            return False

        forward_offset = coerce_float(
            context.context.get(
                "central_forward_offset",
                context.cfg_value("central_forward_offset", 0.0),
            ),
            default=0.0,
        )

        last_id = None
        for raw_direction in directions:
            if not isinstance(raw_direction, (tuple, list)) or len(raw_direction) < 2:
                continue
            direction = normalise_vector(raw_direction)
            spawn_pos = (
                spawn[0] + direction[0] * forward_offset,
                spawn[1] + direction[1] * forward_offset,
            )
            velocity = (direction[0] * params.speed, direction[1] * params.speed)
            last_id = self._spawn_projectile(context, maps, spawn_pos, velocity, params)

        if last_id is not None:
            context.mark_fireball_id(last_id)
        return True

    def _spawn_parallel(
        self,
        context: SpellReleaseContext,
        maps: Dict[str, Dict[Any, Any]],
        spawn: Tuple[float, float],
        direction: Tuple[float, float],
        params: ProjectileParams,
    ) -> None:
        if context.spell_type != "projectile":
            return

        parallel_count = coerce_int(
            context.context.get("parallel_count", context.cfg_value("parallel_count", 1)),
            default=1,
        )
        if parallel_count <= 1:
            return

        spacing = coerce_float(
            context.context.get("parallel_spacing", context.cfg_value("parallel_spacing", 16.0)),
            default=16.0,
        )
        forward_offset = coerce_float(
            context.context.get("sides_forward_offset", context.cfg_value("sides_forward_offset", 0.0)),
            default=0.0,
        )

        perpendicular = normalise_vector((-direction[1], direction[0]))
        for side in (-1, 1):
            spawn_pos = (
                spawn[0] + perpendicular[0] * spacing * side + direction[0] * forward_offset,
                spawn[1] + perpendicular[1] * spacing * side + direction[1] * forward_offset,
            )
            velocity = (direction[0] * params.speed, direction[1] * params.speed)
            self._spawn_projectile(context, maps, spawn_pos, velocity, params)

    def _spawn_projectile(
        self,
        context: SpellReleaseContext,
        maps: Dict[str, Dict[Any, Any]],
        spawn_pos: Tuple[float, float],
        velocity: Tuple[float, float],
        params: ProjectileParams,
    ) -> Any:
        entity_id = context.world.create_entity()
        maps["Position"][entity_id] = Position(spawn_pos[0], spawn_pos[1])
        maps["Velocity"][entity_id] = Velocity(velocity[0], velocity[1])
        maps["FireballComponent"][entity_id] = FireballComponent(
            velocity[0],
            velocity[1],
            damage=context.cfg_value("damage", 0),
            lifespan=context.cfg_value("lifespan", 0),
            caster=context.entity.id,
            spell_key=context.spell_key,
            spawn_pos=spawn_pos,
            vfx_scale_multiplier=params.scale_multiplier,
            hit_radius=params.hit_radius,
        )
        if params.sprite_surface is not None:
            try:
                maps["Sprite"][entity_id] = Sprite(params.sprite_surface)
                maps["Scale"][entity_id] = Scale(scale=params.effective_scale)
            except Exception:
                logger.debug("Failed to set sprite for projectile", exc_info=True)
        try:
            logger.debug(
                "[ProjectileReleaseHandler] Spawn fireball eid=%s spell=%s pos=(%.1f, %.1f) vel=(%.2f, %.2f)",
                entity_id,
                context.spell_key,
                spawn_pos[0],
                spawn_pos[1],
                velocity[0],
                velocity[1],
            )
        except Exception:
            pass
        return entity_id
