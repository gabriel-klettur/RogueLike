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

from roguelike_engine.utils.benchmark import benchmark
import math
from roguelike_game.config.spells_config import SPELLS
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
        # Per-caster counting for dash: component vive en el caster
        if spell_type == 'dash':
            return 1 if eid in world.components.get('DashComponent', {}) else 0
        comp_key = self._TYPE_TO_COMPONENT.get(spell_type)
        if comp_key:
            return len(world.components.get(comp_key, {}))
        return 0
    
    def update(self, world, camera=None):
        """
        Recorre todas las entidades que tengan el componente 'WantsToCastSpell'.

        - 'wants' es el diccionario de intenciones de hechizo:
          key: entity_id, value: instancia de WantsToCastSpell
        - 'npcs' es el diccionario de componentes NPCState, se usa para distinguir NPCs de jugador.

        Args:
            world: instancia de ECSWorld, contenedor de entidades y componentes.
            camera: objeto de cámara, usado para convertir coordenadas de pantalla a mundo.
        """
        # Obtener diccionario actual de intenciones de hechizo
        wants = world.components.get('WantsToCastSpell', {})
        npcs = world.components.get('NPCState', {})

        #logger.debug(f" Inicio update: {len(wants)} intenciones detectadas.")

        # Iterar sobre una copia de las llaves, porque vamos a eliminar intenciones mientras iteramos
        for eid in list(wants.keys()):
            intent = wants[eid]
            # Validar max_instances y allow_overlap segun spells.json
            cfg = SPELLS.get(intent.spell, {})
            spell_type = cfg.get('type')
            max_inst = cfg.get('max_instances', 0)
            # Contar instancias activas (centralizado)
            active = self._count_active(world, eid, intent, spell_type)

            if max_inst and active >= max_inst:
                continue
            allow_overlap = cfg.get('allow_overlap', True)
            if not allow_overlap and active > 0:
                continue
            # Verificar coste de maná (si el caster tiene maná)
            try:
                godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and (eid == getattr(world, 'player_entity', None))
                if not godmode:
                    mana_comp = world.components.get('Mana', {}).get(eid)
                    mana_cost = float(getattr(cfg, 'mana_cost', cfg.get('mana_cost', 0)))
                    if mana_comp is not None and mana_cost > 0:
                        if float(mana_comp.current_mana) < mana_cost:
                            # Notificar falta de maná al jugador mediante burbuja
                            try:
                                from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble
                                push_bubble(world, eid, 'No tengo suficiente maná', color=(240, 200, 200), ttl_ms=1800)
                            except Exception:
                                pass
                            # Disparar flash azul en la barra de maná durante 600ms
                            try:
                                import time as _time
                                flash_store = getattr(world, '_mana_flash_until', None)
                                if not isinstance(flash_store, dict):
                                    flash_store = {}
                                    setattr(world, '_mana_flash_until', flash_store)
                                flash_store[eid] = _time.time() + 0.6
                            except Exception:
                                pass
                            # Descartar intención y continuar
                            wants.pop(eid, None)
                            continue
            except Exception:
                # fallback duro: no bloquear por errores de lectura
                pass

            # Permitir castear solo si Idle o (allow_movement y MoveState)
            state_comp = npcs.get(eid)
            allow_mov = cfg.get('allow_movement', False)
            if state_comp:
                current = state_comp.fsm.current_state
                # salto si no está en IdleState o MoveState con permiso
                if not (isinstance(current, IdleState) or (allow_mov and isinstance(current, MoveState))):
                    # en medio de cast; verificar interruptibilidad
                    if not cfg.get('interruptible', False):
                        # descartar intención si no es interruptible
                        wants.pop(eid, None)
                        continue
            # Si tiene FSM global (NPC o jugador), iniciar sub-FSM de hechizo
            if eid in npcs:
                npc_state = npcs[eid]
                proxy = _EntityProxy(world, eid)
                # Elegir estado inicial según tipo de entidad
                if eid == world.player_entity:
                    new_state = PlayerSpellCastState()
                    # Compute direction and spawn center
                    pos_cmp = world.components['Position'][eid]
                    sprite_cmp = world.components.get('Sprite', {}).get(eid)
                    if sprite_cmp:
                        w, h = sprite_cmp.image.get_size()
                        spawn_x, spawn_y = pos_cmp.x + w/2, pos_cmp.y + h/2
                    else:
                        spawn_x, spawn_y = pos_cmp.x, pos_cmp.y
                    # Dirección inicial: preferir stick si es fuente dominante
                    inp = world.components.get('InputComponent', {}).get(eid)
                    dir_x, dir_y = 0.0, 0.0
                    if inp and getattr(inp, 'aim_source', 'mouse') == 'stick':
                        dir_x = float(getattr(inp, 'aim_dir_x', 0.0) or 0.0)
                        dir_y = float(getattr(inp, 'aim_dir_y', 0.0) or 0.0)
                    if dir_x == 0.0 and dir_y == 0.0:
                        if camera:
                            mx, my = pygame.mouse.get_pos()
                            world_x = mx / camera.zoom + camera.offset_x
                            world_y = my / camera.zoom + camera.offset_y
                        else:
                            world_x, world_y = mx, my
                        dx, dy = world_x - spawn_x, world_y - spawn_y
                        length = math.hypot(dx, dy) or 1
                        dir_x, dir_y = dx/length, dy/length
                    new_state.spell_fsm.context['direction'] = (dir_x, dir_y)
                    new_state.spell_fsm.context['spawn_pos'] = (spawn_x, spawn_y)
                    # Guardar camera y spell para recalcular aiming dinámico
                    new_state.spell_fsm.context['camera'] = camera
                    new_state.spell_fsm.context['spell'] = intent.spell
                    new_state.spell_fsm.context['automatic'] = cfg.get('automatic', False)
                    new_state.spell_fsm.context['automatic_cast_punish'] = cfg.get('automatic_cast_punish', 1.0)
                else:
                    new_state = CastState()
                    new_state.spell_fsm.context['spell'] = intent.spell
                    new_state.spell_fsm.context['automatic'] = cfg.get('automatic', False)
                    new_state.spell_fsm.context['automatic_cast_punish'] = cfg.get('automatic_cast_punish', 1.0)
                logger.debug(f" Entidad {eid} inicia hechizo '{intent.spell}' via FSM.")
                npc_state.fsm.change_state(new_state, proxy)
            # Limpiar intención
            wants.pop(eid, None)
            logger.debug(f" Intención de hechizo de entidad {eid} eliminada.\n")

        # Nota: pulse la FSM de hechizo (prepare/channel/release/cooldown) en CastState y subestados