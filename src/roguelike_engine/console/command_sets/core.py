"""
Comandos core de la consola (help, echo, quit, placeholders).
"""
from __future__ import annotations
import pygame
from typing import TYPE_CHECKING, Any, Optional

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

    # --- PLACEHOLDERS de otras áreas (pueden migrar a sus módulos) ---
    for cmd in ['spawn','kill','teleport','listentities']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.", category='entities')
    for cmd in ['setvar','getvar','listvars']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.", category='vars')
    for cmd in ['save','load']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.", category='io')
    for cmd in ['pause','resume','noclip']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.", category='system')
