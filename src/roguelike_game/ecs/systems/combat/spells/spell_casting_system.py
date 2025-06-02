"""
Sistema ECS que detecta componentes 'WantsToCastSpell' y, según corresponda,
arranca la máquina de estados de hechizos (CastState y subestados) para NPCs
y jugadores.
"""
from roguelike_game.ecs.fsm.states.cast_state import CastState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy
from roguelike_game.ecs.fsm.states.aggro_state import AggroState
from roguelike_game.ecs.components.fsm.npc_state import NPCState
from roguelike_game.ecs.fsm.fsm import FiniteStateMachine
import pygame

from roguelike_engine.utils.benchmark import benchmark


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
            # Ignorar intents mientras estemos en cualquier estado de casting/cooldown
            state = npcs.get(eid)
            if state and not isinstance(state.fsm.current_state, AggroState):
                # Consumir intención mientras no estemos en AggroState
                wants.pop(eid, None)
                continue
            intent = wants[eid]
            print(f"\n[SpellCastingSystem] Procesando entidad {eid} con intención de hechizo '{intent.spell}'.")

            # 1) Si la entidad con intención está en 'NPCState' y NO es jugador, se trata de un NPC
            if eid in npcs and eid != world.player_entity:
                print(f"[SpellCastingSystem] Entidad {eid} es NPC. Iniciando sub-FSM de hechizo.")
                # Obtener el componente NPCState (que contiene la FSM)
                npc_state = npcs[eid]
                entity_proxy = _EntityProxy(world, eid)
                # Preparar nuevo estado de hechizo con contexto en sub-FSM
                new_state = CastState()
                new_state.spell_fsm.context['spell'] = intent.spell
                print(f"[SpellCastingSystem] NPC {eid} switch FSM a CastState con hechizo '{intent.spell}'.")
                npc_state.fsm.change_state(new_state, entity_proxy)

            else:
                # 2) Si la entidad NO está en NPCState, asumimos que es el jugador y usamos FSM
                print(f"[SpellCastingSystem] Entidad {eid} NO es NPC. Se asume jugador. Iniciando sub-FSM de hechizo.")
                entity_proxy = _EntityProxy(world, eid)
                # Crear FSM para jugador si no existe
                if eid not in npcs:
                    fsm = FiniteStateMachine(CastState())
                    npcs[eid] = NPCState(fsm, 'CastState')
                    print(f"[SpellCastingSystem] NPCState creado para jugador {eid}")
                npc_state = npcs[eid]
                # Preparar nuevo estado de hechizo con contexto en sub-FSM
                new_state = CastState()
                # Calcular posición central del jugador
                pos_cmp = world.components['Position'][eid]
                center_x = pos_cmp.x
                center_y = pos_cmp.y
                sprite_cmp = world.components['Sprite'].get(eid)
                if sprite_cmp:
                    w, h = sprite_cmp.image.get_size()
                    center_x += w / 2
                    center_y += h / 2
                # Pantalla → mundo para el mouse
                mx, my = pygame.mouse.get_pos()
                wx = mx / camera.zoom + camera.offset_x
                wy = my / camera.zoom + camera.offset_y
                # Vector dirección desde el centro del jugador
                dx = wx - center_x
                dy = wy - center_y
                length = (dx*dx + dy*dy) ** 0.5 or 1
                dir_x, dir_y = dx / length, dy / length
                # Desplazar spawn un poco adelante del jugador
                offset = (max(w, h) / 2) if sprite_cmp else 0
                spawn_x = center_x + dir_x * offset
                spawn_y = center_y + dir_y * offset
                # Guardar en contexto de la sub-FSM
                ctx = new_state.spell_fsm.context
                ctx['direction'] = (dir_x, dir_y)
                ctx['spawn_pos'] = (spawn_x, spawn_y)
                new_state.spell_fsm.context['spell'] = intent.spell
                print(f"[SpellCastingSystem] Jugador {eid} switch FSM a CastState con hechizo '{intent.spell}'.")
                npc_state.fsm.change_state(new_state, entity_proxy)

            # 3) Una vez procesada la intención (NPC o jugador), la removemos del diccionario
            wants.pop(eid, None)
            print(f"[SpellCastingSystem] Intención de hechizo de entidad {eid} eliminada.\n")
