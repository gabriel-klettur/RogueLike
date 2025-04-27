import pygame, time
import src.roguelike_engine.config as config
from .menu import execute_menu_option



def handle_keyboard(event, state):
    if event.type == pygame.KEYDOWN:
        if event.key == pygame.K_ESCAPE:
            state.show_menu = not state.show_menu

        elif event.key == pygame.K_q:
            state.player.restore_all()
            state.systems.effects.spawn_healing_aura()
            state.player.stats.last_restore_time = time.time()

        elif state.show_menu:
            result = state.menu.handle_input(event)
            if result:
                execute_menu_option(result, state)

        # ---------- HABILIDADES DEL JUGADOR ---------- #
        elif event.key == pygame.K_1:
            if state.player.stats.activate_shield():
                state.systems.effects.spawn_magic_shield()
                state.player.stats.last_shield_time = time.time()

        elif event.key == pygame.K_f:
            state.systems.effects.spawn_firework()
            state.player.stats.last_firework_time = time.time()

        elif event.key == pygame.K_r:
            state.systems.effects.spawn_smoke_emitter()
            state.player.stats.last_smoke_time = time.time()

        elif event.key == pygame.K_t:
            state.systems.effects.spawn_smoke()
            state.player.stats.last_smoke_time = time.time()

        elif event.key == pygame.K_z:
            mx, my = pygame.mouse.get_pos()
            world_x = mx / state.camera.zoom + state.camera.offset_x
            world_y = my / state.camera.zoom + state.camera.offset_y
            state.systems.effects.spawn_lightning((world_x, world_y))
            state.player.stats.last_lightning_time = time.time()

        elif event.key == pygame.K_x:
            mx, my = pygame.mouse.get_pos()
            wx = mx / state.camera.zoom + state.camera.offset_x
            wy = my / state.camera.zoom + state.camera.offset_y
            state.systems.effects.spawn_arcane_flame(wx, wy)

        elif event.key == pygame.K_v:
            mx, my = pygame.mouse.get_pos()
            wx = mx / state.camera.zoom + state.camera.offset_x
            wy = my / state.camera.zoom + state.camera.offset_y
            px, py = state.systems.effects._player_center()
            dir_vec = pygame.math.Vector2(wx - px, wy - py)
            if dir_vec.length():
                dir_vec.normalize_ip()
            state.systems.effects.spawn_dash(state.player, dir_vec)
            state.player.stats.last_dash_time = time.time()

        elif event.key == pygame.K_e:
            mx, my = pygame.mouse.get_pos()
            world_x = mx / state.camera.zoom + state.camera.offset_x
            world_y = my / state.camera.zoom + state.camera.offset_y
            px, py = state.systems.effects._player_center()
            dir_vec = pygame.math.Vector2(world_x - px, world_y - py)
            if dir_vec.length():
                dir_vec.normalize_ip()
            state.systems.effects.spawn_slash(dir_vec)
            state.player.stats.last_slash_time = time.time()

        # ---------- TEST / DEBUG ---------- #
        elif event.key == pygame.K_F10:
            if hasattr(state, "editor"):
                state.editor.active = not state.editor.active
                print("🛠️ Modo editor activado" if state.editor.active else "🛑 Modo editor desactivado")

        elif event.key == pygame.K_F9:
            config.DEBUG = not config.DEBUG
            print(f"🧪 DEBUG {'activado' if config.DEBUG else 'desactivado'}")

        # ---------- TILE-EDITOR (F8) --------- #
        elif event.key == pygame.K_F8:
            new_val = not getattr(state, "tile_editor_active", False)
            state.tile_editor_active = new_val
            if hasattr(state, "tile_editor_state"):
                tes = state.tile_editor_state
                tes.active = new_val
                if not new_val:
                    tes.picker_open    = False
                    tes.selected_tile  = None
                    tes.current_choice = None
            print("🟩 Tile-Editor ON" if new_val else "🟥 Tile-Editor OFF")
            return  # evitamos más atajos este frame
