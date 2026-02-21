"""
Comandos de inventario para la consola.
"""
from __future__ import annotations
from typing import Any, Optional, List
from roguelike_engine.console.contexts.inventory import InventoryContext

if False:  # para linters que validan imports no usados cuando solo tipamos
    from roguelike_engine.console.console_model import CommandRegistry


def _context_completer(args: List[str]) -> List[str]:
    # Completa el primer argumento (nombre del contexto)
    options = ['inventory']
    if not args:
        return options
    prefix = args[-1]
    return [o for o in options if o.startswith(prefix)]


def register_inventory_commands(registry: 'CommandRegistry', game: Optional[Any] = None) -> None:
    """Registra comandos de inventario. Si game es None, registra stubs informativos."""
    if game is None:
        # Registrar comandos que informan que el contexto no está disponible
        def _na(*args: str, **kwargs: str) -> str:
            return 'Inventario no disponible (contexto no inicializado)'
        for name in ['add', 'remove', 'edit', 'list', 'listitems']:
            registry.register(name, _na, category='inventory')
        return

    ctx = InventoryContext(game)
    contexts = {
        'inventory': ctx,
    }

    def add_cmd(*args: str) -> str:
        if len(args) < 2:
            return 'Uso: add <contexto> <item|categoria item> [cantidad]'
        context_name = args[0]
        handler = contexts.get(context_name)
        if not handler:
            return f"Contexto desconocido: {context_name}"
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

    registry.register(
        'add', add_cmd,
        usage='add <contexto> <item_id> [cantidad] | add <contexto> <categoria> <item> [cantidad]',
        help='Añade ítems al inventario del jugador.',
        category='inventory',
        completer=_context_completer
    )

    def remove_cmd(*args: str) -> str:
        if len(args) < 2:
            return 'Uso: remove <contexto> <item|categoria item> [cantidad]'
        context_name = args[0]
        handler = contexts.get(context_name)
        if not handler:
            return f"Contexto desconocido: {context_name}"
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

    registry.register(
        'remove', remove_cmd,
        usage='remove <contexto> <item_id> [cantidad] | remove <contexto> <categoria> <item> [cantidad]',
        help='Elimina ítems del inventario del jugador.',
        category='inventory',
        completer=_context_completer
    )

    def edit_cmd(*args: str) -> str:
        if len(args) < 3:
            return 'Uso: edit <contexto> <item|categoria item> <prop> <valor>'
        context_name = args[0]
        handler = contexts.get(context_name)
        if not handler:
            return f"Contexto desconocido: {context_name}"
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

    registry.register(
        'edit', edit_cmd,
        usage='edit <contexto> <item_id> <prop> <valor> | edit <contexto> <categoria> <item> <prop> <valor>',
        help='Modifica propiedades de un ítem ya presente en inventario.',
        category='inventory',
        completer=_context_completer
    )

    def list_cmd(*args: str) -> str:
        if len(args) < 1:
            return 'Uso: list <contexto>'
        context_name = args[0]
        handler = contexts.get(context_name)
        return handler.list() if handler else f"Contexto desconocido: {context_name}"

    registry.register(
        'list', list_cmd,
        usage='list <contexto>',
        help='Lista el contenido del inventario.',
        category='inventory',
        completer=_context_completer
    )
    registry.register('listitems', list_cmd, aliases=['lsinv'], category='inventory', help='Alias de list <contexto>.')
