from __future__ import annotations

import logging
import math
import time
from typing import Dict, Tuple

import pygame

from roguelike_game.ecs.components.ai.wants_to_cast import WantsToCastSpell
from roguelike_game.ecs.systems.inventory.drop_drag_system import DropDragSystem
from roguelike_game.ecs.systems.inventory.inventory_ui_system import InventoryUISystem
from roguelike_ui.ui_blocker import is_blocked

from .constants import ACTION_BASES, SPELL_ATTRS

logger = logging.getLogger(__name__)


def block_reason(world) -> str | None:
    """Detecta si alguna UI global requiere bloquear todo el input."""
    state = getattr(world, "state", None)
    if not state:
        return None
    if bool(getattr(state, "console_open", False)):
        return "console"
    item_state = getattr(state, "item_editor_state", None)
    if item_state and getattr(item_state, "visible", False):
        return "item_editor"
    if bool(getattr(state, "class_selector_open", False)):
        return "class_selector"
    if bool(getattr(state, "spawner_editor_active", False)):
        return "spawner_editor"
    if bool(getattr(state, "chat_open", False)):
        return "chat"
    return None


def _zero_velocity(world, eid: int) -> None:
    vel = world.components.get("Velocity", {}).get(eid)
    if vel:
        vel.vx = 0
        vel.vy = 0


def reset_entity_inputs(system, world, eid: int, inp) -> None:
    """Resetea inputs visibles y memorias de flancos para un eid."""
    # Inputs inmediatos
    inp.click = False
    inp.move_x = 0
    inp.move_y = 0
    inp.attack = False
    inp.interact = False
    inp.show_all_drops = False
    for name in SPELL_ATTRS:
        setattr(inp, f"spell_{name}", False)
    inp.toggle_editor = False
    inp.toggle_inventory = False

    # Movimiento
    _zero_velocity(world, eid)

    # Memorias de flancos
    system.prev_click[eid] = False
    system.prev_right[eid] = False
    system.prev_toggle[eid] = False
    system.prev_toggle_inventory[eid] = False
    system.prev_interact[eid] = False
    system.prev_attack[eid] = False

    # Memorias de ratón
    system.prev_mouse[(eid, "fireball")] = False
    system.prev_mouse[(eid, "dash")] = False
    # Resetear ratón para todos los hechizos mapeables por mouse
    for name in SPELL_ATTRS:
        system.prev_mouse[(eid, f"spell_{name}")] = False

    # Memorias de teclado por slots
    for base in ACTION_BASES:
        system.prev_action_slots[(eid, f"{base}_kb_a")] = False
        system.prev_action_slots[(eid, f"{base}_kb_b")] = False

    # Memorias de teclas de hechizos
    for name in SPELL_ATTRS:
        system.prev_spell_keys[(eid, name)] = 0


def block_all_inputs_and_reset(system, world) -> None:
    for eid, inp in world.components.get("InputComponent", {}).items():
        reset_entity_inputs(system, world, eid, inp)


def set_velocity_from_input(vel, ms, dx: int, dy: int) -> None:
    if not vel or not ms:
        return
    length = math.hypot(dx, dy)
    if length > 0:
        speed = ms.speed
        vel.vx = dx / length * speed
        vel.vy = dy / length * speed
    else:
        vel.vx = 0
        vel.vy = 0


def map_keyboard_spells(inp, any_pressed) -> None:
    inp.spell_lightball = any_pressed("spell_lightball")
    inp.spell_slash = any_pressed("spell_slash")
    inp.spell_healing_aura = any_pressed("spell_healing_aura")
    inp.spell_darkball = any_pressed("spell_darkball")
    inp.spell_iceball = any_pressed("spell_iceball")
    inp.spell_lightning = any_pressed("spell_lightning")
    inp.spell_arcane_flame = any_pressed("spell_arcane_flame")
    inp.spell_firework_launch = any_pressed("spell_firework_launch")
    inp.spell_smoke = any_pressed("spell_smoke")
    inp.spell_smoke_emitter = any_pressed("spell_smoke_emitter")
    inp.spell_sphere_magic_shield = any_pressed("spell_sphere_magic_shield")
    inp.spell_teleport = any_pressed("spell_teleport")
    inp.spell_puddle_lava = any_pressed("spell_puddle_lava")
    inp.spell_mine_basic = any_pressed("spell_mine_basic")
    inp.spell_boomerang = any_pressed("spell_boomerang")
    inp.spell_chain_lightning = any_pressed("spell_chain_lightning")
    inp.spell_vortex_pull = any_pressed("spell_vortex_pull")
    inp.spell_vortex_push = any_pressed("spell_vortex_push")
    inp.spell_flame_breath = any_pressed("spell_flame_breath")


