from typing import Optional

class PhysicalItemComponent:
    """Componente que representa un montón de ítems en el suelo."""
    def __init__(self, drop_id: str, item_id: str, quantity: int, zone_id: Optional[str] = None, created_at: Optional[float] = None):
        self.drop_id = drop_id
        self.item_id = item_id
        self.quantity = quantity
        self.zone_id = zone_id
        # Epoch seconds cuando se creó el drop en el mapa
        self.created_at = created_at
