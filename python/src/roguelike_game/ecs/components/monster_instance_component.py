import uuid


class MonsterInstanceComponent:
    """
    Componente que asigna un identificador único y persistente a cada instancia de monstruo.
    """
    def __init__(self, instance_id: str = None):
        # Generar un UUID si no se proporciona
        self.instance_id = instance_id or str(uuid.uuid4())
