import pygame
from roguelike_project.network.client import WebSocketClient  # ✅ Importación necesaria

def handle_events(state):
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            state.running = False

        elif event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE:
                state.show_menu = not state.show_menu
            elif event.key == pygame.K_q:
                state.player.restore_all()
            elif state.show_menu:
                result = state.menu.handle_input(event)
                if result:
                    execute_menu_option(result, state)
        elif event.type == pygame.MOUSEWHEEL:
            if event.y > 0:
                state.camera.zoom = min(state.camera.zoom + 0.1, 2.0)  # Zoom in
            elif event.y < 0:
                state.camera.zoom = max(state.camera.zoom - 0.1, 0.5)  # Zoom out

    if not state.show_menu:
        keys = pygame.key.get_pressed()
        dx = dy = 0
        if keys[pygame.K_UP]: dy = -1
        if keys[pygame.K_DOWN]: dy = 1
        if keys[pygame.K_LEFT]: dx = -1
        if keys[pygame.K_RIGHT]: dx = 1
        solid_tiles = [tile for tile in state.tiles if tile.solid]
        state.player.move(dx, dy, state.obstacles, solid_tiles)

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

            # ✅ Conectar WebSocket si no estaba conectado
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

            # ✅ Desconectar WebSocket si estaba activo
            if hasattr(state, "websocket") and state.websocket:
                try:
                    state.websocket.stop()
                    print("🧯 WebSocket desconectado.")
                except Exception as e:
                    print(f"❌ Error al cerrar WebSocket: {e}")
                state.websocket = None
                state.websocket_connected = False

        state.show_menu = False
