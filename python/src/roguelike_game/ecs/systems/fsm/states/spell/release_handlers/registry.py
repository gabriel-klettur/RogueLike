"""Registry of spell release handlers."""

from __future__ import annotations

import logging
from typing import Dict

from roguelike_game.ecs.utils.position_utils import compute_entity_center

from ..spell_release_context import SpellReleaseContext
from ..release_utils import enqueue_audio_event
from .base import SpellReleaseHandler, SupportsSpellContext
from .function_handler import FunctionSpellReleaseHandler
from .projectile_handler import ProjectileReleaseHandler
from .resolver_handler import Hook, ResolverSpellReleaseHandler

logger = logging.getLogger(__name__)

_REGISTRY: Dict[str, SpellReleaseHandler] | None = None
_DEFAULT_PROJECTILE_HANDLER = ProjectileReleaseHandler()


def get_handler(spell_type: str | None) -> SpellReleaseHandler:
    """Return the handler registered for *spell_type* or a fallback."""

    registry = _ensure_registry()
    if spell_type and spell_type in registry:
        return registry[spell_type]
    return _DEFAULT_PROJECTILE_HANDLER


def register_handler(spell_type: str, handler: SpellReleaseHandler) -> None:
    """Register or override a handler at runtime."""

    registry = _ensure_registry()
    registry[spell_type] = handler


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------


def _ensure_registry() -> Dict[str, SpellReleaseHandler]:
    global _REGISTRY
    if _REGISTRY is None:
        _REGISTRY = _build_default_registry()
    return _REGISTRY


def _build_default_registry() -> Dict[str, SpellReleaseHandler]:
    resolver = ResolverSpellReleaseHandler
    registry: Dict[str, SpellReleaseHandler] = {
        "sphere_magic_shield": resolver("sphere_magic_shield"),
        "teleport": resolver("teleport"),
        "aura": resolver("aura"),
        "dash": resolver("dash"),
        "lightning": resolver("lightning"),
        "chain_lightning": resolver("chain_lightning"),
        "vortex_field": resolver("vortex_field"),
        "arcane_flame": resolver("arcane_flame"),
        "firework_launch": resolver("firework_launch"),
        "smoke": resolver("smoke"),
        "smoke_emitter": resolver("smoke_emitter"),
        "mine": resolver("mine"),
        "boomerang": resolver("boomerang"),
        "meteor_shower": resolver("meteor_shower"),
        "summon": resolver("summon"),
        "totem": resolver("totem"),
        "wall": resolver("wall"),
        "projectile": _DEFAULT_PROJECTILE_HANDLER,
        "cone_breath": FunctionSpellReleaseHandler(_noop),
        "beam": FunctionSpellReleaseHandler(_stop_beam),
        "puddle": resolver("puddle", before=_prepare_puddle_spawn),
        "slash": resolver("slash", after=_play_slash_audio),
    }
    return registry


# ---------------------------------------------------------------------------
# Hooks
# ---------------------------------------------------------------------------


def _noop(context: SupportsSpellContext) -> None:  # pragma: no cover - trivial
    del context


def _stop_beam(context: SupportsSpellContext) -> None:
    world = getattr(context, "world", None)
    if world is None:
        return
    try:
        world.components.get("LaserBeamComponent", {}).pop(context.entity.id, None)
    except Exception:
        logger.debug("Failed to clean beam component", exc_info=True)


def _play_slash_audio(context: SupportsSpellContext) -> None:
    world = getattr(context, "world", None)
    if world is None:
        return
    event = {
        "type": "play_sfx",
        "choices": [
            "sword_clash_1",
            "sword_clash_2",
            "sword_clash_3",
            "sword_clash_4",
            "sword_clash_5",
            "sword_clash_6",
            "sword_clash_7",
            "sword_clash_8",
            "sword_clash_9",
            "sword_clash_10",
        ],
        "group": "sfx",
    }
    enqueue_audio_event(world, event)


def _prepare_puddle_spawn(context: SupportsSpellContext) -> None:
    if not isinstance(context, SpellReleaseContext):
        return
    ctx = context.context
    try:
        spawn_pos = ctx.get("spawn_pos")
        has_spawn = isinstance(spawn_pos, (tuple, list)) and len(spawn_pos) == 2
    except Exception:
        has_spawn = False
    if has_spawn:
        return

    world = context.world
    if world is None:
        return
    if context.entity.id == getattr(world, "player_entity", None):
        return

    try:
        player_id = getattr(world, "player_entity", None)
        if player_id is None:
            return
        components = getattr(world, "components", {})
        pos_map = components.get("Position", {})
        sprite_map = components.get("Sprite", {})
        scale_map = components.get("Scale", {})
        player_pos = pos_map.get(player_id)
        if player_pos is None:
            return
        player_sprite = sprite_map.get(player_id)
        player_scale = scale_map.get(player_id)
        if player_sprite is not None:
            center = compute_entity_center(player_pos, player_sprite, player_scale)
            context.set_spawn_position((float(center.x), float(center.y)))
        else:
            context.set_spawn_position((float(getattr(player_pos, "x", 0.0)), float(getattr(player_pos, "y", 0.0))))
    except Exception:
        logger.debug("Failed to prepare puddle spawn", exc_info=True)
