from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.combat.death_timer import DeathTimer
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_engine.config.config_z_layer import Z_LAYERS
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.utils.health_utils import resolve_death_duration

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
        # Determinar duración del temporizador de inconsciencia/desaparición (helper unificado)
        duration = resolve_death_duration(world, eid)
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
