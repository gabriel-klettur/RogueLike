"""Vista principal del Spawner Editor: dibuja overlays y paneles UI.

Responsabilidades clave:
- Renderizar barras (título, toolbar principal, toolbar de instancias).
- Colocar paneles (Templates Manager, Instances, Properties) con anclajes.
- Dibujar overlays (foco de visuales, hints, confirmaciones, picker de visuales).
- Trazar resaltes sobre edificios vinculados (hover/selección, z-tools, split bar).

La orquestación del render se delega a `roguelike_editors.spawner.views.orchestrator`.
"""
from __future__ import annotations

import pygame
from roguelike_editors.buildings.tools.z_tool.z_tool_view import ZToolView
from roguelike_editors.buildings.tools.split_z_tool.split_tool_view import SplitToolView
from roguelike_editors.spawner.views import orchestrate_render


class SpawnerEditorView:
    """Vista responsable de dibujar los overlays del Spawner Editor.

    Mantiene las preocupaciones de dibujo separadas de la lógica de eventos/input.
    La lógica de orquestación del render se centraliza en `views.orchestrator`.
    """
    def __init__(self, controller):
        """Inicializa la vista y recursos de UI reutilizados.

        Args:
            controller: Fachada `SpawnerEditorController` para acceder a estado y sub-vistas.
        """
        self.controller = controller
        # Small font for ID label (lazy)
        try:
            self._id_font = pygame.font.Font(None, 16)
        except Exception:
            self._id_font = None
        # Reuse Building Editor Z tool views for UI parity
        try:
            self._z_bottom_view = ZToolView(None, None, target="bottom")
            self._z_top_view = ZToolView(None, None, target="top")
        except Exception:
            self._z_bottom_view = None
            self._z_top_view = None
        # Split bar view (visual split ratio control)
        try:
            self._split_view = SplitToolView(None, None)
        except Exception:
            self._split_view = None

    # --- Internal helpers ---------------------------------------------------
    def _find_building_entity_by_id_world(self, bid: int):
        """Resuelve un edificio por ID consultando directamente el mundo ECS.

        Esto hace a la vista más resiliente cuando el panel de Visuals no está disponible.
        """
        try:
            world = getattr(getattr(self.controller.game, 'ecs', None), 'ecs_world', None)
        except Exception:
            world = None
        if world is None:
            return None
        try:
            for ob in getattr(world, 'buildings', []) or []:
                try:
                    if int(getattr(ob, 'id', None)) == int(bid):
                        return ob
                except Exception:
                    continue
        except Exception:
            pass
        return None

    def _reset_last_rects(self) -> None:
        """Reinicia los rects caché de la UI para el frame actual."""
        try:
            self._last_title_rect = None
            self._last_toolbar_rect = None
            self._last_instance_toolbar_rect = None
            self._last_manager_rect = None
            self._last_instances_rect = None
            self._last_properties_rect = None
            self._last_selected_delete_rect = None
            self._last_selected_resize_rect = None
            self._last_selected_reset_rect = None
            self._last_z_bottom_minus_rect = None
            self._last_z_bottom_plus_rect = None
            self._last_z_top_minus_rect = None
            self._last_z_top_plus_rect = None
            self._last_split_handle_rect = None
        except Exception:
            pass

    def render(self, screen: pygame.Surface) -> None:
        """Dibuja los overlays/paneles del editor delegando en `views.orchestrator`."""
        orchestrate_render(self, screen)
