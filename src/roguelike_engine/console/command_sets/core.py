"""
Comandos core de la consola (help, echo, quit, placeholders).
"""
from __future__ import annotations
import pygame
from typing import TYPE_CHECKING, Any, Optional
from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import Item as ItemRow

if TYPE_CHECKING:  # solo para type hints sin dependencias en runtime
    from roguelike_engine.console.console_model import CommandRegistry


def register_core_commands(registry: 'CommandRegistry', game: Optional[Any] = None) -> None:
    """Registra comandos básicos y placeholders de sistema/entidades."""
    # --- CORE ---
    def help_cmd(*args: str) -> str:
        # help general
        if not args:
            # Agrupar por categoría
            cats = {}
            for name, meta in registry.metas.items():
                cat = meta.category or 'core'
                cats.setdefault(cat, []).append(name)
            lines = []
            for cat in sorted(cats.keys()):
                cmds = ', '.join(sorted(cats[cat]))
                lines.append(f"[{cat}] {cmds}")
            lines.append("\nUsa: help <comando> para detalles")
            return "\n".join(lines)
        # help de un comando en concreto (resuelve alias)
        q = args[0]
        primary = registry.alias_to_name.get(q, q)
        meta = registry.metas.get(primary)
        if not meta:
            return f"Comando desconocido: {q}"
        lines = [f"{primary}"]
        if meta.usage:
            lines.append(f"Uso: {meta.usage}")
        if meta.help:
            lines.append(meta.help)
        if meta.aliases:
            lines.append(f"Alias: {', '.join(meta.aliases)}")
        if meta.category:
            lines.append(f"Categoría: {meta.category}")
        return "\n".join(lines)

    registry.register(
        'help', help_cmd,
        usage='help [comando]',
        help='Muestra ayuda general o detallada para un comando.',
        category='core',
        aliases=['?', '/help']
    )

    registry.register(
        'echo', lambda *a: ' '.join(a),
        usage='echo <texto...>',
        help='Imprime el texto dado.',
        category='core'
    )
    registry.register(
        'quit', lambda: pygame.event.post(pygame.event.Event(pygame.QUIT)) or '',
        usage='quit',
        help='Cierra el juego (lanza evento QUIT).',
        category='core'
    )

    # --- SYSTEM ---
    def _set_godmode(val: bool) -> str:
        # Set flag tanto en game.state como en world.state (por seguridad)
        try:
            if game is not None:
                setattr(getattr(game, 'state', game), 'godmode', bool(val))
                world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
                if world and hasattr(world, 'state'):
                    setattr(world.state, 'godmode', bool(val))
        except Exception:
            pass
        return 'godmode on' if val else 'godmode off'

    def godmode_cmd(*args: str) -> str:
        # toggle por defecto; acepta on/off/true/false/1/0/toggle
        current = False
        try:
            if game is not None:
                # Leer de world.state si existe, si no de game.state
                world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
                if world and hasattr(world, 'state'):
                    current = bool(getattr(world.state, 'godmode', False))
                else:
                    current = bool(getattr(getattr(game, 'state', game), 'godmode', False))
        except Exception:
            current = False
        val = None
        if args:
            a0 = str(args[0]).lower()
            if a0 in ('on','1','true','yes','y'):
                val = True
            elif a0 in ('off','0','false','no','n'):
                val = False
            elif a0 in ('toggle','switch'):
                val = not current
        if val is None:
            val = not current
        return _set_godmode(val)

    registry.register(
        'godmode', godmode_cmd,
        usage='godmode [on|off|toggle]',
        help='Activa/desactiva modo dios: sin coste de maná, invulnerabilidad total y one-shot en ataques del jugador, además de dash infinito.',
        category='system',
        aliases=['/godmode']
    )

    # --- CHEATS / UTILIDADES ---
    def givememoney_cmd(*args: str) -> str:
        try:
            world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
            player_eid = getattr(world, 'player_entity', None) if world else None
            inv_store = world.components.get('InventoryComponent', {}) if world else {}
            inv = inv_store.get(player_eid)
            if not inv:
                return 'Inventario no disponible'
            inv.add('gold', 100)
            return 'Añadidos 100x gold'
        except Exception:
            return 'Error al añadir gold'

    registry.register(
        'givememoney', givememoney_cmd,
        usage='givememoney',
        help='Añade 100 de gold al inventario del jugador.',
        category='cheats',
        aliases=['/givememoney']
    )

    def restockvendorfood_cmd(*args: str) -> str:
        """Añade N unidades de cada item con type='food' al vendor indicado o al target actual.

        Uso:
          - restockvendorfood <vendor_name|current> [cantidad]
          - Si <vendor_name> es 'current' intenta usar el target actual del chat.
        """
        try:
            world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
            if world is None:
                return 'World no disponible'
            qty = 100
            vendor_eid = None
            if args:
                key = str(args[0]).strip().lower()
            else:
                key = 'current'
            if len(args) >= 2:
                try:
                    qty = max(1, int(args[1]))
                except Exception:
                    qty = 100
            if key in {'current', 'here'}:
                try:
                    vendor_eid = getattr(getattr(world, 'state', None), 'chat_target_eid', None)
                except Exception:
                    vendor_eid = None
            if vendor_eid is None:
                # Buscar por nombre en Identity
                idents = world.components.get('Identity', {})
                for eid, ident in idents.items():
                    try:
                        nm = str(getattr(ident, 'name', '')).lower()
                        if not nm:
                            continue
                        if key in nm:
                            vendor_eid = eid
                            break
                    except Exception:
                        continue
            if vendor_eid is None:
                return f"Vendor no encontrado: {key}"
            inv_store = world.components.get('InventoryComponent', {})
            inv = inv_store.get(vendor_eid)
            if not inv:
                return 'El vendor no tiene inventario'
            # Obtener todos los items type='food' desde SQLite
            food_ids: list[str] = []
            try:
                with session_scope() as s:
                    rows = s.query(ItemRow).filter(ItemRow.type == 'food').all()  # type: ignore[attr-defined]
                    for r in rows:
                        try:
                            iid = str(getattr(r, 'id'))
                            if iid:
                                food_ids.append(iid)
                        except Exception:
                            continue
                # Fallback adicional: si la columna type falla, usar prefijo 'food_'
                if not food_ids:
                    with session_scope() as s:
                        rows = s.query(ItemRow).all()
                        for r in rows:
                            try:
                                iid = str(getattr(r, 'id'))
                                if iid.startswith('food_'):
                                    food_ids.append(iid)
                            except Exception:
                                continue
            except Exception:
                return 'Error consultando items en SQLite'
            added = 0
            for iid in food_ids:
                try:
                    if inv.add(iid, qty):
                        added += 1
                except Exception:
                    continue
            return f"Restock OK: añadidos {qty} de cada uno de {added} items de tipo food."
        except Exception:
            return 'Error en restockvendorfood'

    registry.register(
        'restockvendorfood', restockvendorfood_cmd,
        usage='restockvendorfood <vendor_name|current> [cantidad]',
        help='Añade N unidades de todos los items con type=food al vendor indicado (o al target actual).',
        category='cheats',
        aliases=['/restockvendorfood']
    )

    # --- PLACEHOLDERS de otras áreas (pueden migrar a sus módulos) ---
    for cmd in ['spawn','kill','teleport','listentities']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.", category='entities')
    for cmd in ['setvar','getvar','listvars']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.", category='vars')
    for cmd in ['save','load']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.", category='io')
    for cmd in ['pause','resume','noclip']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.", category='system')
