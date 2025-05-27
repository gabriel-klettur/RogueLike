class AttackCooldown:
    """
    Controla cuándo puede volver a atacar.
    """
    def __init__(self, next_time: float = 0.0):
        self.next_time = next_time
