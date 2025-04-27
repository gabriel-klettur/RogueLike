
def execute_menu_option(selected, state):
    if selected == "Cambiar personaje":
        new = "valkyria" if state.player.character_name=="first_hero" else "first_hero"
        state.player.change_character(new)
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