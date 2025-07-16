import sys
import pygame
from typing import Any
from roguelike_engine.console.model.model import CommandRegistry


def register_commands(registry: CommandRegistry, game: Any = None) -> None:
    """
    Registra los 20 comandos básicos de la consola.
    Parámetros opcionales:
        game: instancia del juego para handlers que requieran acceso al estado.
    """
    # --- CORE COMMANDS ---
    def help_handler() -> str:
        """Lista todos los comandos disponibles."""
        return '\n'.join(sorted(registry.commands.keys()))
    registry.register('help', help_handler)

    def echo_handler(*args: str) -> str:
        """Repite el texto suministrado."""
        return ' '.join(args)
    registry.register('echo', echo_handler)

    def quit_handler() -> str:
        """Cierra el juego inmediatamente."""
        pygame.event.post(pygame.event.Event(pygame.QUIT))
        return ''
    registry.register('quit', quit_handler)

    # --- ULTIMA ONLINE INSPIRED ---
    # Ítems
    for cmd in ['add', 'remove', 'edit', 'listitems']:
        registry.register(cmd, lambda *args, name=cmd: f'[{name}] Comando implementado próximamente.')

    # Entidades
    for cmd in ['spawn', 'kill', 'teleport', 'listentities']:
        registry.register(cmd, lambda *args, name=cmd: f'[{name}] Comando implementado próximamente.')

    # Variables y estado
    for cmd in ['setvar', 'getvar', 'listvars']:
        registry.register(cmd, lambda *args, name=cmd: f'[{name}] Comando implementado próximamente.')

    # Guardado y carga
    for cmd in ['save', 'load']:
        registry.register(cmd, lambda *args, name=cmd: f'[{name}] Comando implementado próximamente.')

    # Control de ejecución
    registry.register('pause', lambda: '[pause] Comando implementado próximamente.')
    registry.register('resume', lambda: '[resume] Comando implementado próximamente.')

    # Debug y cheats
    registry.register('godmode', lambda *args: '[godmode] Comando implementado próximamente.')
    registry.register('noclip', lambda *args: '[noclip] Comando implementado próximamente.')
