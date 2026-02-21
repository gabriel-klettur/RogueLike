"""Rules that limit projectile spawning."""

from __future__ import annotations

import logging

from ...spell_release_context import SpellReleaseContext
from ...release_utils import coerce_int

logger = logging.getLogger(__name__)


def exceeds_instance_limit(context: SpellReleaseContext) -> bool:
    """Return ``True`` when the spell already has the maximum active projectiles."""

    if context.spell_type != "projectile":
        return False

    try:
        max_instances = coerce_int(context.cfg_value("max_instances", 0), default=0)
    except Exception:  # pragma: no cover - legacy resilience
        logger.debug("Cannot read max_instances for %s", context.spell_key, exc_info=True)
        return False

    if max_instances <= 0:
        return False

    components = context.get_component_map("FireballComponent")
    active = 0
    for component in components.values():
        try:
            if getattr(component, "spell_key", "") == context.spell_key:
                active += 1
        except Exception:
            continue
        if active >= max_instances:
            return True
    return False
