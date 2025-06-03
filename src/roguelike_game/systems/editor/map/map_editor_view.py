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
        # Show full-screen loading overlay and progress bar for async tools
        if self.state.executing_tool:
            sw, sh = screen.get_size()
            # draw dim overlay
            overlay = pygame.Surface((sw, sh), pygame.SRCALPHA)
            overlay.fill((0, 0, 0, 150))
            screen.blit(overlay, (0, 0))
            # draw progress bar
            bar_w, bar_h = sw * 0.5, 20
            bar_x = (sw - bar_w) / 2
            bar_y = (sh - bar_h) / 2
            pygame.draw.rect(screen, (50, 50, 50), (bar_x, bar_y, bar_w, bar_h))
            total = max(self.state.execution_total, 1)
            progress = self.state.execution_index / total
            fill_w = bar_w * progress
            pygame.draw.rect(screen, (0, 150, 215), (bar_x, bar_y, fill_w, bar_h))
            pygame.draw.rect(screen, (255, 255, 255), (bar_x, bar_y, bar_w, bar_h), 2)
            font_small = pygame.font.SysFont(None, 24)
            label = f"{self.state.executing_tool.replace('_', ' ').title()}: {int(progress*100)}%"
            text_surf = font_small.render(label, True, (255, 255, 255))
            screen.blit(text_surf, (bar_x + (bar_w - text_surf.get_width()) / 2,
                                     bar_y + (bar_h - text_surf.get_height()) / 2))
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
            # Highlight pending deletion
            if self.state.pending_delete_zone == zone:
                outline_color = (255, 0, 0)
                fill_color = (255, 0, 0, 50)
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
        # Draw colliders overlay on top of zones
        if self.state.show_colliders:
            for tile in self.map_manager.solid_tiles:
                tl = camera.apply((tile.x, tile.y))
                size = camera.scale((TILE_SIZE, TILE_SIZE))
                overlay = pygame.Surface(size, pygame.SRCALPHA)
                overlay.fill((255, 0, 0, 80))
                screen.blit(overlay, tl)
                pygame.draw.rect(screen, (255, 0, 0), (*tl, *size), 1)
        # Render Map Editor toolbar and dropdown
        toolbar = self.controller.toolbar
        # Draw toolbar icon
        screen.blit(toolbar.icon, (toolbar.x, toolbar.y))
        toolbar.icon_rect = pygame.Rect(toolbar.x, toolbar.y, toolbar.size, toolbar.size)
        # Draw Add Zone button
        add_x = toolbar.x
        add_y = toolbar.y + toolbar.size + toolbar.padding
        screen.blit(toolbar.add_icon, (add_x, add_y))
        toolbar.add_rect = pygame.Rect(add_x, add_y, toolbar.size, toolbar.size)
        if self.state.add_zone_mode:
            pygame.draw.rect(screen, (0, 255, 0), toolbar.add_rect, 3)
        # Draw Delete Zone button
        del_x = toolbar.x
        del_y = toolbar.y + 2 * (toolbar.size + toolbar.padding)
        screen.blit(toolbar.delete_icon, (del_x, del_y))
        toolbar.delete_rect = pygame.Rect(del_x, del_y, toolbar.size, toolbar.size)
        if self.state.delete_zone_mode:
            pygame.draw.rect(screen, (255, 0, 0), toolbar.delete_rect, 3)
        # Draw Paint Tiles Zone button
        pt_x = toolbar.x
        pt_y = toolbar.y + 3 * (toolbar.size + toolbar.padding)
        screen.blit(toolbar.paint_tiles_icon, (pt_x, pt_y))
        toolbar.paint_tiles_rect = pygame.Rect(pt_x, pt_y, toolbar.size, toolbar.size)
        if self.state.paint_tiles_mode:
            pygame.draw.rect(screen, (0, 0, 255), toolbar.paint_tiles_rect, 3)
        # Draw Clear Colliders Zone button
        cc_x = toolbar.x
        cc_y = toolbar.y + 4 * (toolbar.size + toolbar.padding)
        screen.blit(toolbar.clear_colliders_icon, (cc_x, cc_y))
        toolbar.clear_colliders_rect = pygame.Rect(cc_x, cc_y, toolbar.size, toolbar.size)
        if self.state.clear_colliders_mode:
            pygame.draw.rect(screen, (255, 165, 0), toolbar.clear_colliders_rect, 3)
        # Draw Paint Colliders Zone button
        pc_x = toolbar.x
        pc_y = toolbar.y + 5 * (toolbar.size + toolbar.padding)
        screen.blit(toolbar.paint_colliders_icon, (pc_x, pc_y))
        toolbar.paint_colliders_rect = pygame.Rect(pc_x, pc_y, toolbar.size, toolbar.size)
        if self.state.paint_colliders_mode:
            pygame.draw.rect(screen, (128, 0, 128), toolbar.paint_colliders_rect, 3)
        # Draw tile layer visibility dropdown
        if self.state.layers_view_open:
            font = pygame.font.SysFont("Arial", 14)
            drop_x = toolbar.x + toolbar.size + toolbar.padding
            drop_y = toolbar.y
            toolbar.option_rects.clear()
            # Show All, Hide All, each tile layer, and Buildings
            keys = ["show_all", "hide_all"] + list(Layer) + ["buildings", "colliders"]
            for idx, key in enumerate(keys):
                ry = drop_y + idx * BTN_H
                rect = pygame.Rect(drop_x, ry, BTN_W, BTN_H)
                toolbar.option_rects[key] = rect
                pygame.draw.rect(screen, (20, 20, 20), rect)
                if key in ("show_all", "hide_all"):  # show/hide all
                    border_color = (255, 255, 255)
                elif isinstance(key, Layer):  # tile layers
                    border_color = (0, 255, 0) if self.state.visible_layers[key] else (255, 0, 0)
                elif key == "buildings":  # buildings layer
                    border_color = (128, 0, 128) if self.state.show_buildings else (255, 0, 0)
                elif key == "colliders":  # collision layer
                    border_color = (255, 255, 0) if self.state.show_colliders else (255, 0, 0)
                pygame.draw.rect(screen, border_color, rect, 2)
                text = ("Show All" if key == "show_all" else
                        "Hide All" if key == "hide_all" else
                        key.name if isinstance(key, Layer) else
                        "Buildings" if key == "buildings" else
                        "Colliders")
                text_surf = font.render(text, True, (255, 255, 255))
                screen.blit(text_surf, (drop_x + 5, ry + (BTN_H - text_surf.get_height()) // 2))
        # Render delete confirmation dialog
        if self.state.confirm_delete_zone and self.state.pending_delete_zone:
            sw, sh = screen.get_size()
            msg = f"Eliminar zona {self.state.pending_delete_zone}?"
            font = pygame.font.SysFont(None, 24)
            text_surf = font.render(msg, True, (255, 255, 255))
            box_w = text_surf.get_width() + 20
            box_h = text_surf.get_height() + 60
            box_x = (sw - box_w) // 2
            box_y = (sh - box_h) // 2
            box_rect = pygame.Rect(box_x, box_y, box_w, box_h)
            pygame.draw.rect(screen, (0, 0, 0), box_rect)
            pygame.draw.rect(screen, (255, 255, 255), box_rect, 2)
            screen.blit(text_surf, (box_x + 10, box_y + 10))
            # Yes button
            yes_w, yes_h = 60, 30
            yes_x = box_x + 10
            yes_y = box_y + box_h - yes_h - 10
            yes_rect = pygame.Rect(yes_x, yes_y, yes_w, yes_h)
            pygame.draw.rect(screen, (0, 200, 0), yes_rect)
            pygame.draw.rect(screen, (255, 255, 255), yes_rect, 2)
            yes_surf = font.render("Sí", True, (255, 255, 255))
            screen.blit(yes_surf, (yes_rect.centerx - yes_surf.get_width() // 2, yes_rect.centery - yes_surf.get_height() // 2))
            self.state.confirm_yes_rect = yes_rect
            # No button
            no_w, no_h = 60, 30
            no_x = yes_rect.right + 10
            no_y = yes_y
            no_rect = pygame.Rect(no_x, no_y, no_w, no_h)
            pygame.draw.rect(screen, (200, 0, 0), no_rect)
            pygame.draw.rect(screen, (255, 255, 255), no_rect, 2)
            no_surf = font.render("No", True, (255, 255, 255))
            screen.blit(no_surf, (no_rect.centerx - no_surf.get_width() // 2, no_rect.centery - no_surf.get_height() // 2))
            self.state.confirm_no_rect = no_rect
        # Render paint tiles confirmation dialog
        if self.state.confirm_paint_tiles and self.state.pending_paint_tiles_zone:
            sw, sh = screen.get_size()
            msg = f"Pintar tiles de zona {self.state.pending_paint_tiles_zone}?"
            font = pygame.font.SysFont(None, 24)
            text_surf = font.render(msg, True, (255, 255, 255))
            box_w = text_surf.get_width() + 20
            box_h = text_surf.get_height() + 60
            box_x = (sw - box_w) // 2
            box_y = (sh - box_h) // 2
            box_rect = pygame.Rect(box_x, box_y, box_w, box_h)
            pygame.draw.rect(screen, (0, 0, 0), box_rect)
            pygame.draw.rect(screen, (255, 255, 255), box_rect, 2)
            screen.blit(text_surf, (box_x + 10, box_y + 10))
            # Yes button
            yes_w, yes_h = 60, 30
            yes_x = box_x + 10
            yes_y = box_y + box_h - yes_h - 10
            yes_rect = pygame.Rect(yes_x, yes_y, yes_w, yes_h)
            pygame.draw.rect(screen, (0, 200, 0), yes_rect)
            pygame.draw.rect(screen, (255, 255, 255), yes_rect, 2)
            yes_surf = font.render("Sí", True, (255, 255, 255))
            screen.blit(yes_surf, (yes_rect.centerx - yes_surf.get_width() // 2, yes_rect.centery - yes_surf.get_height() // 2))
            self.state.confirm_paint_yes_rect = yes_rect
            # No button
            no_w, no_h = 60, 30
            no_x = yes_rect.right + 10
            no_y = yes_y
            no_rect = pygame.Rect(no_x, no_y, no_w, no_h)
            pygame.draw.rect(screen, (200, 0, 0), no_rect)
            pygame.draw.rect(screen, (255, 255, 255), no_rect, 2)
            no_surf = font.render("No", True, (255, 255, 255))
            screen.blit(no_surf, (no_rect.centerx - no_surf.get_width() // 2, no_rect.centery - no_surf.get_height() // 2))
            self.state.confirm_paint_no_rect = no_rect
        # Render clear colliders confirmation dialog
        if self.state.confirm_clear_colliders and self.state.pending_clear_colliders_zone:
            sw, sh = screen.get_size()
            msg = f"Vaciar colliders de zona {self.state.pending_clear_colliders_zone}?"
            font = pygame.font.SysFont(None, 24)
            text_surf = font.render(msg, True, (255, 255, 255))
            box_w = text_surf.get_width() + 20
            box_h = text_surf.get_height() + 60
            box_x = (sw - box_w) // 2
            box_y = (sh - box_h) // 2
            box_rect = pygame.Rect(box_x, box_y, box_w, box_h)
            pygame.draw.rect(screen, (0, 0, 0), box_rect)
            pygame.draw.rect(screen, (255, 255, 255), box_rect, 2)
            screen.blit(text_surf, (box_x + 10, box_y + 10))
            # Yes button
            yes_w, yes_h = 60, 30
            yes_x = box_x + 10
            yes_y = box_y + box_h - yes_h - 10
            yes_rect = pygame.Rect(yes_x, yes_y, yes_w, yes_h)
            pygame.draw.rect(screen, (0, 200, 0), yes_rect)
            pygame.draw.rect(screen, (255, 255, 255), yes_rect, 2)
            yes_surf = font.render("Sí", True, (255, 255, 255))
            screen.blit(yes_surf, (yes_rect.centerx - yes_surf.get_width() // 2, yes_rect.centery - yes_surf.get_height() // 2))
            self.state.confirm_clear_colliders_yes_rect = yes_rect
            # No button
            no_w, no_h = 60, 30
            no_x = yes_rect.right + 10
            no_y = yes_y
            no_rect = pygame.Rect(no_x, no_y, no_w, no_h)
            pygame.draw.rect(screen, (200, 0, 0), no_rect)
            pygame.draw.rect(screen, (255, 255, 255), no_rect, 2)
            no_surf = font.render("No", True, (255, 255, 255))
            screen.blit(no_surf, (no_rect.centerx - no_surf.get_width() // 2, no_rect.centery - no_surf.get_height() // 2))
            self.state.confirm_clear_colliders_no_rect = no_rect
        # Render paint colliders confirmation dialog
        if self.state.confirm_paint_colliders and self.state.pending_paint_colliders_zone:
            sw, sh = screen.get_size()
            msg = f"Pintar colliders de zona {self.state.pending_paint_colliders_zone}?"
            font = pygame.font.SysFont(None, 24)
            text_surf = font.render(msg, True, (255, 255, 255))
            box_w = text_surf.get_width() + 20
            box_h = text_surf.get_height() + 60
            box_x = (sw - box_w) // 2
            box_y = (sh - box_h) // 2
            box_rect = pygame.Rect(box_x, box_y, box_w, box_h)
            pygame.draw.rect(screen, (0, 0, 0), box_rect)
            pygame.draw.rect(screen, (255, 255, 255), box_rect, 2)
            screen.blit(text_surf, (box_x + 10, box_y + 10))
            # Yes button
            yes_w, yes_h = 60, 30
            yes_x = box_x + 10
            yes_y = box_y + box_h - yes_h - 10
            yes_rect = pygame.Rect(yes_x, yes_y, yes_w, yes_h)
            pygame.draw.rect(screen, (0, 200, 0), yes_rect)
            pygame.draw.rect(screen, (255, 255, 255), yes_rect, 2)
            yes_surf = font.render("Sí", True, (255, 255, 255))
            screen.blit(yes_surf, (yes_rect.centerx - yes_surf.get_width() // 2, yes_rect.centery - yes_surf.get_height() // 2))
            self.state.confirm_paint_colliders_yes_rect = yes_rect
            # No button
            no_w, no_h = 60, 30
            no_x = yes_rect.right + 10
            no_y = yes_y
            no_rect = pygame.Rect(no_x, no_y, no_w, no_h)
            pygame.draw.rect(screen, (200, 0, 0), no_rect)
            pygame.draw.rect(screen, (255, 255, 255), no_rect, 2)
            no_surf = font.render("No", True, (255, 255, 255))
            screen.blit(no_surf, (no_rect.centerx - no_surf.get_width() // 2, no_rect.centery - no_surf.get_height() // 2))
            self.state.confirm_paint_colliders_no_rect = no_rect
        # Render add zone confirmation dialog
        if self.state.confirm_add_zone and self.state.pending_add_zone_coords:
            sw, sh = screen.get_size()
            tx, ty = self.state.pending_add_zone_coords
            msg = f"Agregar zona en ({tx},{ty})?"
            font = pygame.font.SysFont(None, 24)
            text_surf = font.render(msg, True, (255, 255, 255))
            box_w = text_surf.get_width() + 20
            box_h = text_surf.get_height() + 60
            box_x = (sw - box_w) // 2
            box_y = (sh - box_h) // 2
            box_rect = pygame.Rect(box_x, box_y, box_w, box_h)
            pygame.draw.rect(screen, (0, 0, 0), box_rect)
            pygame.draw.rect(screen, (255, 255, 255), box_rect, 2)
            screen.blit(text_surf, (box_x + 10, box_y + 10))
            # Yes button
            yes_w, yes_h = 60, 30
            yes_x = box_x + 10
            yes_y = box_y + box_h - yes_h - 10
            yes_rect = pygame.Rect(yes_x, yes_y, yes_w, yes_h)
            pygame.draw.rect(screen, (0, 200, 0), yes_rect)
            pygame.draw.rect(screen, (255, 255, 255), yes_rect, 2)
            yes_surf = font.render("Sí", True, (255, 255, 255))
            screen.blit(yes_surf, (yes_rect.centerx - yes_surf.get_width() // 2,
                                  yes_rect.centery - yes_surf.get_height() // 2))
            self.state.confirm_add_yes_rect = yes_rect
            # No button
            no_w, no_h = 60, 30
            no_x = yes_rect.right + 10
            no_y = yes_y
            no_rect = pygame.Rect(no_x, no_y, no_w, no_h)
            pygame.draw.rect(screen, (200, 0, 0), no_rect)
            pygame.draw.rect(screen, (255, 255, 255), no_rect, 2)
            no_surf = font.render("No", True, (255, 255, 255))
            screen.blit(no_surf, (no_rect.centerx - no_surf.get_width() // 2,
                                  no_rect.centery - no_surf.get_height() // 2))
            self.state.confirm_add_no_rect = no_rect
        # Determinate progress bar for async tool execution
        if self.state.executing_tool:
            sw, sh = screen.get_size()
            bar_w, bar_h = sw * 0.5, 8
            bar_x = (sw - bar_w) / 2
            bar_y = sh * 0.85
            # track background
            pygame.draw.rect(screen, (50, 50, 50), (bar_x, bar_y, bar_w, bar_h))
            # fill according to progress
            total = max(self.state.execution_total, 1)
            progress = self.state.execution_index / total
            fill_w = bar_w * progress
            pygame.draw.rect(screen, (0, 150, 215), (bar_x, bar_y, fill_w, bar_h))
            # border
            pygame.draw.rect(screen, (255, 255, 255), (bar_x, bar_y, bar_w, bar_h), 1)
            # label
            font_small = pygame.font.SysFont(None, 20)
            label = f"{self.state.executing_tool.replace('_', ' ').title()}: {int(progress*100)}%"
            text_surf = font_small.render(label, True, (255, 255, 255))
            screen.blit(text_surf, (bar_x, bar_y - bar_h - 2))