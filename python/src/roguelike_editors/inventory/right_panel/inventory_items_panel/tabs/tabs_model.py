from dataclasses import dataclass

@dataclass
class TabsModel:
    """
    Modelo para gestionar las pestañas de inventario (default/active).
    """
    # Pestaña activa ('default' o 'active')
    active_tab: str = 'default'
    # Pestañas disponibles
    available_tabs: list = None
    
    def __post_init__(self):
        if self.available_tabs is None:
            self.available_tabs = ['default', 'active']
