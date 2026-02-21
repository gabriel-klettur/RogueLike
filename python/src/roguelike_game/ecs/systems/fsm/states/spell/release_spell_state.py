"""State responsible for releasing (*casting*) spells."""

from __future__ import annotations

import logging

from roguelike_game.ecs.systems.fsm.state import State

from .release_handlers.registry import get_handler
from .spell_release_context import build_context


logger = logging.getLogger(__name__)

class ReleaseSpellState(State):
    def enter(self, entity):
        context = build_context(entity, self.fsm)
        context.deduct_mana_cost()

        handler = get_handler(context.spell_type or "projectile")
        try:
            handler.handle(context)
        except Exception:  # pragma: no cover - keep runtime resilience
            logger.exception(
                "Failed to release spell",
                extra={
                    "spell": context.spell_key,
                    "spell_type": context.spell_type,
                    "handler": handler.__class__.__name__,
                },
            )

    def execute(self, entity, dt):
        # Transición inmediata a fase de resolución
        from roguelike_game.ecs.systems.fsm.states.spell.resolve_spell_state import ResolveSpellState
        self.fsm.change_state(ResolveSpellState(), entity)

    def exit(self, entity):
        pass