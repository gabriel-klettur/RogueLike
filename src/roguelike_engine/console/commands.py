import sys
import pygame
from typing import Any
from roguelike_engine.console.model.model import CommandRegistry


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
        return f"Añadidos {quantity}x {item_id}" if inv.add(item_id, quantity) else f"No se pudo añadir {item_id}, inventario lleno"

    def add_direct(self, item_id: str, quantity: int) -> str:
        inv = self._get_inv()
        if isinstance(inv, str):
            return inv
        if item_id not in self.game.items:
            return f"Item desconocido: {item_id}"
        return f"Añadidos {quantity}x {item_id}" if inv.add(item_id, quantity) else f"No se pudo añadir {item_id}, inventario lleno"

    def remove(self, category: str, key: str, quantity: int) -> str:
        inv = self._get_inv()
        if isinstance(inv, str):
            return inv
        item_id = f"{category}_{key}"
        if item_id not in self.game.items:
            return f"Item desconocido: {item_id}"
        if not inv.has(item_id, quantity):
            return f"No hay suficiente {item_id}"
        return f"Eliminados {quantity}x {item_id}" if inv.remove(item_id, quantity) else f"No se pudo eliminar {item_id}"

    def remove_direct(self, item_id: str, quantity: int) -> str:
        inv = self._get_inv()
        if isinstance(inv, str):
            return inv
        if item_id not in self.game.items:
            return f"Item desconocido: {item_id}"
        if not inv.has(item_id, quantity):
            return f"No hay suficiente {item_id}"
        return f"Eliminados {quantity}x {item_id}" if inv.remove(item_id, quantity) else f"No se pudo eliminar {item_id}"

    def edit(self, category: str, key: str, prop: str, value: str) -> str:
        inv = self._get_inv()
        if isinstance(inv, str):
            return inv
        item_id = f"{category}_{key}"
        if item_id not in self.game.items:
            return f"Item desconocido: {item_id}"
        for stack in inv.slots:
            if stack and stack.item_id == item_id:
                if prop == 'quantity':
                    try:
                        qty = int(value)
                    except ValueError:
                        return f"Valor inválido: {value}"
                    stack.quantity = qty
                    return f"{item_id} cantidad ajustada a {qty}"
                return f"Propiedad desconocida: {prop}"
        return f"Item {item_id} no encontrado"

    def edit_direct(self, item_id: str, prop: str, value: str) -> str:
        inv = self._get_inv()
        if isinstance(inv, str):
            return inv
        if item_id not in self.game.items:
            return f"Item desconocido: {item_id}"
        for stack in inv.slots:
            if stack and stack.item_id == item_id:
                if prop == 'quantity':
                    try:
                        qty = int(value)
                    except ValueError:
                        return f"Valor inválido: {value}"
                    stack.quantity = qty
                    return f"{item_id} cantidad ajustada a {qty}"
                return f"Propiedad desconocida: {prop}"
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


def register_commands(registry: CommandRegistry, game: Any = None) -> None:
    """
    Registra los comandos de la consola, con arquitectura escalable vía context handlers.
    """
    # Contextos disponibles
    contexts = {
        'inventory': InventoryContext(game),
    }

    # --- CORE ---
    registry.register('help', lambda: '\n'.join(sorted(registry.commands.keys())))
    registry.register('echo', lambda *a: ' '.join(a))
    registry.register('quit', lambda: pygame.event.post(pygame.event.Event(pygame.QUIT)) or '')

    # --- ITEMS ---
    def add_cmd(*args: str) -> str:
        if len(args) < 2:
            return 'Uso: add <contexto> <item|categoria item> [cantidad]'
        ctx = args[0]
        handler = contexts.get(ctx)
        if not handler:
            return f"Contexto desconocido: {ctx}"
        rest = args[1:]
        # Sintaxis: add <contexto> <item_id> [cantidad]
        if len(rest) == 1 or (len(rest) >= 2 and rest[1].isdigit()):
            item_id = rest[0]
            qty = int(rest[1]) if len(rest) > 1 and rest[1].isdigit() else 1
            return handler.add_direct(item_id, qty)
        # Sintaxis: add <contexto> <categoria> <item> [cantidad]
        category, key = rest[0], rest[1]
        qty = int(rest[2]) if len(rest) > 2 and rest[2].isdigit() else 1
        return handler.add(category, key, qty)
    registry.register('add', add_cmd)

    def remove_cmd(*args: str) -> str:
        if len(args) < 2:
            return 'Uso: remove <contexto> <item|categoria item> [cantidad]'
        ctx = args[0]
        handler = contexts.get(ctx)
        if not handler:
            return f"Contexto desconocido: {ctx}"
        rest = args[1:]
        # Sintaxis: remove <contexto> <item_id> [cantidad]
        if len(rest) == 1 or (len(rest) >= 2 and rest[1].isdigit()):
            item_id = rest[0]
            qty = int(rest[1]) if len(rest) > 1 and rest[1].isdigit() else 1
            return handler.remove_direct(item_id, qty)
        # Sintaxis: remove <contexto> <categoria> <item> [cantidad]
        category, key = rest[0], rest[1]
        qty = int(rest[2]) if len(rest) > 2 and rest[2].isdigit() else 1
        return handler.remove(category, key, qty)
    registry.register('remove', remove_cmd)

    def edit_cmd(*args: str) -> str:
        if len(args) < 3:
            return 'Uso: edit <contexto> <item|categoria item> <prop> <valor>'
        ctx = args[0]
        handler = contexts.get(ctx)
        if not handler:
            return f"Contexto desconocido: {ctx}"
        rest = args[1:]
        # Sintaxis: edit <contexto> <item_id> <prop> <valor>
        if len(rest) == 3:
            item_id, prop, val = rest[0], rest[1], rest[2]
            return handler.edit_direct(item_id, prop, val)
        # Sintaxis: edit <contexto> <categoria> <item> <prop> <valor>
        if len(rest) >= 4:
            cat, key, prop, val = rest[0], rest[1], rest[2], rest[3]
            return handler.edit(cat, key, prop, val)
        return 'Uso: edit <contexto> <item|categoria item> <prop> <valor>'
    registry.register('edit', edit_cmd)

    def list_cmd(*args: str) -> str:
        if len(args) < 1:
            return 'Uso: list <contexto>'
        ctx = args[0]
        handler = contexts.get(ctx)
        return handler.list() if handler else f"Contexto desconocido: {ctx}"
    registry.register('list', list_cmd)
    registry.register('listitems', list_cmd)

    # --- ENTITIES, VARS, SAVE/LOAD, ETC. ---
    for cmd in ['spawn','kill','teleport','listentities',
                'setvar','getvar','listvars',
                'save','load']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.")
    for cmd in ['pause','resume','godmode','noclip']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.")

