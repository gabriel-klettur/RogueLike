"""
Contextos de consola relacionados con inventario.
"""
from typing import Any


class InventoryContext:
    def __init__(self, game: Any):
        self.game = game

    def add(self, category: str, key: str, quantity: int) -> str:
        inv = self._get_inv()
        if isinstance(inv, str):
            return inv
        item_id = f"{category}_{key}"
        if item_id not in self.game.items:
            return f"Item desconocido: {item_id}"
        return (
            f"Añadidos {quantity}x {item_id}" if inv.add(item_id, quantity)
            else f"No se pudo añadir {item_id}, inventario lleno"
        )

    def add_direct(self, item_id: str, quantity: int) -> str:
        inv = self._get_inv()
        if isinstance(inv, str):
            return inv
        if item_id not in self.game.items:
            return f"Item desconocido: {item_id}"
        return (
            f"Añadidos {quantity}x {item_id}" if inv.add(item_id, quantity)
            else f"No se pudo añadir {item_id}, inventario lleno"
        )

    def remove(self, category: str, key: str, quantity: int) -> str:
        inv = self._get_inv()
        if isinstance(inv, str):
            return inv
        item_id = f"{category}_{key}"
        if item_id not in self.game.items:
            return f"Item desconocido: {item_id}"
        if not inv.has(item_id, quantity):
            return f"No hay suficiente {item_id}"
        return (
            f"Eliminados {quantity}x {item_id}" if inv.remove(item_id, quantity)
            else f"No se pudo eliminar {item_id}"
        )

    def remove_direct(self, item_id: str, quantity: int) -> str:
        inv = self._get_inv()
        if isinstance(inv, str):
            return inv
        if item_id not in self.game.items:
            return f"Item desconocido: {item_id}"
        if not inv.has(item_id, quantity):
            return f"No hay suficiente {item_id}"
        return (
            f"Eliminados {quantity}x {item_id}" if inv.remove(item_id, quantity)
            else f"No se pudo eliminar {item_id}"
        )

    def edit(self, category: str, key: str, prop: str, value: str) -> str:
        inv = self._get_inv()
        if isinstance(inv, str):
            return inv
        item_id = f"{category}_{key}"
        if item_id not in self.game.items:
            return f"Item desconocido: {item_id}"
        # Validar propiedad y valor antes de buscar en el inventario
        if prop != 'quantity':
            return f"Propiedad desconocida: {prop}"
        try:
            qty = int(value)
        except ValueError:
            return f"Valor inválido: {value}"
        # Buscar el stack y aplicar cambio si existe
        for stack in inv.slots:
            if stack and stack.item_id == item_id:
                stack.quantity = qty
                return f"{item_id} cantidad ajustada a {qty}"
        return f"Item {item_id} no encontrado"

    def edit_direct(self, item_id: str, prop: str, value: str) -> str:
        inv = self._get_inv()
        if isinstance(inv, str):
            return inv
        if item_id not in self.game.items:
            return f"Item desconocido: {item_id}"
        # Validar propiedad y valor antes de buscar en el inventario
        if prop != 'quantity':
            return f"Propiedad desconocida: {prop}"
        try:
            qty = int(value)
        except ValueError:
            return f"Valor inválido: {value}"
        for stack in inv.slots:
            if stack and stack.item_id == item_id:
                stack.quantity = qty
                return f"{item_id} cantidad ajustada a {qty}"
        return f"Item {item_id} no encontrado"

    def list(self) -> str:
        inv = self._get_inv()
        if isinstance(inv, str):
            return inv
        lines = [f"{s.item_id}: {s.quantity}" for s in inv.slots if s]
        return "\n".join(lines) if lines else "Inventario vacío"

    def _get_inv(self):
        comp_store = self.game.ecs.ecs_world.components.get('InventoryComponent', {})
        inv = comp_store.get(self.game.ecs.ecs_world.player_entity)
        return inv or 'Inventario no disponible'