def process_spell_edges(system, world, eid: int, inp) -> None:
    for name in SPELL_ATTRS:
        curr = getattr(inp, f"spell_{name}")
        state = system.prev_spell_keys.get((eid, name), 0)
        ts = time.time()
        if curr and state == 0:
            logger.debug(f"[DEBUG][{ts:.3f}] eid={eid} botón presionado -> {name}")
            world.components.setdefault("WantsToCastSpell", {})[eid] = WantsToCastSpell(caster=eid, spell=name)
            state = 1
        elif curr and state == 1:
            logger.debug(f"[DEBUG][{ts:.3f}] eid={eid} botón mantenido apretado -> {name}")
            state = 2
        elif not curr and state > 0:
            logger.debug(f"[DEBUG][{ts:.3f}] eid={eid} botón soltado    -> {name}")
            state = 0
        system.prev_spell_keys[(eid, name)] = state


def compute_suppression_flags(system, world) -> Tuple[bool, Dict[str, bool]]:
    state = getattr(world, "state", None)
    editor_buildings_active = bool(getattr(state, "buildings_editor_active", False))
    particles_editor_visible = bool(getattr(state, "particles_editor_visible", False))

    # Dragging desde sistemas
    dragging_items = any(
        isinstance(s, DropDragSystem) and s.dragging_eid is not None for s in getattr(world, "update_systems", [])
    )
    dragging_ui = any(
        isinstance(s, InventoryUISystem) and getattr(s, "dragging", False) for s in getattr(world, "render_systems", [])
    )

    spawner_suppressed = bool(getattr(state, "spawner_input_suppressed", False))

    suppressed_now = editor_buildings_active or particles_editor_visible or dragging_items or dragging_ui or spawner_suppressed

    details = {
        "buildings_editor": editor_buildings_active,
        "particles_editor": particles_editor_visible,
        "dragging_items": dragging_items,
        "dragging_ui": dragging_ui,
        "spawner": spawner_suppressed,
    }

    return suppressed_now, details


def log_suppression_transitions(system, eid: int, suppressed_now: bool, details: Dict[str, bool]) -> None:
    prev_supp = system._prev_suppressed.get(eid, False)
    if suppressed_now and not prev_supp:
        logger.debug(
            f"[DEBUG] [InputSystem] input suppressed (buildings_editor={details['buildings_editor']}, "
            f"particles_editor={details['particles_editor']}, dragging_items={details['dragging_items']}, dragging_ui={details['dragging_ui']})"
        )
    elif not suppressed_now and prev_supp:
        logger.debug("[DEBUG] [InputSystem] input suppression ended")
    system._prev_suppressed[eid] = suppressed_now


