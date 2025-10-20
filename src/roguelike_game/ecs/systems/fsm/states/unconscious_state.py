from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.combat.death_timer import DeathTimer
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_engine.config.config_z_layer import Z_LAYERS
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.config.players_config import PLAYER_STATS
from roguelike_game.factories.monster.config import MONSTER_STATS, MONSTER_DEFAULTS

import time
import logging
logger = logging.getLogger(__name__)


class UnconsciousState(State):
    """
    Estado Unconscious: el cuerpo queda tendido en el suelo y corre un temporizador de desaparición.
    Al expirar, la FSM transiciona a DeathState (desaparición o gris, según sea NPC o Player).
    """

    def enter(self, entity):
        world = entity.world
        eid = entity.id
        logger.debug(f"[Unconscious.enter] eid={eid}, is_player={eid in world.components.get('PlayerTagComponent', {})}")
        # Determinar duración del temporizador de inconsciencia/desaparición
        duration = None
        pt = world.components.get('PlayerTagComponent', {}).get(eid)
        if pt:
            cls_name = getattr(pt, 'class_name', None)
            if cls_name and cls_name in PLAYER_STATS:
                duration = PLAYER_STATS[cls_name].get('basic_death_timer_duration', 60.0)
        else:
            identity = world.components.get('Identity', {}).get(eid)
            monster_class = getattr(identity, 'name', None) if identity else None
            stats = MONSTER_STATS.get(monster_class, {}) if (monster_class and monster_class in MONSTER_STATS) else {}
            duration = stats.get('death_dissapear_time')
            if duration is None:
                duration = MONSTER_DEFAULTS.get('death_dissapear_time')
        logger.debug(f"[Unconscious.enter] eid={eid} death_timer_duration={duration}")
        world.components.setdefault('DeathTimer', {})[eid] = DeathTimer(time.time(), duration)
        # Limpiar flash
        world.components.get('FlashComponent', {}).pop(eid, None)
        # Anular movimiento
        vel_map = world.components.get('Velocity', {})
        if eid in vel_map:
            try:
                vel_map[eid].vx = 0
                vel_map[eid].vy = 0
            except Exception:
                world.components.setdefault('Velocity', {})[eid] = Velocity(0, 0)
        else:
            world.components.setdefault('Velocity', {})[eid] = Velocity(0, 0)
        # Mostrar sprite de muerte si existe y detener animación
        sprite = world.components.get('Sprite', {}).get(eid)
        if sprite and hasattr(sprite, 'death_image'):
            death_img = sprite.death_image
            try:
                sprite.image = death_img.copy()
            except Exception:
                sprite.image = death_img
            world.components.get('Animator', {}).pop(eid, None)
            world.components.get('AnimationTimer', {}).pop(eid, None)
        # Bajar la capa Z del cadáver para que drops pasen por encima
        world.components.setdefault('ZLayer', {})[eid] = ZLayer(Z_LAYERS.get('low_object', 2))
        # Atribuir KO al último atacante para el sistema de combos (si aplica)
        try:
            last_attacker = world.components.get('LastAttacker', {}).get(eid)
            if last_attacker is not None:
                attacker_eid = getattr(last_attacker, 'attacker_eid', None)
                if attacker_eid in world.components.get('PlayerTagComponent', {}):
                    counted = world.components.setdefault('ComboKillCounted', set())
                    if eid not in counted:
                        combo_q = world.components.setdefault('ComboEventQueue', [])
                        combo_q.append({'type': 'kill', 'entity': attacker_eid, 'target': eid, 'time': float(time.time())})
                        counted.add(eid)
        except Exception:
            # Nunca romper transición por contabilidad de combos
            pass

    def execute(self, entity, dt):
        world = entity.world
        eid = entity.id
        dt_cmp = world.components.get('DeathTimer', {}).get(eid)
        if not dt_cmp:
            # Si no hay temporizador por alguna razón, finalizar directamente
            from roguelike_game.ecs.systems.fsm.states.death_state import DeathState
            world.components['NPCState'][eid].fsm.change_state(DeathState(), entity)
            return
        now = time.time()
        elapsed = now - dt_cmp.start_time
        duration = dt_cmp.duration
        if elapsed >= duration:
            logger.debug(f"[Unconscious.execute] eid={eid}, elapsed={duration:.1f}/{duration:.1f}")
            from roguelike_game.ecs.systems.fsm.states.death_state import DeathState
            world.components['NPCState'][eid].fsm.change_state(DeathState(), entity)
            return

    def exit(self, entity):
        logger.debug(f"[Unconscious.exit] eid={entity.id}")
        # Remover DeathTimer al salir para evitar barras residuales
        try:
            world = entity.world
            world.components.get('DeathTimer', {}).pop(entity.id, None)
        except Exception:
            pass
