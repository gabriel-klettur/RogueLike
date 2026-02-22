"""
Sistema ECS que detecta componentes 'WantsToCastSpell' y, según corresponda,
arranca la máquina de estados de hechizos (CastState y subestados) para NPCs
y jugadores.
"""
from roguelike_game.ecs.systems.fsm.states.cast_state import CastState
from roguelike_game.ecs.systems.fsm.states.player.player_spell_cast_state import PlayerSpellCastState
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_game.ecs.systems.fsm.states.player.move_state import MoveState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy

from roguelike_engine.utils.benchmark.benchmark import benchmark
import math
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale
import pygame

import logging
logger = logging.getLogger(__name__)

class SpellCastingSystem:
    """
    Sistema que procesa intenciones de hechizo registradas en el ECS:
      • Si la intención pertenece a un NPC (está en 'NPCState'), inicia su sub-FSM de hechizos
        cambiando su estado a CastState.
      • Si pertenece al jugador, genera inmediatamente una fireball dirigida a la posición
        del ratón, sin pasar por sub-FSM.
    """

    def __init__(self, perf_log=None):
        """
        Args:
            perf_log: objeto de logging o benchmarking (opcional), usado por el decorador @benchmark.
        """
        self.perf_log = perf_log

    # Mapping for simple type -> component counting
    _TYPE_TO_COMPONENT = {
        'aura': 'AuraComponent',
        'beam': 'LaserBeamComponent',
        'dash': 'DashComponent',
        'lightning': 'LightningComponent',
        'arcane_flame': 'ArcaneFlameComponent',
        'firework_launch': 'FireworkLaunchComponent',
        'smoke': 'SmokeComponent',
        'smoke_emitter': 'SmokeEmitterComponent',
        'sphere_magic_shield': 'SphereMagicShieldComponent',
        'teleport': 'TeleportComponent',
        'mine': 'MineComponent',
        'boomerang': 'BoomerangComponent',
        'chain_lightning': 'ChainLightningComponent',
        'totem': 'TotemComponent',
        'summon': 'SummonedUnitComponent',
        'wall': 'WallSegmentComponent',
        # New spell types counting
        'cone_breath': 'ConeBreathComponent',
        'puddle': 'PuddleComponent',
        'meteor_shower': 'MeteorShowerComponent',
        'vortex_field': 'ForceFieldComponent',
    }

    def _count_active(self, world, eid, intent, spell_type: str) -> int:
        """Count active instances for the given spell type, preserving current semantics.

        Special cases:
        - projectile: count FireballComponent filtered by spell_key
        - slash: count HitboxComponent owned by the caster
        Others: count all components of mapped type
        """
        if spell_type == 'projectile':
            return sum(1 for comp in world.components.get('FireballComponent', {}).values()
                       if getattr(comp, 'spell_key', '') == intent.spell)
        if spell_type == 'slash':
            return sum(1 for comp in world.components.get('HitboxComponent', {}).values()
                       if getattr(comp, 'owner', None) == eid)
        # Walls: consider one active instance per caster+spell if any segment exists
        if spell_type == 'wall':
            segs = world.components.get('WallSegmentComponent', {})
            if not segs:
                return 0
            try:
                s_key = getattr(intent, 'spell', '')
            except Exception:
                s_key = ''
            for _wid, w in list(segs.items()):
                if getattr(w, 'owner', None) == eid and (not s_key or getattr(w, 'spell_key', '') == s_key):
                    return 1
            return 0
        # Per-caster counting for dash: component vive en el caster
        if spell_type == 'dash':
            return 1 if eid in world.components.get('DashComponent', {}) else 0
        comp_key = self._TYPE_TO_COMPONENT.get(spell_type)
        if comp_key:
            return len(world.components.get(comp_key, {}))
        return 0

    def update(self, world, camera=None):
        """Procesa 'WantsToCastSpell' y encola la sub-FSM adecuada.
        Para NPCs, inyecta intent.meta (incluido 'spawn_pos') en el contexto para que los resolvers lo usen.
        """
        wants = world.components.get('WantsToCastSpell', {})
        npcs = world.components.get('NPCState', {})
        player_eid = getattr(world, 'player_entity', None)

        for eid in list(wants.keys()):
            intent = wants.get(eid)
            if intent is None:
                continue
            cfg = SPELLS.get(intent.spell, {})
            spell_type = cfg.get('type')

            # No interrumpir un cast no-interruptible en curso (p. ej., root_whip)
            try:
                npc_state = world.components.get('NPCState', {}).get(eid)
                if npc_state is not None:
                    # Bloquear si está en canalización activa de un hechizo no-interrumpible (AutoCast)
                    ac = world.components.get('AutoCastComponent', {}).get(eid)
                    chan = getattr(ac, 'active_channel', None) if ac is not None else None
                    if isinstance(chan, dict):
                        ch_spell = str(chan.get('spell', ''))
                        if ch_spell:
                            ch_cfg = SPELLS.get(ch_spell, {})
                            if not bool(ch_cfg.get('interruptible', False)):
                                continue
                    cur_state = getattr(npc_state, 'fsm', None)
                    # Evitar preempt si el nuevo hechizo es 'non_preemptive' y ya se está casteando algo
                    try:
                        if bool(cfg.get('non_preemptive', False)):
                            if isinstance(getattr(cur_state, 'current_state', None), CastState):
                                # Mantener wants[eid] para que se procese cuando termine el cast actual
                                continue
                    except Exception:
                        pass
                    cur_state = getattr(cur_state, 'current_state', None)
                    if isinstance(cur_state, CastState):
                        cur_ctx = getattr(cur_state, 'spell_fsm', None)
                        cur_ctx = getattr(cur_ctx, 'context', {}) if cur_ctx is not None else {}
                        cur_spell = cur_ctx.get('spell')
                        if cur_spell:
                            cur_cfg = SPELLS.get(cur_spell, {})
                            # Si el hechizo actual NO es interruptible, saltar este intent y dejarlo pendiente
                            if not bool(cur_cfg.get('interruptible', False)):
                                # Mantener wants[eid] para que se procese cuando termine el cast actual
                                continue
            except Exception:
                pass

            # Limites de instancias / overlap
            try:
                active = self._count_active(world, eid, intent, spell_type)
                max_inst = int(cfg.get('max_instances', 0) or 0)
                if max_inst and active >= max_inst:
                    wants.pop(eid, None)
                    continue
                if not bool(cfg.get('allow_overlap', True)) and active > 0:
                    wants.pop(eid, None)
                    continue
            except Exception:
                pass

            # Lanzar FSM
            if eid in npcs:
                proxy = _EntityProxy(world, eid)
                if eid == player_eid:
                    new_state = PlayerSpellCastState()
                    # Dirección basada en ratón (mantiene comportamiento previo)
                    pos_cmp = world.components['Position'][eid]
                    spr = world.components.get('Sprite', {}).get(eid)
                    if spr:
                        w, h = spr.image.get_size()
                        sx, sy = pos_cmp.x + w/2, pos_cmp.y + h/2
                    else:
                        sx, sy = pos_cmp.x, pos_cmp.y
                    if camera:
                        mx, my = pygame.mouse.get_pos()
                        wx = mx / camera.zoom + camera.offset_x
                        wy = my / camera.zoom + camera.offset_y
                    else:
                        mx, my = pygame.mouse.get_pos()
                        wx, wy = float(mx), float(my)
                    dx, dy = wx - sx, wy - sy
                    length = math.hypot(dx, dy) or 1.0
                    new_state.spell_fsm.context['direction'] = (dx/length, dy/length)
                    new_state.spell_fsm.context['spawn_pos'] = (sx, sy)
                    new_state.spell_fsm.context['camera'] = camera
                    new_state.spell_fsm.context['spell'] = intent.spell
                    new_state.spell_fsm.context['automatic'] = cfg.get('automatic', False)
                    new_state.spell_fsm.context['automatic_cast_punish'] = cfg.get('automatic_cast_punish', 1.0)
                else:
                    new_state = CastState()
                    new_state.spell_fsm.context['spell'] = intent.spell
                    new_state.spell_fsm.context['automatic'] = cfg.get('automatic', False)
                    new_state.spell_fsm.context['automatic_cast_punish'] = cfg.get('automatic_cast_punish', 1.0)
                    new_state.spell_fsm.context['camera'] = camera
                    # Inyectar meta (incluye spawn_pos desde AutoCastSystem)
                    try:
                        meta = getattr(intent, 'meta', None)
                        if isinstance(meta, dict):
                            for k, v in meta.items():
                                new_state.spell_fsm.context[k] = v
                    except Exception:
                        pass
                    # Si viene spawn_pos pero no direction:
                    # - Para proyectiles: calcular dirección hacia el Player
                    # - Para otros: usar (1, 0) como neutra para evitar overrides
                    try:
                        if ('spawn_pos' in new_state.spell_fsm.context) and ('direction' not in new_state.spell_fsm.context):
                            if cfg.get('type') == 'projectile':
                                # Calcular vector caster -> player (centros)
                                pos_map = world.components.get('Position', {})
                                spr_map = world.components.get('Sprite', {})
                                scl_map = world.components.get('Scale', {})
                                cpos = pos_map.get(eid)
                                ppos = pos_map.get(player_eid) if player_eid is not None else None
                                if (cpos is not None) and (ppos is not None):
                                    # Centro caster
                                    cspr = spr_map.get(eid)
                                    if cspr:
                                        w, h = cspr.image.get_size()
                                        cx, cy = float(cpos.x) + w/2, float(cpos.y) + h/2
                                    else:
                                        cx, cy = float(cpos.x), float(cpos.y)
                                    # Centro player
                                    pspr = spr_map.get(player_eid)
                                    if pspr:
                                        w2, h2 = pspr.image.get_size()
                                        px, py = float(ppos.x) + w2/2, float(ppos.y) + h2/2
                                    else:
                                        px, py = float(ppos.x), float(ppos.y)
                                    dx, dy = px - cx, py - cy
                                    length = math.hypot(dx, dy) or 1.0
                                    new_state.spell_fsm.context['direction'] = (dx/length, dy/length)
                                    # Asegurar que ReleaseSpellState no recalcula con ratón
                                    new_state.spell_fsm.context['force_lock_direction'] = True
                                else:
                                    new_state.spell_fsm.context['direction'] = (1.0, 0.0)
                                    new_state.spell_fsm.context['force_lock_direction'] = True
                            else:
                                new_state.spell_fsm.context['direction'] = (1.0, 0.0)
                                new_state.spell_fsm.context['force_lock_direction'] = True
                    except Exception:
                        pass
                    # Fallback adicional: si es proyectil y aún no hay direction, calcular hacia el Player
                    try:
                        if (cfg.get('type') == 'projectile') and ('direction' not in new_state.spell_fsm.context):
                            pos_map = world.components.get('Position', {})
                            spr_map = world.components.get('Sprite', {})
                            cpos = pos_map.get(eid)
                            ppos = pos_map.get(player_eid) if player_eid is not None else None
                            if (cpos is not None) and (ppos is not None):
                                cspr = spr_map.get(eid)
                                if cspr:
                                    w, h = cspr.image.get_size()
                                    cx, cy = float(cpos.x) + w/2, float(cpos.y) + h/2
                                else:
                                    cx, cy = float(cpos.x), float(cpos.y)
                                pspr = spr_map.get(player_eid)
                                if pspr:
                                    w2, h2 = pspr.image.get_size()
                                    px, py = float(ppos.x) + w2/2, float(ppos.y) + h2/2
                                else:
                                    px, py = float(ppos.x), float(ppos.y)
                                dx, dy = px - cx, py - cy
                                length = math.hypot(dx, dy) or 1.0
                                new_state.spell_fsm.context['direction'] = (dx/length, dy/length)
                                new_state.spell_fsm.context['force_lock_direction'] = True
                    except Exception:
                        pass
                    # Fallback robusto: si es un puddle (o root_whip) y no hay spawn_pos, usar centro del Player
                    try:
                        has_spawn = isinstance(new_state.spell_fsm.context.get('spawn_pos'), (tuple, list)) and len(new_state.spell_fsm.context.get('spawn_pos')) == 2
                    except Exception:
                        has_spawn = False
                    if (not has_spawn) and (cfg.get('type') == 'puddle' or intent.spell == 'root_whip'):
                        try:
                            peid = player_eid
                            if peid is not None:
                                pos_map = world.components.get('Position', {})
                                spr_map = world.components.get('Sprite', {})
                                scl_map = world.components.get('Scale', {})
                                ppos = pos_map.get(peid)
                                if ppos is not None:
                                    pspr = spr_map.get(peid)
                                    pscl = scl_map.get(peid)
                                    if pspr is not None:
                                        from roguelike_game.ecs.utils.position_utils import compute_entity_center
                                        cen = compute_entity_center(ppos, pspr, pscl)
                                        new_state.spell_fsm.context['spawn_pos'] = (float(cen.x), float(cen.y))
                                    else:
                                        new_state.spell_fsm.context['spawn_pos'] = (float(ppos.x), float(ppos.y))
                        except Exception:
                            pass
                npcs[eid].fsm.change_state(new_state, proxy)
            # Consumir intención y continuar
            wants.pop(eid, None)
            continue