def handle_mouse_actions(system, world, eid: int, keys, mx: int, my: int) -> None:
    """Gestiona fireball (edge), laser_beam (hold), dash (edge) y mouse_spell_<name>."""
    ui_blocked = is_blocked(mx, my)
    mouse_pressed = pygame.mouse.get_pressed(5)

    # Click izquierdo para pick-ups
    curr_left = bool(mouse_pressed[0]) and not ui_blocked
    inp = world.components.get("InputComponent", {}).get(eid)
    if inp is not None:
        inp.click = curr_left
    system.prev_click[eid] = curr_left

    # Botones/teclas configurables
    _get_mouse_btn = getattr(system.config, "get_mouse_button_for_binding", None)
    _get_key = getattr(system.config, "get_key_for_binding", None)
    fb_btn = _get_mouse_btn("mouse_fireball") if callable(_get_mouse_btn) else None
    lb_btn = _get_mouse_btn("mouse_laser_beam") if callable(_get_mouse_btn) else None
    dash_btn = _get_mouse_btn("mouse_dash") if callable(_get_mouse_btn) else None

    kb_codes = {
        ("fireball", "a"): _get_key("kb_fireball_a") if callable(_get_key) else None,
        ("fireball", "b"): _get_key("kb_fireball_b") if callable(_get_key) else None,
        ("laser_beam", "a"): _get_key("kb_laser_beam_a") if callable(_get_key) else None,
        ("laser_beam", "b"): _get_key("kb_laser_beam_b") if callable(_get_key) else None,
        ("dash", "a"): _get_key("kb_dash_a") if callable(_get_key) else None,
        ("dash", "b"): _get_key("kb_dash_b") if callable(_get_key) else None,
    }

    # Fireball (edge)
    curr_fb_mouse = bool(mouse_pressed[fb_btn]) if isinstance(fb_btn, int) else False
    curr_fb_mouse = curr_fb_mouse and not ui_blocked
    prev_fb_mouse = system.prev_mouse.get((eid, "fireball"), False)
    fb_edge = curr_fb_mouse and not prev_fb_mouse
    for slot in ("a", "b"):
        code = kb_codes.get(("fireball", slot))
        if code is not None:
            curr = bool(keys[code]) and not ui_blocked
            prev = system.prev_action_slots.get((eid, f"fireball_kb_{slot}"), False)
            if curr and not prev:
                fb_edge = True
            system.prev_action_slots[(eid, f"fireball_kb_{slot}")] = curr
    if eid in world.components.get("PlayerTagComponent", {}) and fb_edge:
        world.components.setdefault("WantsToCastSpell", {})[eid] = WantsToCastSpell(caster=eid, spell="fireball")
    system.prev_mouse[(eid, "fireball")] = curr_fb_mouse

    # Laser beam (hold)
    curr_lb_mouse = bool(mouse_pressed[lb_btn]) if isinstance(lb_btn, int) else False
    curr_lb_mouse = curr_lb_mouse and not ui_blocked
    curr_lb = curr_lb_mouse
    for slot in ("a", "b"):
        code = kb_codes.get(("laser_beam", slot))
        if code is not None:
            curr_lb = curr_lb or (bool(keys[code]) and not ui_blocked)
    if curr_lb:
        logger.debug(f"[DEBUG][{time.time():.3f}] eid={eid} mouse-button({lb_btn}) -> laser_beam")
        world.components.setdefault("WantsToCastSpell", {})[eid] = WantsToCastSpell(caster=eid, spell="laser_beam")

    # Dash (edge) con supresión sobre panel de inventario
    curr_dash_mouse = bool(mouse_pressed[dash_btn]) if isinstance(dash_btn, int) else False
    curr_dash_mouse = curr_dash_mouse and not ui_blocked
    prev_dash_mouse = system.prev_mouse.get((eid, "dash"), False)
    dash_edge = curr_dash_mouse and not prev_dash_mouse
    if dash_edge:
        for s in getattr(world, "render_systems", []):
            if isinstance(s, InventoryUISystem) and getattr(s, "panel_rect", None) and s.panel_rect.collidepoint((mx, my)):
                curr_dash_mouse = False
                dash_edge = False
                break
    for slot in ("a", "b"):
        code = kb_codes.get(("dash", slot))
        if code is not None:
            curr = bool(keys[code]) and not ui_blocked
            prev = system.prev_action_slots.get((eid, f"dash_kb_{slot}"), False)
            if curr and not prev:
                dash_edge = True
            system.prev_action_slots[(eid, f"dash_kb_{slot}")] = curr
    if dash_edge:
        logger.debug(f"[DEBUG][{time.time():.3f}] eid={eid} mouse-button({dash_btn}) -> dash")
        world.components.setdefault("WantsToCastSpell", {})[eid] = WantsToCastSpell(caster=eid, spell="dash")
    system.prev_mouse[(eid, "dash")] = curr_dash_mouse

    # Mouse para hechizos genéricos
    for name in SPELL_ATTRS:
        btn = _get_mouse_btn(f"mouse_spell_{name}") if callable(_get_mouse_btn) else None
        if isinstance(btn, int):
            curr_spell_mouse = bool(mouse_pressed[btn]) and not ui_blocked
            prev_spell_mouse = system.prev_mouse.get((eid, f"spell_{name}"), False)
            edge = curr_spell_mouse and not prev_spell_mouse
            if eid in world.components.get("PlayerTagComponent", {}) and edge:
                world.components.setdefault("WantsToCastSpell", {})[eid] = WantsToCastSpell(caster=eid, spell=name)
            system.prev_mouse[(eid, f"spell_{name}")] = curr_spell_mouse


def rising_edge(system, eid: int, prev_dict: Dict[int, bool], curr: bool) -> bool:
    prev = prev_dict.get(eid, False)
    edge = curr and not prev
    prev_dict[eid] = curr
    return edge
