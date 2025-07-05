from typing import List, Optional, Dict
from roguelike_game.ecs.components.item_models import ItemStack


class InventoryComponent:
    """
    Componente ECS que gestiona el inventario de una entidad.
    """
    def __init__(self, capacity: int = 20, player_id: Optional[str] = None):
        self.player_id = player_id
        self.capacity = capacity
        # Lista de ItemStack o None
        self.slots: List[Optional[ItemStack]] = [None] * capacity

    def add(self, item_id: str, qty: int) -> bool:
        """
        Añade qty del item_id. Retorna True si se añadió completamente.
        """
        remaining = qty
        # Apilar en ranuras existentes
        for stack in self.slots:
            if stack and stack.item_id == item_id:
                stack.quantity += remaining
                return True
        # Crear nueva pila en ranura vacía
        for idx, stack in enumerate(self.slots):
            if stack is None:
                self.slots[idx] = ItemStack(item_id, remaining)
                return True
        return False

    def has(self, item_id: str, qty: int) -> bool:
        """
        Retorna True si hay al menos qty del item_id en el inventario.
        """
        total = sum(stack.quantity for stack in self.slots if stack and stack.item_id == item_id)
        return total >= qty

    def remove(self, item_id: str, qty: int) -> bool:
        """
        Elimina qty del item_id. Retorna False si no hay suficiente.
        """
        if not self.has(item_id, qty):
            return False
        remaining = qty
        for idx, stack in enumerate(self.slots):
            if stack and stack.item_id == item_id:
                if stack.quantity > remaining:
                    stack.quantity -= remaining
                    return True
                else:
                    remaining -= stack.quantity
                    self.slots[idx] = None
                    if remaining == 0:
                        return True
        return True

    def serialize(self) -> Dict:
        """
        Serializa el inventario a un dict para persistencia o UI.
        """
        data: Dict = {
            "player_id": self.player_id,
            "capacity": self.capacity,
            "slots": []
        }
        for stack in self.slots:
            if stack:
                data["slots"].append({
                    "item": stack.item_id,
                    "quantity": stack.quantity
                })
            else:
                data["slots"].append(None)
        return data
