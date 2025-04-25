import pygame
import logging

from src.roguelike_project.systems.editor.buildings.model.persistence.json_handler import save_buildings_to_json
from src.roguelike_project.config import BUILDINGS_DATA_PATH

logger = logging.getLogger("building_editor.events")

class BuildingEditorEventHandler:
    """
    Manejador de eventos para el Building Editor en modo MVC.
    """
    def __init__(self, state, editor_state, controller):
        self.state = state
        self.editor = editor_state
        self.controller = controller

    def handle(self):
        """Procesa todos los eventos de Pygame"""
        for ev in pygame.event.get():
            if ev.type == pygame.QUIT:
                self._on_quit(ev)
            elif ev.type == pygame.KEYDOWN:
                self._on_keydown(ev)
            elif ev.type == pygame.MOUSEBUTTONDOWN:
                self._on_mouse_down(ev)
            elif ev.type == pygame.MOUSEBUTTONUP:
                self._on_mouse_up(ev)
            elif ev.type == pygame.MOUSEMOTION:
                self._on_mouse_motion(ev)

    def _on_quit(self, ev):
        logger.info("Quit event received in Building Editor")
        self.state.running = False

    def _on_keydown(self, ev):
        # ESC: salir del editor y guardar
        if ev.key == pygame.K_ESCAPE:
            logger.info("Escape: closing Building Editor and saving")
            self.editor.active = False
            self.editor.selected_building = None
            self.editor.dragging = False
            self.editor.resizing = False
            self.editor.split_dragging = False
            save_buildings_to_json(self.state.buildings, BUILDINGS_DATA_PATH, z_state=self.state.z_state)

        # F10: toggle editor
        elif ev.key == pygame.K_F10:
            self.editor.active = not self.editor.active
            logger.info(f"Building Editor {'ON' if self.editor.active else 'OFF'} via F10")
            if not self.editor.active:
                save_buildings_to_json(self.state.buildings, BUILDINGS_DATA_PATH, z_state=self.state.z_state)

        # Ctrl+S: guardar sin salir
        elif ev.key == pygame.K_s and (ev.mod & pygame.KMOD_CTRL):
            logger.info("Ctrl+S: saving buildings")
            save_buildings_to_json(self.state.buildings, BUILDINGS_DATA_PATH, z_state=self.state.z_state)

        # N: colocar edificio
        elif ev.key == pygame.K_n:
            self.controller.placer_tool.place_building_at_mouse()

        # Supr: borrar edificio
        elif ev.key == pygame.K_DELETE:
            self.controller.delete_tool.delete_building_at_mouse()

    def _on_mouse_down(self, ev):
        mx, my = pygame.mouse.get_pos()
        self.controller.on_mouse_down((mx, my), ev.button)

    def _on_mouse_up(self, ev):
        self.controller.on_mouse_up(ev.button)

    def _on_mouse_motion(self, ev):
        self.controller.on_mouse_motion(ev.pos)
