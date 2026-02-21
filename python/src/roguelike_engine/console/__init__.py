"""Paquete de consola (quake-like) para el motor.

API pública:
- ConsoleState, CommandRegistry (modelo)
- ConsoleController (controlador)
- ConsoleView (vista)
- ConsoleEvents (router de eventos)
- register_commands (agregador de comandos)
"""
from .console_model import ConsoleState, CommandRegistry
from .console_controller import ConsoleController
from .console_view import ConsoleView
from .console_events import ConsoleEvents
from .command_sets import register_commands
