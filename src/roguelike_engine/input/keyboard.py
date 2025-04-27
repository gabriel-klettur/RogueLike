import pygame, time
import src.roguelike_engine.config as config
from .menu import execute_menu_option

# Mapa de teclas a funciones
_KEY_ACTIONS = {
    pygame.K_ESCAPE: lambda state: setattr(state, 'show_menu', not state.show_menu),
    pygame.K_q:      lambda state: (_restore_and_heal(state)),

    pygame.K_1:      lambda state: (_activate_shield(state)),
    pygame.K_f:      lambda state: (_spawn_and_stamp(state, 'firework', 'last_firework_time')),
    pygame.K_r:      lambda state: (_spawn_and_stamp(state, 'smoke_emitter', 'last_smoke_time')),
    pygame.K_t:      lambda state: (_spawn_and_stamp(state, 'smoke', 'last_smoke_time')),

    pygame.K_z:      lambda state: (_spawn_and_stamp(state, 'lightning', 'last_lightning_time')),
    pygame.K_x:      lambda state: (_spawn_arcane(state)),
    pygame.K_v:      lambda state: (_spawn_and_stamp(state, 'dash', 'last_dash_time')),
    pygame.K_e:      lambda state: (_spawn_and_stamp(state, 'slash', 'last_slash_time')),
}

def handle_keyboard(event, state):
    if event.type != pygame.KEYDOWN:
        return

    # Si hay menú abierto, prioridad a flechas/Enter
    if state.show_menu and event.key not in (pygame.K_ESCAPE, pygame.K_F9, pygame.K_F10, pygame.K_F8):
        result = state.menu.handle_input(event)
        if result:
            execute_menu_option(result, state)
        return

    action = _KEY_ACTIONS.get(event.key)
    if action:
        action(state)
    elif event.key == pygame.K_F9:
        config.DEBUG = not config.DEBUG
        print(f"🧪 DEBUG {'activado' if config.DEBUG else 'desactivado'}")
    elif event.key == pygame.K_F10 and hasattr(state, "editor"):
        state.editor.active = not state.editor.active
        print("🛠️ Modo editor " + ("activado" if state.editor.active else "desactivado"))
    elif event.key == pygame.K_F8:
        _toggle_tile_editor(state)

# — Funciones auxiliares internas —

def _restore_and_heal(state):
    state.player.restore_all()
    state.systems.effects.spawn_healing_aura()
    state.player.stats.last_restore_time = time.time()

def _activate_shield(state):
    if state.player.stats.activate_shield():
        state.systems.effects.spawn_magic_shield()
        state.player.stats.last_shield_time = time.time()

def _spawn_and_stamp(state, ability, timestamp_attr):
    getattr(state.systems.effects, f"spawn_{ability}")()
    setattr(state.player.stats, timestamp_attr, time.time())

def _spawn_arcane(state):
    # arcane_flame no lleva sello de cooldown en stats
    mx, my = pygame.mouse.get_pos()
    wx = mx/state.camera.zoom + state.camera.offset_x
    wy = my/state.camera.zoom + state.camera.offset_y
    state.systems.effects.spawn_arcane_flame(wx, wy)

def _toggle_tile_editor(state):
    new = not getattr(state, "tile_editor_active", False)
    state.tile_editor_active = new
    tes = getattr(state, "tile_editor_state", None)
    if tes:
        tes.active = new
        if not new:
            tes.picker_open = tes.selected_tile = tes.current_choice = None
    print("🟩 Tile-Editor ON" if new else "🟥 Tile-Editor OFF")
