"""Orquestador de eventos del Spawner Editor.

Este módulo mantiene estable la importación externa::
    from roguelike_editors.spawner.spawner_editor_events import SpawnerEditorEventHandler

y a la vez actúa como una fachada/orquestador sobre la implementación
`events.handler.SpawnerEditorEventHandler`, dejando un punto único para
agregar lógica transversal (logging, métricas, feature flags, etc.).
"""

from __future__ import annotations

from typing import Any
import pygame

from .events.handler import SpawnerEditorEventHandler as _CoreHandler


class SpawnerEditorEventHandler:
    """Fachada-orquestador del manejador de eventos del Spawner Editor.

    Delegamos en la implementación "core" (`events.handler.SpawnerEditorEventHandler`)
    y mantenemos aquí un punto de extensión para lógica transversal u orquestación
    entre múltiples submanejadores.
    """

    def __init__(self, controller: Any) -> None:
        """Inicializa el orquestador con el controlador de alto nivel.

        Args:
            controller: Instancia del `SpawnerEditorController`.
        """
        self._core = _CoreHandler(controller)
        self.controller = controller

    # Ciclo de vida -----------------------------------------------------------
    def set_game(self, game: Any) -> None:
        """Propaga la instancia de juego al manejador core."""
        try:
            self._core.set_game(game)
        except Exception:
            pass

    def toggle_visible(self) -> None:
        """Alterna la visibilidad del editor delegando en el core handler."""
        try:
            self._core.toggle_visible()
        except Exception:
            # Fallback: minimizar efectos si algo falla
            try:
                mdl = getattr(self.controller, 'model', None)
                if mdl is not None:
                    mdl.visible = not bool(getattr(mdl, 'visible', False))
            except Exception:
                pass

    # Enrutado de eventos -----------------------------------------------------
    def handle_event(self, event: pygame.event.Event) -> bool:
        """Orquesta el enrutado de eventos delegando en el core handler.

        Returns:
            True si el evento fue consumido; False en caso contrario.
        """
        try:
            return bool(self._core.handle_event(event))
        except Exception:
            return False

