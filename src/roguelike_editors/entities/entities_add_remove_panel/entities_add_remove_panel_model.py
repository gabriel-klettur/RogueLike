class EntitiesAddRemovePanelModel:
    """
    Modelo para el panel de añadir/eliminar entidades en el mapa.
    """
    def __init__(self):
        # Claves para las herramientas de añadir y eliminar
        self.tools = [
            'add_entitie',
            'remove_entitie',
        ]
        # Herramienta activa
        self.active_tool = None
