"""
Module: input_system.py
Sistema que traduce el estado de teclado a InputComponent y actualiza Velocity.
"""
# Path: src/roguelike_game/ecs/systems/input/input_system.py
import pygame
import time
import math
from roguelike_game.ecs.systems.inventory.drop_drag_system import DropDragSystem
from roguelike_game.ecs.systems.inventory.inventory_ui_system import InventoryUISystem

from roguelike_game.ecs.components.ai.wants_to_cast import WantsToCastSpell
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_game.ecs.systems.fsm.states.player.move_state import MoveState
from roguelike_game.ecs.systems.fsm.states.player.player_attack_state import PlayerAttackState
from roguelike_game.ecs.systems.fsm.states.player.player_spell_select_state import PlayerSpellSelectState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy
from roguelike_game.config.input_config import InputConfig

class InputSystem:
    """
    Captura el estado del teclado y actualiza InputComponent y Velocity.
    """
    def __init__(self, perf_log, config_path=None):
        self.perf_log = perf_log
        # Mapear estado previo de click y right-click para detección de flanco ascendente
        self.prev_click = {}
        self.prev_right = {}
        self.prev_drop = {}
        self.prev_toggle = {}
        self.prev_toggle_inventory = {}
        # Estado previo de hechizos para detección de flancos
        self.prev_spell_keys = {}
        # Cargar configuración de teclas desde JSON
        self.config = InputConfig(config_path)

    @benchmark(lambda self: self.perf_log, "4.2.2.InputSystem.update")
    def update(self, world, camera=None):
        # Recargar bindings para aplicar cambios guardados sin reiniciar
        self.config._load()
        # Obtener estado actual del teclado
        keys = pygame.key.get_pressed()
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
        # Para cada entidad con InputComponent
        # Flag para mostrar todos los drops
        alt_down = bool(pygame.key.get_mods() & pygame.KMOD_ALT)
        for eid, inp in world.components.get('InputComponent', {}).items():
            # Asignar flag show_all_drops
            inp.show_all_drops = alt_down
            # Movimiento en ejes X e Y
            inp.move_x = int(keys[move_right]) - int(keys[move_left])
            inp.move_y = int(keys[move_down]) - int(keys[move_up])
            inp.attack = bool(keys[attack_key])
            #print(f"[DEBUG][{time.time():.3f}] eid={eid} move=({inp.move_x},{inp.move_y}), click={inp.click}")
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
                    # Ataque físico
                    if inp.attack:
                        state_comp.fsm.change_state(PlayerAttackState(), proxy)
                    # Hechizos (q/e)
                    if inp.spell_lightball or inp.spell_slash:
                        state_comp.fsm.change_state(PlayerSpellSelectState(), proxy)

            # Mapear habilidades Q, E, X desde config
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
            spell_attrs = ['lightball','slash','healing_aura','darkball','iceball','lightning','arcane_flame','firework_launch','smoke','smoke_emitter','sphere_magic_shield','teleport']
            for name in spell_attrs:
                curr = getattr(inp, f'spell_{name}')
                state = self.prev_spell_keys.get((eid,name), 0)
                ts = time.time()
                if curr and state == 0:
                    print(f'[DEBUG][{ts:.3f}] eid={eid} botón presionado -> {name}')
                    world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell=name)
                    state = 1
                elif curr and state == 1:
                    print(f'[DEBUG][{ts:.3f}] eid={eid} botón mantenido apretado -> {name}')
                    state = 2
                elif not curr and state > 0:
                    print(f'[DEBUG][{ts:.3f}] eid={eid} botón soltado    -> {name}')
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

            drop_key = self.config.get_key("drop")
            curr_drop = bool(keys[drop_key])
            prev_d = self.prev_drop.get(eid, False)
            if curr_drop and not prev_d:
                inp.drop = True
            else:
                inp.drop = False
            self.prev_drop[eid] = curr_drop

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
            dragging = dragging_items or dragging_ui

            if dragging:
                print(f"[DEBUG] [InputSystem] input suppressed due to UI drag (dragging_items={dragging_items}, dragging_ui={dragging_ui})")
                inp.click = False
                self.prev_click[eid] = False
                curr_right = False
                self.prev_right[eid] = False
            else:
                # Actualizar estado del click y detectar flanco ascendente
                curr_click = bool(pygame.mouse.get_pressed()[0])
                inp.click = curr_click
                prev = self.prev_click.get(eid, False)
                # Generar intención de fireball sólo en flanco ascendente
                if eid in world.components.get('PlayerTagComponent', {}) and curr_click and not prev:
                    world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='fireball')
                # Guardar estado click para próxima iteración
                self.prev_click[eid] = curr_click
                # Lanzar el beam con click del medio
                middle = pygame.mouse.get_pressed()[1]
                if middle:
                    print(f"[DEBUG][{time.time():.3f}] eid={eid} middle-click -> laser_beam")
                    world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='laser_beam')
                # Detectar dash (right-click) por flanco ascendente
                curr_right = bool(pygame.mouse.get_pressed()[2])
                prev_r = self.prev_right.get(eid, False)
                # Suppress initial dash if right-click on inventory panel
                if curr_right and not prev_r:
                    for s in getattr(world, 'render_systems', []):
                        if isinstance(s, InventoryUISystem) and s.panel_rect and s.panel_rect.collidepoint(pygame.mouse.get_pos()):
                            curr_right = False
                            print(f"[DEBUG] [InputSystem] suppressed initial dash on inventory panel")
                            break
                if curr_right and not prev_r:
                    print(f"[DEBUG][{time.time():.3f}] eid={eid} right-click -> dash")
                    world.components.setdefault('WantsToCastSpell', {})[eid] = WantsToCastSpell(caster=eid, spell='dash')
                self.prev_right[eid] = curr_right