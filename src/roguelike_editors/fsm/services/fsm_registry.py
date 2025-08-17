from __future__ import annotations
from typing import Dict, Type, Optional

from roguelike_game.ecs.systems.fsm.state import State
import logging
logger = logging.getLogger(__name__)

_STATE_REGISTRY: Dict[str, Type[State]] = {}


def _try_register(qual_name: str, cls_name: str, key: Optional[str] = None) -> None:
    if key is None:
        key = cls_name
    try:
        module = __import__(qual_name, fromlist=[cls_name])
        cls = getattr(module, cls_name, None)
        if cls is not None:
            _STATE_REGISTRY[key] = cls  # type: ignore[assignment]
    except Exception as ex:
        # Optional states may not exist yet during early phases
        logger.warning("[FSMRegistry] failed to import %s.%s: %s", qual_name, cls_name, ex)


# Core/common
_try_register('roguelike_game.ecs.systems.fsm.states.idle_state', 'IdleState')
_try_register('roguelike_game.ecs.systems.fsm.states.attack_state', 'AttackState')
# Player
_try_register('roguelike_game.ecs.systems.fsm.states.player.move_state', 'MoveState')
_try_register('roguelike_game.ecs.systems.fsm.states.player.player_attack_state', 'PlayerAttackState')
_try_register('roguelike_game.ecs.systems.fsm.states.player.player_spell_select_state', 'PlayerSpellSelectState')
# Monster
_try_register('roguelike_game.ecs.systems.fsm.states.monster.patrol_state', 'PatrolState')
_try_register('roguelike_game.ecs.systems.fsm.states.monster.chase_state', 'ChaseState')
_try_register('roguelike_game.ecs.systems.fsm.states.monster.aggro_state', 'AggroState')
_try_register('roguelike_game.ecs.systems.fsm.states.monster.flee_state', 'FleeState')
_try_register('roguelike_game.ecs.systems.fsm.states.monster.alert_chase_state', 'AlertChaseState')
_try_register('roguelike_game.ecs.systems.fsm.states.death_state', 'DeathState')
_try_register('roguelike_game.ecs.systems.fsm.states.damage_state', 'DamageState')


def get_state_class(name: str) -> Optional[Type[State]]:
    return _STATE_REGISTRY.get(name)


__all__ = ["get_state_class", "_STATE_REGISTRY"]
