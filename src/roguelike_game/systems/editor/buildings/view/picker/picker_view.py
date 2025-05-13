# Path: src/roguelike_game/systems/editor/buildings/view/picker/picker_view.py

import os
import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_game.systems.editor.buildings.buildings_editor_config import (
    THUMB_SIZE, THUMB_PADDING, NAV_HEIGHT,
    COLOR_BG, COLOR_BORDER, COLOR_HIGHLIGHT, ICON_BACK
)
from ...controller.picker.picker_controller import DirEntry



class PickerView:
    def __init__(self, editor_state):
        self.editor = editor_state
        # Carga icono de “atrás”
        self.back_icon = load_image(ICON_BACK, (NAV_HEIGHT, NAV_HEIGHT))
        # Cache de miniaturas
        self.thumb_cache: dict[str, pygame.Surface] = {}

    def render(self, screen, camera):
        sw, sh = screen.get_size()
        # 1) Fondo
        screen.fill(COLOR_BG)

        # 2) Barra de navegación
        self._draw_nav_bar(screen)

        # 3) Thumbnails
        self._draw_thumbnails(screen)

        # 4) Preview de drag (si aplica)
        if self.editor.dragging_building and self.editor.selected_entry:
            self._draw_drag_preview(screen)

    def _draw_nav_bar(self, screen):
        # Fondo
        nav_rect = pygame.Rect(0, 0, screen.get_width(), NAV_HEIGHT)
        pygame.draw.rect(screen, COLOR_BORDER, nav_rect)
        # Icono de “atrás”
        screen.blit(self.back_icon, (0, 0))
        # Breadcrumbs
        parts = self.editor.current_dir.replace('\\','/').split('/')
        x = NAV_HEIGHT + 10
        font = pygame.font.SysFont(None, NAV_HEIGHT - 4)
        for part in parts:
            txt = font.render(part, True, COLOR_HIGHLIGHT)
            screen.blit(txt, (x, (NAV_HEIGHT - txt.get_height())//2))
            x += txt.get_width() + 10

    def _draw_thumbnails(self, screen):
        entries = self.editor.entries
        sw = screen.get_width()
        cols = max(1, sw // (THUMB_SIZE + THUMB_PADDING))
        for idx, entry in enumerate(entries):
            row = idx // cols
            col = idx % cols
            x = col * (THUMB_SIZE + THUMB_PADDING) + THUMB_PADDING//2
            y = NAV_HEIGHT + row * (THUMB_SIZE + THUMB_PADDING) + THUMB_PADDING//2
            rect = pygame.Rect(x, y, THUMB_SIZE, THUMB_SIZE)

            # Fondo y borde
            color = COLOR_HIGHLIGHT if entry == self.editor.selected_entry else COLOR_BORDER
            pygame.draw.rect(screen, color, rect, 1)

            # Si es directorio, dibujar un icono de carpeta
            if entry.is_dir:
                # Puedes usar un icono de carpeta o un rectángulo relleno
                pygame.draw.rect(screen, COLOR_BORDER, rect.inflate(-10,-10))
            else:
                # Cargar miniatura de la imagen
                thumb = self.thumb_cache.get(entry.path)
                if not thumb:
                    img = load_image(entry.path)
                    thumb = pygame.transform.scale(img, (THUMB_SIZE, THUMB_SIZE))
                    self.thumb_cache[entry.path] = thumb
                screen.blit(thumb, (x, y))

    def _draw_drag_preview(self, screen):
        mx, my = pygame.mouse.get_pos()
        entry = self.editor.selected_entry
        img = load_image(entry.path)
        # Escalamos al THUMB_SIZE*2 para preview (por ejemplo)
        w = THUMB_SIZE * 2
        h = img.get_height() * w // img.get_width()
        surf = pygame.transform.scale(img, (w, h))
        # Dibujamos semitransparente
        surf.set_alpha(200)
        screen.blit(surf, (mx - w//2, my - h//2))
