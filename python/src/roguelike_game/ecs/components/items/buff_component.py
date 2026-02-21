class BuffComponent:
    """
    Componente que aplica un buff de atributo temporal.
    """
    def __init__(self, stat: str, value: float, duration: float):
        self.stat = stat
        self.value = value
        self.duration = duration
