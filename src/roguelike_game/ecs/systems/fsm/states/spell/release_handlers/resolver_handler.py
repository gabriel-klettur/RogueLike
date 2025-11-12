"""Handler wrapping existing resolver-based spell execution."""

from __future__ import annotations

from typing import Callable

from roguelike_game.ecs.systems.combat.spells.resolvers import SPELL_RESOLVERS

from .base import SpellReleaseHandler, SupportsSpellContext


Hook = Callable[[SupportsSpellContext], None]


class ResolverSpellReleaseHandler(SpellReleaseHandler):
    """Delegate spell execution to preexisting resolver instances."""

    def __init__(
        self,
        resolver_key: str | None = None,
        *,
        before: Hook | None = None,
        after: Hook | None = None,
    ) -> None:
        self._resolver_key = resolver_key
        self._before = before
        self._after = after

    def handle(self, context: SupportsSpellContext) -> None:
        if self._before is not None:
            self._before(context)

        spell_key = self._resolver_key or context.spell_type
        resolver = SPELL_RESOLVERS.get(spell_key)
        if resolver is not None and context.world is not None:
            resolver.resolve(context.world, context.entity.id, context.context, context.spell_cfg, context.camera)

        if self._after is not None:
            self._after(context)
