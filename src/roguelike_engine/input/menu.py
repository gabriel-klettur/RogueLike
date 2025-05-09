
def execute_menu_option(selected, state, entities):
    if selected == "Cambiar personaje":
        new = "valkyria" if entities.player.character_name=="first_hero" else "first_hero"
        entities.player.change_character(new)
        print(f"✅ Cambiado a personaje: {new}")
        state.show_menu = False

    elif selected == "Salir":
        state.running = False

    elif selected in ("Modo multijugador","Modo local"):
        _toggle_mode(state)

def _toggle_mode(state):
    """
    Cambia entre local y online usando NetworkManager.
    """
    if state.mode == "local":
        state.mode = "online"
        print("🌐 Conectando al servidor...")
        state.network.connect()
    else:
        state.mode = "local"
        print("🔌 Cambiando a modo local...")
        state.network.disconnect()
# Path: src/roguelike_engine/input/menu.py