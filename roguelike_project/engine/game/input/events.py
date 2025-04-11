import pygame
import time
from roguelike_project.network.client import WebSocketClient

def handle_events(state):
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            state.running = False

        elif event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE:
                state.show_menu = not state.show_menu
            
            elif event.key == pygame.K_q:
                state.player.stats.restore_all(state)

            elif state.show_menu:
                result = state.menu.handle_input(event)
                if result:
                    execute_menu_option(result, state)
            
            elif event.key == pygame.K_1:
                if state.player.stats.activate_shield():
                    state.combat.effects.spawn_magic_shield()

            elif event.key == pygame.K_f:
                state.combat.projectiles.spawn_firework()

            elif event.key == pygame.K_r:
                state.combat.effects.spawn_smoke_emitter()

            elif event.key == pygame.K_z:
                mx, my = pygame.mouse.get_pos()
                world_x = mx / state.camera.zoom + state.camera.offset_x
                world_y = my / state.camera.zoom + state.camera.offset_y
                state.combat.effects.spawn_lightning((world_x, world_y))

        elif event.type == pygame.MOUSEWHEEL:
            if event.y > 0:
                state.camera.zoom = min(state.camera.zoom + 0.1, 2.0)
            elif event.y < 0:
                state.camera.zoom = max(state.camera.zoom - 0.1, 0.5)

        elif event.type == pygame.MOUSEBUTTONDOWN:
            if event.button == 1:  # Click izquierdo
                mouse_x, mouse_y = pygame.mouse.get_pos()
                world_mouse_x = mouse_x / state.camera.zoom + state.camera.offset_x
                world_mouse_y = mouse_y / state.camera.zoom + state.camera.offset_y

                player_center_x = state.player.x + state.player.sprite_size[0] / 2
                player_center_y = state.player.y + state.player.sprite_size[1] / 2

                dx = world_mouse_x - player_center_x
                dy = world_mouse_y - player_center_y

                angle = -pygame.math.Vector2(dx, dy).angle_to((1, 0))

                # ✅ Disparo delegado al manager de proyectiles
                state.combat.projectiles.spawn_fireball(angle)

            elif event.button == 3:  # Click derecho presionado
                state.combat.effects.shooting_laser = True
                state.combat.effects.last_laser_time = 0

        elif event.type == pygame.MOUSEBUTTONUP:
            if event.button == 3:  # Soltar click derecho
                state.combat.effects.shooting_laser = False
                state.combat.effects.lasers.clear()

    if not state.show_menu:
        keys = pygame.key.get_pressed()
        dx = dy = 0
        if keys[pygame.K_UP]: dy = -1
        if keys[pygame.K_DOWN]: dy = 1
        if keys[pygame.K_LEFT]: dx = -1
        if keys[pygame.K_RIGHT]: dx = 1
        if keys[pygame.K_w]: dy = -1
        if keys[pygame.K_s]: dy = 1
        if keys[pygame.K_a]: dx = -1
        if keys[pygame.K_d]: dx = 1

        state.player.is_walking = dx != 0 or dy != 0
        solid_tiles = [tile for tile in state.tiles if tile.solid]
        state.player.move(dx, dy, state.obstacles, solid_tiles)

    # 🔁 Fuego continuo de láser
    if state.combat.effects.shooting_laser:
        now = time.time()
        if now - state.combat.effects.last_laser_time >= 0.01:
            mouse_x, mouse_y = pygame.mouse.get_pos()
            world_mouse_x = mouse_x / state.camera.zoom + state.camera.offset_x
            world_mouse_y = mouse_y / state.camera.zoom + state.camera.offset_y

            enemies = state.enemies + list(state.remote_entities.values())
            state.combat.effects.spawn_laser(world_mouse_x, world_mouse_y, enemies)

            state.combat.effects.last_laser_time = now

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
