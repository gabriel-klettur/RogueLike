from roguelike_game.config.players_config import PLAYER_STATS
from roguelike_game.factories.monster.config import MONSTER_STATS, MONSTER_DEFAULTS
from roguelike_game.ecs.components.core.identity import Faction


def get_current_hp(world, eid):
    h = world.components.get('Health', {}).get(eid)
    if h is not None:
        try:
            return int(h.current_hp)
        except Exception:
            return None
    cs = world.components.get('CombatStats', {}).get(eid)
    if cs is not None:
        try:
            return int(cs.current_hp)
        except Exception:
            return None
    return None


def set_current_hp(world, eid, value):
    h = world.components.get('Health', {}).get(eid)
    if h is not None:
        try:
            h.current_hp = int(value)
            return True
        except Exception:
            return False
    cs = world.components.get('CombatStats', {}).get(eid)
    if cs is not None:
        try:
            cs.current_hp = int(value)
            return True
        except Exception:
            return False
    return False


def is_player(world, eid) -> bool:
    try:
        return eid in world.components.get('PlayerTagComponent', {})
    except Exception:
        return False


def resolve_death_duration(world, eid) -> float:
    """
    Devuelve la duración del DeathTimer para una entidad (jugador o monstruo).
    Mantiene la misma política que hoy aplica `UnconsciousState.enter()`.
    """
    try:
        if is_player(world, eid):
            pt = world.components.get('PlayerTagComponent', {}).get(eid)
            cls_name = getattr(pt, 'class_name', None)
            if cls_name and cls_name in PLAYER_STATS:
                val = PLAYER_STATS[cls_name].get('basic_death_timer_duration', 60.0)
                return float(val) if val is not None else 60.0
            return 60.0
        # Monster/NPC
        arche = world.components.get('MonsterArchetype', {}).get(eid)
        monster_class = getattr(arche, 'type', None) if arche is not None else None
        if not monster_class:
            identity = world.components.get('Identity', {}).get(eid)
            monster_class = getattr(identity, 'name', None) if identity else None
        stats = MONSTER_STATS.get(monster_class, {}) if (monster_class in MONSTER_STATS) else {}
        duration = stats.get('death_dissapear_time')
        if duration is None:
            duration = MONSTER_DEFAULTS.get('death_dissapear_time', 30.0)
        return float(duration) if duration is not None else 30.0
    except Exception:
        # Fallbacks seguros
        return 60.0 if is_player(world, eid) else 30.0


def is_neutral(world, eid) -> bool:
    """
    True si la entidad tiene Identity con facción NEUTRAL.
    """
    try:
        idt = world.components.get('Identity', {}).get(eid)
        return bool(idt and getattr(idt, 'faction', None) == Faction.NEUTRAL)
    except Exception:
        return False
