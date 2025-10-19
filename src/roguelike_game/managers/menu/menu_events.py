"""
Definiciones de eventos para interacción con el menú.
"""

class MenuEvent:
    """Evento base para el sistema de menú."""
    pass

class NavigateEvent(MenuEvent):
    """Evento de navegación: direction es +1 o -1."""
    def __init__(self, direction):
        self.direction = direction

class SelectEvent(MenuEvent):
    """Evento de selección de opción: index es el índice de la opción."""
    def __init__(self, index):
        self.index = index

class ConfigureEvent(MenuEvent):
    """Evento para invocar configuración de botones."""
    pass

class ExitEvent(MenuEvent):
    """Evento para indicar salida del menú."""
    pass
