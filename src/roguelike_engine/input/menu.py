# Path: src/roguelike_engine/input/menu.py
def execute_menu_option(selected, state):
    if selected == "Salir":
        state.running = False

    elif selected in ("Modo multijugador","Modo local"):
        _toggle_mode(state)

def _toggle_mode(state):
    """
    Cambia entre local y online usando NetworkManager.
    """
    if state.mode == "online":
        #state.mode = "online"
        print("🌐 Conectando al servidor...")
        #state.network.connect()
        print("Conectado al servidor.")
        print("Desconectando del servidor...")
        state.mode = "online"
    else:
        state.mode = "local"
        print("🔌 Cambiando a modo local...")
        #state.network.disconnect()
