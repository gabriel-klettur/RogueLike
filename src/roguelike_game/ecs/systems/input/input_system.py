"""
Module: input_system.py
Sistema que traduce el estado de teclado a InputComponent y actualiza Velocity.
"""
import pygame
import logging

from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_game.ecs.systems.fsm.states.player.move_state import MoveState
from roguelike_game.ecs.systems.fsm.states.player.player_attack_state import PlayerAttackState
from roguelike_game.ecs.systems.fsm.states.player.player_spell_select_state import PlayerSpellSelectState
from roguelike_game.ecs.systems.fsm.states.unconscious_state import UnconsciousState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy
from roguelike_game.config.input_config import InputConfig
from roguelike_game.config.spells_config import reload_spells

from .constants import ACTION_BASES, SPELL_ATTRS
from .helpers import (
    block_reason,
    block_all_inputs_and_reset,
    reset_entity_inputs,
    set_velocity_from_input,
    map_keyboard_spells,
    process_spell_edges,
    compute_suppression_flags,
    log_suppression_transitions,
    handle_mouse_actions,
    rising_edge,
)

logger = logging.getLogger(__name__)

class InputSystem:
    """
    Captura el estado del teclado y actualiza InputComponent y Velocity.
    """
    def __init__(self, perf_log, config_path=None):
        self.perf_log = perf_log
        # Mapear estado previo de click izquierdo (para pickups) y botones de acciones mouse
        self.prev_click = {}  # left button state for inp.click semantics
        self.prev_right = {}  # legacy compatibility; no longer used for dash detection
        self.prev_mouse = {}  # (eid, action) -> bool for edge detection of mouse actions
        # Estado previo por slot de acciones con teclado (kb_a/kb_b)
        # keys: (eid, '<base>_kb_a'|'_kb_b') -> bool
        self.prev_action_slots = {}

        self.prev_toggle = {}
        self.prev_toggle_inventory = {}
        # Estado previo para interacción contextual (flanco ascendente)
        self.prev_interact = {}
        # Estado previo de hechizos para detección de flancos
        self.prev_spell_keys = {}
        # Estado previo de ataque para detección de flanco ascendente
        self.prev_attack = {}
        # Cargar configuración de teclas desde JSON
        self.config = InputConfig(config_path)
        # Edge detection for manual spells reload (F5)
        self._prev_reload_spells = False
        # Per-entity cache to avoid spamming suppression logs each frame
        self._prev_suppressed = {}
    
    def update(self, world, *args):
        # Suppress ALL gameplay input when a global UI requires focus
        reason = block_reason(world)
        if reason is not None:
            block_all_inputs_and_reset(self, world)
            return

        # Recargar bindings para aplicar cambios guardados sin reiniciar
        self.config._load()
        # Obtener estado actual del teclado
        keys = pygame.key.get_pressed()
        # Helper para evaluar múltiples teclas configuradas por acción
        def any_pressed(action: str) -> bool:
            try:
                codes = self.config.get_keys_for_action(action)
            except Exception:
                codes = []
            return any(bool(keys[c]) for c in codes if isinstance(c, int))

        # Manual reload of spells.json via F4
        reload_pressed = bool(keys[getattr(pygame, 'K_F4')])
        if reload_pressed and not self._prev_reload_spells:
            try:
                reload_spells()
                logger.info("[InputSystem] Manual reload of spells.json triggered (F4)")
            except Exception:
                logger.exception("[InputSystem] Failed to reload spells on F4")
        self._prev_reload_spells = reload_pressed

        # Leer bindings dinámicos (multi-binding via OR)
        # Movimiento y acciones simples se evaluarán con any_pressed dentro del bucle por entidad

        # Flags: Editors activos -> suprimir acciones de combate
        editor_buildings_active = bool(getattr(getattr(world, 'state', None), 'buildings_editor_active', False))
        particles_editor_visible = bool(getattr(getattr(world, 'state', None), 'particles_editor_visible', False))

        # Para cada entidad con InputComponent
        # Flag para mostrar todos los drops
        alt_down = bool(pygame.key.get_mods() & pygame.KMOD_ALT)
        for eid, inp in world.components.get('InputComponent', {}).items():
            # 0) Si el jugador está inconsciente: bloquear completamente entradas y movimiento
            if eid in world.components.get('PlayerTagComponent', {}):
                state_comp = world.components.get('NPCState', {}).get(eid)
                if state_comp and isinstance(state_comp.fsm.current_state, UnconsciousState):
                    # Limpiar inputs actuales
                    reset_entity_inputs(self, world, eid, inp)
                    # Cancelar acciones pendientes (hechizos/dash) del frame
                    try:
                        world.components.get('WantsToCastSpell', {}).pop(eid, None)
                    except Exception:
                        pass
                    # Saltar procesamiento de esta entidad en este frame
                    continue
            # Asignar flag show_all_drops
            inp.show_all_drops = alt_down
            # Movimiento en ejes X e Y (soporta múltiples teclas por acción)
            right_down = any_pressed("move_right")
            left_down = any_pressed("move_left")
            down_down = any_pressed("move_down")
            up_down = any_pressed("move_up")
            inp.move_x = int(right_down) - int(left_down)
            inp.move_y = int(down_down) - int(up_down)
            # Ataque físico (soporta múltiples teclas por acción)
            curr_attack = any_pressed("attack")

            inp.attack = curr_attack
            # Actualizar velocidad según MovementSpeed
            vel = world.components.get('Velocity', {}).get(eid)
            ms = world.components.get('MovementSpeed', {}).get(eid)
            set_velocity_from_input(vel, ms, inp.move_x, inp.move_y)

            # Integración FSM para Player
            # Detectar Player usando componente PlayerTagComponent
            if eid in world.components.get('PlayerTagComponent', {}):
                state_comp = world.components.get('NPCState', {}).get(eid)
                if state_comp:
                    current = state_comp.fsm.current_state
                    proxy = _EntityProxy(world, eid)
                    # Movimiento
                    if (inp.move_x != 0 or inp.move_y != 0) and isinstance(current, IdleState):
                        state_comp.fsm.change_state(MoveState(), proxy)
                    elif inp.move_x == 0 and inp.move_y == 0 and isinstance(current, MoveState):
                        state_comp.fsm.change_state(IdleState(), proxy)
                    if not editor_buildings_active and not particles_editor_visible:
                        # Ataque físico: sólo en flanco ascendente y evitar re-entrada
                        prev_a = self.prev_attack.get(eid, False)
                        if curr_attack and not prev_a:
                            if (not isinstance(current, PlayerAttackState)) and (isinstance(current, IdleState) or isinstance(current, MoveState)):
                                state_comp.fsm.change_state(PlayerAttackState(), proxy)
                        # Hechizos (q/e): entrar a selección SOLO en flanco ascendente y si está en Idle/Move
                        pressed_lb = any_pressed("spell_lightball") and self.prev_spell_keys.get((eid, 'lightball'), 0) == 0
                        pressed_sl = any_pressed("spell_slash") and self.prev_spell_keys.get((eid, 'slash'), 0) == 0

                        if (pressed_lb or pressed_sl) and (isinstance(current, IdleState) or isinstance(current, MoveState)):
                            state_comp.fsm.change_state(PlayerSpellSelectState(), proxy)
                        # Actualizar memoria de flanco para ataque
                        self.prev_attack[eid] = curr_attack
                    else:
                        # En editor: desactivar ataque y limpiar memoria para evitar flancos al salir
                        inp.attack = False
                        self.prev_attack[eid] = False

            # Mapear habilidades Q, E, X desde config
            if editor_buildings_active or particles_editor_visible:
                # En editor: desactivar todos los hechizos y resetear flancos
                for name in SPELL_ATTRS:
                    setattr(inp, f'spell_{name}', False)
                    self.prev_spell_keys[(eid, name)] = 0
            else:
                # Mapear hechizos desde teclado (OR de múltiples bindings)
                map_keyboard_spells(inp, any_pressed)
                # Detección de eventos de hechizos: presionado, mantenido y soltado
                process_spell_edges(self, world, eid, inp)

            # Toggle editor mode: detectar flanco ascendente (OR de múltiples teclas)
            curr_toggle = any_pressed("toggle_item_editor")
            inp.toggle_editor = rising_edge(self, eid, self.prev_toggle, curr_toggle)

            # Toggle inventory display: detectar flanco ascendente (OR de múltiples teclas)
            curr_inv = any_pressed("toggle_inventory")
            inp.toggle_inventory = rising_edge(self, eid, self.prev_toggle_inventory, curr_inv)

            # Interact action: rising-edge detection (OR of multiple bindings)
            curr_interact = any_pressed("interact")
            inp.interact = rising_edge(self, eid, self.prev_interact, curr_interact)

            # Control de click: desactivar cuando se arrastra un ítem o el panel de inventario
            suppressed_now, details = compute_suppression_flags(self, world)
            log_suppression_transitions(self, eid, suppressed_now, details)

            if suppressed_now:
                inp.click = False
                self.prev_click[eid] = False
                self.prev_right[eid] = False
                inp.interact = False
                self.prev_interact[eid] = False
                # Reset mouse action edges
                self.prev_mouse[(eid, 'fireball')] = False
                self.prev_mouse[(eid, 'dash')] = False
                # Resetear memorias de ratón de hechizos mapeados por mouse (p.ej. slash)
                self.prev_mouse[(eid, 'spell_slash')] = False
                # Reset keyboard slot edges
                for base in ACTION_BASES:
                    self.prev_action_slots[(eid, f'{base}_kb_a')] = False
                    self.prev_action_slots[(eid, f'{base}_kb_b')] = False
            else:
                # Actualizar estado del mouse y acciones configurables
                mx, my = pygame.mouse.get_pos()
                handle_mouse_actions(self, world, eid, keys, mx, my)