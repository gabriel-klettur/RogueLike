import pygame
import time
from src.roguelike_project.engine.game.network.client import WebSocketClient
import src.roguelike_project.config as config

def handle_events(state):
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            state.running = False

        # ------------------------------------------------------------- #
        #                      TECLADO PRINCIPAL                        #
        # ------------------------------------------------------------- #
        elif event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE:
                state.show_menu = not state.show_menu

            elif event.key == pygame.K_q:
                state.player.stats.restore_all(state)

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

        # ------------------------------------------------------------- #
        #                       WHEEL / MOUSE                           #
        # ------------------------------------------------------------- #
        elif event.type == pygame.MOUSEWHEEL:
            if event.y > 0:
                state.camera.zoom = min(state.camera.zoom + 0.1, 2.0)
            elif event.y < 0:
                state.camera.zoom = max(state.camera.zoom - 0.1, 0.5)

        elif event.type == pygame.MOUSEBUTTONDOWN:
            if event.button == 1:
                mouse_x, mouse_y = pygame.mouse.get_pos()
                world_mouse_x = mouse_x / state.camera.zoom + state.camera.offset_x
                world_mouse_y = mouse_y / state.camera.zoom + state.camera.offset_y

                player_center_x = state.player.x + state.player.sprite_size[0] / 2
                player_center_y = state.player.y + state.player.sprite_size[1] / 2

                dx = world_mouse_x - player_center_x
                dy = world_mouse_y - player_center_y
                angle = -pygame.math.Vector2(dx, dy).angle_to((1, 0))
                state.systems.effects.spawn_fireball(angle)

            elif event.button == 2:
                state.systems.effects.shooting_laser = True
                state.systems.effects.last_laser_time = 0

            elif event.button == 3:
                mx, my = pygame.mouse.get_pos()
                world_x = mx / state.camera.zoom + state.camera.offset_x
                world_y = my / state.camera.zoom + state.camera.offset_y
                state.player.movement.teleport(world_x, world_y)            
                state.systems.effects.spawn_teleport(world_x, world_y)

        elif event.type == pygame.MOUSEBUTTONUP:
            if event.button == 2:
                state.systems.effects.shooting_laser = False

    # ---------------------- MOVIMIENTO CONTINUO ---------------------- #
    if not state.show_menu:
        keys = pygame.key.get_pressed()
        dx = dy = 0
        if keys[pygame.K_UP] or keys[pygame.K_w]:    dy = -1
        if keys[pygame.K_DOWN] or keys[pygame.K_s]:  dy = 1
        if keys[pygame.K_LEFT] or keys[pygame.K_a]:  dx = -1
        if keys[pygame.K_RIGHT] or keys[pygame.K_d]: dx = 1

        state.player.is_walking = dx != 0 or dy != 0
        solid_tiles = [tile for tile in state.tiles if tile.solid]
        state.player.move(dx, dy, state.obstacles, solid_tiles)

    # 🔁 Fuego continuo de láser
    if state.systems.effects.shooting_laser:
        now = time.time()
        if now - state.systems.effects.last_laser_time >= 0.01:
            mouse_x, mouse_y = pygame.mouse.get_pos()
            world_mouse_x = mouse_x / state.camera.zoom + state.camera.offset_x
            world_mouse_y = mouse_y / state.camera.zoom + state.camera.offset_y
            enemies = state.enemies + list(state.remote_entities.values())
            state.systems.effects.spawn_laser(world_mouse_x, world_mouse_y, enemies)
            state.systems.effects.last_laser_time = now

# -------------------------------------------------------------------- #
#                      MENÚ PAUSA / OPCIONES                           #
# -------------------------------------------------------------------- #
def execute_menu_option(selected, state):
    if selected == "Cambiar personaje":
        new_name = "valkyria" if state.player.character_name == "first_hero" else "first_hero"
        state.player.change_character(new_name)
        print(f"✅ Cambiado a personaje: {new_name}")
        state.show_menu = False

    elif selected == "Salir":
        state.running = False

    elif selected in ["Modo multijugador", "Modo local"]:
        if state.mode == "local":
            print("🌐 Conectando al servidor...")
            state.mode = "online"
            if not hasattr(state, "websocket") or not state.websocket:
                try:
                    state.websocket = WebSocketClient("ws://localhost:8000/ws", state.player)
                    state.websocket.start()
                    state.websocket_connected = True
                    print("✅ WebSocket conectado correctamente.")
                except Exception as e:
                    print(f"❌ Error al conectar WebSocket: {e}")
                    state.websocket_connected = False
        else:
            print("🔌 Desconectado, modo local activado.")
            state.mode = "local"
            if hasattr(state, "websocket") and state.websocket:
                try:
                    state.websocket.stop()
                    print("🧯 WebSocket desconectado.")
                except Exception as e:
                    print(f"❌ Error al cerrar WebSocket: {e}")
                state.websocket = None
                state.websocket_connected = False

        state.show_menu = False
