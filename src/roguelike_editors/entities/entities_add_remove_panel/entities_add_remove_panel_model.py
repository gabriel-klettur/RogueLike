from roguelike_editors.entities.services.constants import (
    ADD_ENTITIE,
    REMOVE_ENTITIE,
    ADD_ENTITIES_ON_SYSTEM,
    CONFIRM_ADD_ENTITY_ON_SYSTEM,
)

class EntitiesAddRemovePanelModel:
    """
    Modelo para el panel de añadir/eliminar entidades en el mapa.
    """
    def __init__(self):
        # Claves para las herramientas de añadir y eliminar
        self.tools = [
            ADD_ENTITIE,
            REMOVE_ENTITIE,
            ADD_ENTITIES_ON_SYSTEM,
            CONFIRM_ADD_ENTITY_ON_SYSTEM,
        ]
        # Herramienta activa
        self.active_tool = None
