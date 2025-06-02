import pygame
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
import pygame
from roguelike_game.systems.editor.map.map_editor_controller import MapToolbarController
from roguelike_game.systems.editor.tiles.tiles_editor_config import BTN_W, BTN_H
from roguelike_engine.map.model.layer import Layer

class MapEditorView:
    """
    Vista para el Map Editor: dibuja zonas, etiquetas y resaltados.
    """
    def __init__(self, controller, state, map_manager):
        self.controller = controller
        self.state = state
        self.map_manager = map_manager
        # fuente para etiquetas, 3x tamaño más grande
        base_size = 16
        self.font = pygame.font.SysFont(None, base_size * 3)

    def render(self, screen, camera, map_manager):
        if not self.state.active:
            return
        zones = global_map_settings.zone_offsets
        zone_w, zone_h = global_map_settings.zone_size
        for zone, (ox, oy) in zones.items():
            # ocultar si está marcado
            hidden = zone in self.state.hidden_zones
            # color y alpha según estado
            if hidden:
                outline_color = (100, 100, 100)
                fill_color = (*outline_color, 50)
            else:
                if zone == self.state.selected_zone:
                    outline_color = (0, 255, 0)
                else:
                    outline_color = (0, 128, 255)
                fill_color = (*outline_color, 50)
            # rect en píxeles globales
            px, py = ox * TILE_SIZE, oy * TILE_SIZE
            pw, ph = zone_w * TILE_SIZE, zone_h * TILE_SIZE
            # convertir a coordenadas de pantalla
            screen_tl = camera.apply((px, py))
            screen_size = camera.scale((pw, ph))
            # dibujo semitransparente
            surf = pygame.Surface(screen_size, pygame.SRCALPHA)
            surf.fill(fill_color)
            screen.blit(surf, screen_tl)
            # borde
            pygame.draw.rect(screen, outline_color, (*screen_tl, *screen_size), 2)
            # etiqueta centrada y más grande
            if self.state.renaming_zone == zone:
                text = self.state.rename_input or ''
                # calcular rect de zona en pantalla
                px, py = ox * TILE_SIZE, oy * TILE_SIZE
                pw, ph = zone_w * TILE_SIZE, zone_h * TILE_SIZE
                screen_tl = camera.apply((px, py))
                screen_size = camera.scale((pw, ph))
                total_w = screen_size[0]
                # renderizar texto de entrada
                input_surf = self.font.render(text, True, (0, 0, 0))
                text_h = input_surf.get_height()
                padding_y = 4
                box_h = max(text_h + padding_y * 2, BTN_H)
                # calcular anchuras
                accept_w = box_h * 2
                input_w = max(20, total_w - accept_w - 5)
                input_x = screen_tl[0]
                input_y = screen_tl[1] + screen_size[1] - box_h - 5
                input_rect = pygame.Rect(input_x, input_y, input_w, box_h)
                # dibujar caja de entrada (blanca con borde negro)
                pygame.draw.rect(screen, (255, 255, 255), input_rect)
                pygame.draw.rect(screen, (0, 0, 0), input_rect, 2)
                # dibujar texto
                screen.blit(input_surf, (input_x + 5, input_y + (box_h - text_h) // 2))
                self.state.rename_input_rect = input_rect
                # dibujar botón de aceptar (azul profesional)
                accept_rect = pygame.Rect(input_rect.right + 5, input_y, accept_w, box_h)
                pygame.draw.rect(screen, (0, 120, 215), accept_rect)
                pygame.draw.rect(screen, (255, 255, 255), accept_rect, 2)
                btn_font = pygame.font.SysFont(None, int(box_h * 0.6))
                ok_surf = btn_font.render("Aceptar", True, (255, 255, 255))
                screen.blit(ok_surf, (accept_rect.centerx - ok_surf.get_width() // 2,
                                      accept_rect.centery - ok_surf.get_height() // 2))
                self.state.rename_accept_rect = accept_rect
                # dibujar cursor parpadeante
                now = pygame.time.get_ticks()
                if (now // 500) % 2 == 0:
                    caret_x = input_x + 5 + input_surf.get_width()
                    caret_y1 = input_y + padding_y
                    caret_y2 = input_y + box_h - padding_y
                    pygame.draw.line(screen, (0, 0, 0), (caret_x, caret_y1), (caret_x, caret_y2), 2)
            else:
                label = self.font.render(zone, True, (255, 255, 255))
                # Escalar label para que quepa en la zona
                label_w, label_h = label.get_size()
                max_w, max_h = screen_size
                if label_w > max_w or label_h > max_h:
                    scale = min(max_w / label_w, max_h / label_h)
                    new_size = (int(label_w * scale), int(label_h * scale))
                    label = pygame.transform.smoothscale(label, new_size)
                # centrar label en la zona
                label_rect = label.get_rect(
                    center=(screen_tl[0] + screen_size[0] // 2,
                            screen_tl[1] + screen_size[1] // 2)
                )
                screen.blit(label, label_rect)
        # Render Map Editor toolbar and dropdown
        toolbar = self.controller.toolbar
        # Draw toolbar icon
        screen.blit(toolbar.icon, (toolbar.x, toolbar.y))
        toolbar.icon_rect = pygame.Rect(toolbar.x, toolbar.y, toolbar.size, toolbar.size)
        # Draw tile layer visibility dropdown
        if self.state.layers_view_open:
            font = pygame.font.SysFont("Arial", 14)
            drop_x = toolbar.x + toolbar.size + toolbar.padding
            drop_y = toolbar.y
            toolbar.option_rects.clear()
            # Show All, Hide All, each tile layer, and Buildings
            keys = ["show_all", "hide_all"] + list(Layer) + ["buildings"]
            for idx, key in enumerate(keys):
                ry = drop_y + idx * BTN_H
                rect = pygame.Rect(drop_x, ry, BTN_W, BTN_H)
                toolbar.option_rects[key] = rect
                pygame.draw.rect(screen, (20, 20, 20), rect)
                if key == "show_all" or key == "hide_all":
                    border_color = (255, 255, 255)
                elif isinstance(key, Layer):
                    border_color = (0, 255, 0) if self.state.visible_layers[key] else (255, 0, 0)
                else:  # buildings
                    border_color = (128, 0, 128) if self.state.show_buildings else (255, 0, 0)
                pygame.draw.rect(screen, border_color, rect, 2)
                text = ("Show All" if key == "show_all" else
                        "Hide All" if key == "hide_all" else
                        key.name if isinstance(key, Layer) else
                        "Buildings")
                text_surf = font.render(text, True, (255, 255, 255))
                screen.blit(text_surf, (drop_x + 5, ry + (BTN_H - text_surf.get_height()) // 2))