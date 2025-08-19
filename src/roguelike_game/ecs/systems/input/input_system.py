"""
Module: input_system.py
Sistema que traduce el estado de teclado a InputComponent y actualiza Velocity.
"""
import pygame
import time
import math
from roguelike_game.ecs.systems.inventory.drop_drag_system import DropDragSystem
from roguelike_game.ecs.systems.inventory.inventory_ui_system import InventoryUISystem
from roguelike_ui.ui_blocker import is_blocked

from roguelike_game.ecs.components.ai.wants_to_cast import WantsToCastSpell
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_game.ecs.systems.fsm.states.player.move_state import MoveState
from roguelike_game.ecs.systems.fsm.states.player.player_attack_state import PlayerAttackState
from roguelike_game.ecs.systems.fsm.states.player.player_spell_select_state import PlayerSpellSelectState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy
from roguelike_game.config.input_config import InputConfig
from roguelike_game.config.spells_config import reload_spells

import logging
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

    @benchmark(lambda self: self.perf_log, "4.2.2.InputSystem.update")
    def update(self, world, *args):
        # Suppress game clicks when Item Editor is open
        # world.state.item_editor_state is set by ItemsEditorManager
        if hasattr(world, 'state') and getattr(world.state, 'item_editor_state', None) and world.state.item_editor_state.visible:
            # Reset inputs and velocities to fully freeze gameplay while editor is open
            spell_attrs = ['lightball','slash','healing_aura','darkball','iceball','lightning','arcane_flame','firework_launch','smoke','smoke_emitter','sphere_magic_shield','teleport']
            for eid, inp in world.components.get('InputComponent', {}).items():
                # Current-frame inputs
                inp.click = False
                inp.move_x = 0
                inp.move_y = 0
                inp.attack = False
                inp.show_all_drops = False
                for name in spell_attrs:
                    setattr(inp, f'spell_{name}', False)
                inp.toggle_editor = False
                inp.toggle_inventory = False
                # Zero velocity so the player doesn't drift while paused
                vel = world.components.get('Velocity', {}).get(eid)
                if vel:
                    vel.vx = 0
                    vel.vy = 0
                # Clear edge-detection memory to avoid firing on resume
                self.prev_click[eid] = False
                self.prev_right[eid] = False
                self.prev_mouse[(eid, 'fireball')] = False
                self.prev_mouse[(eid, 'dash')] = False
                self.prev_toggle[eid] = False
                self.prev_toggle_inventory[eid] = False
                for name in spell_attrs:
                    self.prev_spell_keys[(eid, name)] = 0
                # Reset attack edge state
                self.prev_attack[eid] = False
                # Reset tri-slot keyboard edges to avoid firing on resume
                for base in ('fireball','laser_beam','dash'):
                    self.prev_action_slots[(eid, f'{base}_kb_a')] = False
                    self.prev_action_slots[(eid, f'{base}_kb_b')] = False
            return

        # Suppress ALL gameplay input when Class Selector is open
        if hasattr(world, 'state') and bool(getattr(world.state, 'class_selector_open', False)):
            spell_attrs = ['lightball','slash','healing_aura','darkball','iceball','lightning','arcane_flame','firework_launch','smoke','smoke_emitter','sphere_magic_shield','teleport']
            for eid, inp in world.components.get('InputComponent', {}).items():
                # Current-frame inputs
                inp.click = False
                inp.move_x = 0
                inp.move_y = 0
                inp.attack = False
                inp.show_all_drops = False
                for name in spell_attrs:
                    setattr(inp, f'spell_{name}', False)
                inp.toggle_editor = False
                inp.toggle_inventory = False
                # Zero velocity so the player doesn't drift while selector is open
                vel = world.components.get('Velocity', {}).get(eid)
                if vel:
                    vel.vx = 0
                    vel.vy = 0
                # Clear edge-detection memory to avoid firing on resume
                self.prev_click[eid] = False
                self.prev_right[eid] = False
                self.prev_mouse[(eid, 'fireball')] = False
                self.prev_mouse[(eid, 'dash')] = False
                self.prev_toggle[eid] = False
                self.prev_toggle_inventory[eid] = False
                for name in spell_attrs:
                    self.prev_spell_keys[(eid, name)] = 0
                # Reset attack edge state
                self.prev_attack[eid] = False
            return

        # Suppress ALL gameplay input when Spawner Editor is active
        if hasattr(world, 'state') and bool(getattr(world.state, 'spawner_editor_active', False)):
            spell_attrs = ['lightball','slash','healing_aura','darkball','iceball','lightning','arcane_flame','firework_launch','smoke','smoke_emitter','sphere_magic_shield','teleport']
            for eid, inp in world.components.get('InputComponent', {}).items():
                inp.click = False
                inp.move_x = 0
                inp.move_y = 0
                inp.attack = False
                inp.show_all_drops = False
                for name in spell_attrs:
                    setattr(inp, f'spell_{name}', False)
                inp.toggle_editor = False
                inp.toggle_inventory = False
                vel = world.components.get('Velocity', {}).get(eid)
                if vel:
                    vel.vx = 0
                    vel.vy = 0
                self.prev_click[eid] = False
                self.prev_right[eid] = False
                self.prev_mouse[(eid, 'fireball')] = False
                self.prev_mouse[(eid, 'dash')] = False
                self.prev_toggle[eid] = False
                self.prev_toggle_inventory[eid] = False
                for name in spell_attrs:
                    self.prev_spell_keys[(eid, name)] = 0
                self.prev_attack[eid] = False
                for base in ('fireball','laser_beam','dash'):
                    self.prev_action_slots[(eid, f'{base}_kb_a')] = False
                    self.prev_action_slots[(eid, f'{base}_kb_b')] = False
            return

        # Recargar bindings para aplicar cambios guardados sin reiniciar
        self.config._load()
        # Obtener estado actual del teclado
        keys = pygame.key.get_pressed()
        # Manual reload of spells.json via F4
        reload_pressed = bool(keys[getattr(pygame, 'K_F4')])
        if reload_pressed and not self._prev_reload_spells:
            try:
                reload_spells()
                logger.info("[InputSystem] Manual reload of spells.json triggered (F4)")
            except Exception:
                logger.exception("[InputSystem] Failed to reload spells on F4")
        self._prev_reload_spells = reload_pressed
        # Leer bindings dinámicos
        move_up = self.config.get_key("move_up")
        move_down = self.config.get_key("move_down")
        move_left = self.config.get_key("move_left")
        move_right = self.config.get_key("move_right")
        attack_key = self.config.get_key("attack")
        # Nuevos hechizos semánticos
        lb_key = self.config.get_key("spell_lightball")
        slash_key = self.config.get_key("spell_slash")
        heal_key = self.config.get_key("spell_healing_aura")
        db_key = self.config.get_key("spell_darkball")
        ib_key = self.config.get_key("spell_iceball")
        lightning_key = self.config.get_key("spell_lightning")
        pause_key = self.config.get_key("pause")
        af_key = self.config.get_key("spell_arcane_flame")
        fw_key = self.config.get_key("spell_firework_launch")
        sm_key = self.config.get_key("spell_smoke")
        se_key = self.config.get_key("spell_smoke_emitter")
        sh_key = self.config.get_key("spell_sphere_magic_shield")
        tp_key = self.config.get_key("spell_teleport")
        # Flag: Buildings Editor activo -> solo permitir movimiento
        editor_buildings_active = bool(getattr(getattr(world, 'state', None), 'buildings_editor_active', False))
        # Para cada entidad con InputComponent
        # Flag para mostrar todos los drops
        alt_down = bool(pygame.key.get_mods() & pygame.KMOD_ALT)
        for eid, inp in world.components.get('InputComponent', {}).items():
            # Asignar flag show_all_drops
            inp.show_all_drops = alt_down
            # Movimiento en ejes X e Y
            inp.move_x = int(keys[move_right]) - int(keys[move_left])
            inp.move_y = int(keys[move_down]) - int(keys[move_up])
            curr_attack = bool(keys[attack_key])
            inp.attack = curr_attack
            #logger.debug(f"[DEBUG][{time.time():.3f}] eid={eid} move=({inp.move_x},{inp.move_y}), click={inp.click}")
            # Actualizar velocidad según MovementSpeed
            vel = world.components.get('Velocity', {}).get(eid)
            ms = world.components.get('MovementSpeed', {}).get(eid)
            if vel and ms:
                dx, dy = inp.move_x, inp.move_y
                speed = ms.speed
                length = math.hypot(dx, dy)
                if length > 0:
                    vel.vx = dx / length * speed
                    vel.vy = dy / length * speed
                else:
                    vel.vx = vel.vy = 0

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
                    if not editor_buildings_active:
                        # Ataque físico: sólo en flanco ascendente y evitar re-entrada
                        prev_a = self.prev_attack.get(eid, False)
                        if curr_attack and not prev_a:
                            if (not isinstance(current, PlayerAttackState)) and (isinstance(current, IdleState) or isinstance(current, MoveState)):
                                state_comp.fsm.change_state(PlayerAttackState(), proxy)
                        # Hechizos (q/e): entrar a selección SOLO en flanco ascendente y si está en Idle/Move
                        pressed_lb = bool(keys[lb_key]) and self.prev_spell_keys.get((eid, 'lightball'), 0) == 0
                        pressed_sl = bool(keys[slash_key]) and self.prev_spell_keys.get((eid, 'slash'), 0) == 0
                        if (pressed_lb or pressed_sl) and (isinstance(current, IdleState) or isinstance(current, MoveState)):
                            state_comp.fsm.change_state(PlayerSpellSelectState(), proxy)
                        # Actualizar memoria de flanco para ataque
                        self.prev_attack[eid] = curr_attack
                    else:
                        # En editor: desactivar ataque y limpiar memoria para evitar flancos al salir
                        inp.attack = False
                        self.prev_attack[eid] = False

            # Mapear habilidades Q, E, X desde config
            spell_attrs = ['lightball','slash','healing_aura','darkball','iceball','lightning','arcane_flame','firework_launch','smoke','smoke_emitter','sphere_magic_shield','teleport']
            if editor_buildings_active:
                # En editor: desactivar todos los hechizos y resetear flancos
                for name in spell_attrs:
                    setattr(inp, f'spell_{name}', False)
                    self.prev_spell_keys[(eid,name)] = 0
            else:
                # Mapear hechizos desde teclado
                inp.spell_lightball = bool(keys[lb_key])
                inp.spell_slash = bool(keys[slash_key])
                inp.spell_healing_aura = bool(keys[heal_key])
                # Mapear nuevos hechizos oscuro e hielo
                inp.spell_darkball = bool(keys[db_key])
                inp.spell_iceball = bool(keys[ib_key])
                inp.spell_lightning = bool(keys[lightning_key])
                inp.spell_arcane_flame = bool(keys[af_key])
                inp.spell_firework_launch = bool(keys[fw_key])
                inp.spell_smoke = bool(keys[sm_key])
                inp.spell_smoke_emitter = bool(keys[se_key])
                inp.spell_sphere_magic_shield = bool(keys[sh_key])
                inp.spell_teleport = bool(keys[tp_key])
                # Detección de eventos de hechizos: presionado, mantenido y soltado
                for name in spell_attrs:
                    curr = getattr(inp, f'spell_{name}')
                    state = self.prev_spell_keys.get((eid,name), 0)
                    ts = time.time()
                    if curr and state == 0:
                        logger.debug(f'[DEBUG][{ts:.3f}] eid={eid} botón presionado -> {name}')
                        world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell=name)
                        state = 1
                    elif curr and state == 1:
                        logger.debug(f'[DEBUG][{ts:.3f}] eid={eid} botón mantenido apretado -> {name}')
                        state = 2
                    elif not curr and state > 0:
                        logger.debug(f'[DEBUG][{ts:.3f}] eid={eid} botón soltado    -> {name}')
                        state = 0
                    self.prev_spell_keys[(eid,name)] = state

            # Toggle editor mode: detectar flanco ascendente
            toggle_key = self.config.get_key("toggle_item_editor")
            curr_toggle = bool(keys[toggle_key])
            prev_toggle = self.prev_toggle.get(eid, False)
            if curr_toggle and not prev_toggle:
                inp.toggle_editor = True
            else:
                inp.toggle_editor = False
            self.prev_toggle[eid] = curr_toggle

            # Toggle inventory display: detectar flanco ascendente
            inv_key = self.config.get_key("toggle_inventory")
            curr_inv = bool(keys[inv_key])
            prev_inv = self.prev_toggle_inventory.get(eid, False)
            if curr_inv and not prev_inv:
                inp.toggle_inventory = True
            else:
                inp.toggle_inventory = False
            self.prev_toggle_inventory[eid] = curr_inv

            # Control de click: desactivar cuando se arrastra un ítem o el panel de inventario
            dragging_items = any(isinstance(s, DropDragSystem) and s.dragging_eid is not None for s in getattr(world, 'update_systems', []))
            dragging_ui = any(isinstance(s, InventoryUISystem) and s.dragging for s in getattr(world, 'render_systems', []))
            # Nota: dropeo por teclado deshabilitado completamente; sólo drag & drop
            dragging = dragging_items or dragging_ui
            # Suppress gameplay input while Spawner Editor is dragging/panning with RMB
            spawner_suppressed = bool(getattr(getattr(world, 'state', None), 'spawner_input_suppressed', False))
            # Log suppression only on transitions to reduce noise
            suppressed_now = editor_buildings_active or dragging or spawner_suppressed
            prev_supp = self._prev_suppressed.get(eid, False)
            if suppressed_now and not prev_supp:
                logger.debug(
                    f"[DEBUG] [InputSystem] input suppressed (buildings_editor={editor_buildings_active}, dragging_items={dragging_items}, dragging_ui={dragging_ui})"
                )
            elif not suppressed_now and prev_supp:
                logger.debug("[DEBUG] [InputSystem] input suppression ended")
            self._prev_suppressed[eid] = suppressed_now

            if suppressed_now:
                inp.click = False
                self.prev_click[eid] = False
                self.prev_right[eid] = False
                # Reset mouse action edges
                self.prev_mouse[(eid, 'fireball')] = False
                self.prev_mouse[(eid, 'dash')] = False
                # Reset keyboard slot edges
                for base in ('fireball','laser_beam','dash'):
                    self.prev_action_slots[(eid, f'{base}_kb_a')] = False
                    self.prev_action_slots[(eid, f'{base}_kb_b')] = False
            else:
                # Actualizar estado del mouse y acciones configurables
                mx, my = pygame.mouse.get_pos()
                ui_blocked = is_blocked(mx, my)
                # Request state for up to 5 buttons (L, M, R, X1, X2)
                mouse_pressed = pygame.mouse.get_pressed(5)
                # inp.click sigue atado al botón izquierdo para pick-up de inventario
                curr_left = bool(mouse_pressed[0]) and not ui_blocked
                inp.click = curr_left
                self.prev_click[eid] = curr_left

                # Botones configurables para acciones (pueden estar desasignados -> None)
                fb_btn = self.config.get_mouse_button_for_binding("mouse_fireball")
                lb_btn = self.config.get_mouse_button_for_binding("mouse_laser_beam")
                dash_btn = self.config.get_mouse_button_for_binding("mouse_dash")

                # Teclas configurables (A/B) para acciones
                kb_codes = {
                    ('fireball','a'): self.config.get_key_for_binding('kb_fireball_a'),
                    ('fireball','b'): self.config.get_key_for_binding('kb_fireball_b'),
                    ('laser_beam','a'): self.config.get_key_for_binding('kb_laser_beam_a'),
                    ('laser_beam','b'): self.config.get_key_for_binding('kb_laser_beam_b'),
                    ('dash','a'): self.config.get_key_for_binding('kb_dash_a'),
                    ('dash','b'): self.config.get_key_for_binding('kb_dash_b'),
                }

                # Fireball en flanco ascendente
                curr_fb_mouse = bool(mouse_pressed[fb_btn]) if isinstance(fb_btn, int) else False
                curr_fb_mouse = curr_fb_mouse and not ui_blocked
                prev_fb_mouse = self.prev_mouse.get((eid, 'fireball'), False)
                fb_edge = (curr_fb_mouse and not prev_fb_mouse)
                # Keyboard edges (A/B)
                for slot in ('a','b'):
                    code = kb_codes.get(('fireball', slot))
                    if code is not None:
                        curr = bool(keys[code]) and not ui_blocked
                        prev = self.prev_action_slots.get((eid, f'fireball_kb_{slot}'), False)
                        if curr and not prev:
                            fb_edge = True
                        self.prev_action_slots[(eid, f'fireball_kb_{slot}')] = curr
                if eid in world.components.get('PlayerTagComponent', {}) and fb_edge:
                    world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='fireball')
                self.prev_mouse[(eid, 'fireball')] = curr_fb_mouse

                # Laser beam: mantener el comportamiento original (disparo continuo mientras se mantiene)
                curr_lb_mouse = bool(mouse_pressed[lb_btn]) if isinstance(lb_btn, int) else False
                curr_lb_mouse = curr_lb_mouse and not ui_blocked
                curr_lb = curr_lb_mouse
                for slot in ('a','b'):
                    code = kb_codes.get(('laser_beam', slot))
                    if code is not None:
                        curr_lb = curr_lb or (bool(keys[code]) and not ui_blocked)
                if curr_lb:
                    logger.debug(f"[DEBUG][{time.time():.3f}] eid={eid} mouse-button({lb_btn}) -> laser_beam")
                    world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='laser_beam')

                # Dash en flanco ascendente, con supresión si el click inicial cae sobre el panel de inventario
                curr_dash_mouse = bool(mouse_pressed[dash_btn]) if isinstance(dash_btn, int) else False
                curr_dash_mouse = curr_dash_mouse and not ui_blocked
                prev_dash_mouse = self.prev_mouse.get((eid, 'dash'), False)
                dash_edge = (curr_dash_mouse and not prev_dash_mouse)
                if dash_edge:
                    # Si se pulsa sobre el panel de inventario, ignorar
                    for s in getattr(world, 'render_systems', []):
                        if isinstance(s, InventoryUISystem) and s.panel_rect and s.panel_rect.collidepoint((mx, my)):
                            curr_dash_mouse = False
                            dash_edge = False
                            break
                # Keyboard edges (A/B) for dash
                for slot in ('a','b'):
                    code = kb_codes.get(('dash', slot))
                    if code is not None:
                        curr = bool(keys[code]) and not ui_blocked
                        prev = self.prev_action_slots.get((eid, f'dash_kb_{slot}'), False)
                        if curr and not prev:
                            dash_edge = True
                        self.prev_action_slots[(eid, f'dash_kb_{slot}')] = curr
                if dash_edge:
                    logger.debug(f"[DEBUG][{time.time():.3f}] eid={eid} mouse-button({dash_btn}) -> dash")
                    world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='dash')
                self.prev_mouse[(eid, 'dash')] = curr_dash_mouse