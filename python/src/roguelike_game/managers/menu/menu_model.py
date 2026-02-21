"""
Menu data model for MVC architecture.
"""

class MenuModel:
    """
    Modelo de datos para gestionar opciones y selección del menú.
    """
    def __init__(self, state):
        self.state = state
        self.selected = 0
        self.options = self._generate_options()

    def _generate_options(self):
        """
        Genera la lista de opciones según el estado.
        """
        mode_option = "Modo local" if self.state.mode == "online" else "Modo multijugador"
        return ["Continuar", mode_option, "Configurar Botones", "Salir"]

    def navigate(self, direction):
        """
        Cambia la selección en base a la dirección (+1 o -1).
        """
        self.selected = (self.selected + direction) % len(self.options)

    def select(self):
        """
        Retorna la opción actualmente seleccionada.
        """
        return self.options[self.selected]
