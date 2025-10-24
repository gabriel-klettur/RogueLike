from roguelike_game.ecs.utils.health_utils import get_current_hp
from roguelike_game.ecs.components.combat.dying_tag import DyingTag


class DeathSystem:
    """
    Centraliza la detección de muerte (hp <= 0) y encola OnDeath una sola vez.
    Evita duplicación en sistemas de daño. No crea DeathTimer (lo hace UnconsciousState).

    Nota: ignora entidades de tipo Spawner/Building que exponen Health para HUD.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def _is_spawner_or_building(self, comps, eid: int) -> bool:
        # Spawners administrados por SpawnerDamageSystem
        if eid in comps.get('SpawnerConfig', {}) or eid in comps.get('SpawnerState', {}):
            return True
        # Buildings podrían tener otras marcas; por ahora sólo spawners
        return False

    def _maybe_enqueue_death(self, world, comps, eid: int, death, fsmq):
        if eid in death:
            return
        if eid in comps.get('DyingTag', {}):
            return
        if self._is_spawner_or_building(comps, eid):
            return
        hp = get_current_hp(world, eid)
        if hp is None:
            return
        if hp <= 0:
            # Marcar entidad como "en muerte" para que el resto del frame pueda consultarlo
            comps.setdefault('DyingTag', {})[eid] = DyingTag()
            q = fsmq.setdefault(eid, [])
            q.append({'type': 'OnDeath'})

    def update(self, world, camera=None):
        comps = world.components
        death = comps.get('DeathTimer', {})
        fsmq = comps.setdefault('FSMEventQueue', {})
        seen = set()
        for eid in list(comps.get('Health', {}).keys()):
            seen.add(eid)
            self._maybe_enqueue_death(world, comps, eid, death, fsmq)
        for eid in list(comps.get('CombatStats', {}).keys()):
            if eid in seen:
                continue
            self._maybe_enqueue_death(world, comps, eid, death, fsmq)
