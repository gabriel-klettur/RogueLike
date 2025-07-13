"""
Sistema ECS que detecta componentes 'WantsToCastSpell' y, según corresponda,
arranca la máquina de estados de hechizos (CastState y subestados) para NPCs
y jugadores.
"""
# Path: src/roguelike_game/ecs/systems/combat/spells/spell_casting_system.py
from roguelike_game.ecs.systems.fsm.states.cast_state import CastState
from roguelike_game.ecs.systems.fsm.states.player.player_spell_cast_state import PlayerSpellCastState
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_game.ecs.systems.fsm.states.player.move_state import MoveState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy

from roguelike_engine.utils.benchmark import benchmark
import math
from roguelike_game.config.spells_config import SPELLS
import pygame

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

    @benchmark(lambda self: self.perf_log, "4.2.2.SpellCastingSystem.update")
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

        #print(f"[SpellCastingSystem] Inicio update: {len(wants)} intenciones detectadas.")

        # Iterar sobre una copia de las llaves, porque vamos a eliminar intenciones mientras iteramos
        for eid in list(wants.keys()):
            intent = wants[eid]
            # Validar max_instances y allow_overlap segun spells.json
            cfg = SPELLS.get(intent.spell, {})
            spell_type = cfg.get('type')
            max_inst = cfg.get('max_instances', 0)
            # Contar instancias activas
            if spell_type == 'projectile':
                active = sum(1 for comp in world.components.get('FireballComponent', {}).values()
                             if getattr(comp, 'spell_key', '') == intent.spell)
            elif spell_type == 'aura':
                active = len(world.components.get('AuraComponent', {}))
            elif spell_type == 'beam':
                active = len(world.components.get('LaserBeamComponent', {}))
            elif spell_type == 'dash':
                active = len(world.components.get('DashComponent', {}))
            elif spell_type == 'slash':
                active = sum(1 for comp in world.components.get('HitboxComponent', {}).values()
                             if getattr(comp, 'owner', None) == eid)
            else:
                active = 0
            if max_inst and active >= max_inst:
                continue
            allow_overlap = cfg.get('allow_overlap', True)
            if not allow_overlap and active > 0:
                continue
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
                    if camera:
                        mx, my = pygame.mouse.get_pos()
                        world_x = mx / camera.zoom + camera.offset_x
                        world_y = my / camera.zoom + camera.offset_y
                    else:
                        world_x, world_y = mx, my
                    dx, dy = world_x - spawn_x, world_y - spawn_y
                    length = math.hypot(dx, dy) or 1
                    new_state.spell_fsm.context['direction'] = (dx/length, dy/length)
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
                print(f"[SpellCastingSystem] Entidad {eid} inicia hechizo '{intent.spell}' via FSM.")
                npc_state.fsm.change_state(new_state, proxy)
            # Limpiar intención
            wants.pop(eid, None)
            print(f"[SpellCastingSystem] Intención de hechizo de entidad {eid} eliminada.\n")

        # Nota: pulse la FSM de hechizo (prepare/channel/release/cooldown) en CastState y subestados