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
        # Icono de “atrás”
        self.back_icon = load_image(ICON_BACK, (NAV_HEIGHT, NAV_HEIGHT))
        # Icono de carpeta
        self.folder_icon = load_image("assets/ui/folder_win.png")
        self.folder_icon = pygame.transform.scale(self.folder_icon, (THUMB_SIZE, THUMB_SIZE))
        # Fuente para el label de las carpetas
        self._label_font = pygame.font.SysFont(None, THUMB_SIZE // 4)
        # Cache de miniaturas
        self.thumb_cache: dict[str, pygame.Surface] = {}

    def render(self, screen, camera):
        sw, sh = screen.get_size()

        # 1) Panel semitransparente de fondo (no sobre toda la pantalla)
        #    Por ejemplo, un rectángulo negro al 60% de opacidad en la parte superior:
        panel_h = NAV_HEIGHT + ((len(self.editor.entries)-1)//(sw//(THUMB_SIZE+THUMB_PADDING)) + 1) * (THUMB_SIZE+THUMB_PADDING)
        overlay = pygame.Surface((sw, panel_h), pygame.SRCALPHA)
        overlay.fill((30, 30, 30, 180))  # (R, G, B, A)
        screen.blit(overlay, (0, 0))

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
        # Etiqueta fija
        font = pygame.font.SysFont(None, NAV_HEIGHT - 4)
        label = "Carpeta actual:"
        lbl_surf = font.render(label, True, (0,0,0))
        x = NAV_HEIGHT + 10
        screen.blit(lbl_surf, (x, (NAV_HEIGHT - lbl_surf.get_height()) // 2))
        x += lbl_surf.get_width() + 20  # un poco de espacio tras la etiqueta

        # Ahora los elementos del path
        parts = self.editor.current_dir.replace('\\','/').split('/')
        for part in parts:
            txt = font.render(part, True, (0, 0, 0))
            screen.blit(txt, (x, (NAV_HEIGHT - txt.get_height()) // 2))
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

            # Borde
            color = COLOR_HIGHLIGHT if entry == self.editor.selected_entry else COLOR_BORDER
            pygame.draw.rect(screen, color, rect, 1)

            if entry.is_dir:
                # Icono de carpeta
                screen.blit(self.folder_icon, (x, y))
                # Label centrado en negro
                label_color = (0, 0, 0)
                label_surf = self._label_font.render(entry.name, True, label_color)
                label_rect = label_surf.get_rect(center=rect.center)
                screen.blit(label_surf, label_rect)
            else:
                # Imagen normal
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
