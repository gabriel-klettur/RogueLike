import pygame
from roguelike_ui.ui_blocker import register_blocker, is_blocked

try:
    from roguelike_engine.config.config_tiles import TILE_SIZE
except Exception:
    TILE_SIZE = 32

from roguelike_editors.tiles.tiles_editor_config import THUMB, PAD, CLR_HOVER, CLR_SELECTION


class BuildingCollidersPanelView:
    def __init__(self, state, editor_state, model):
        self.state = state
        self.editor_state = editor_state
        self.model = model
        # Referencia opcional al BuildingsToolBarPanelView para alinear
        self.toolbar_view = None

    def _render_building_collision_overlay(self, screen, camera, building):
        if not getattr(building, 'collision_map', None):
            return
        try:
            rows = len(building.collision_map)
            cols = len(building.collision_map[0]) if rows > 0 else 0
            if rows <= 0 or cols <= 0:
                return
            img_w = float(building.image.get_width())
            img_h = float(building.image.get_height())
            cw_pix = max(1.0, img_w / cols)
            ch_pix = max(1.0, img_h / rows)
        except Exception:
            cw_pix = float(TILE_SIZE)
            ch_pix = float(TILE_SIZE)
        cell_w, cell_h = camera.scale((int(cw_pix), int(ch_pix)))
        for ry, row in enumerate(building.collision_map):
            for cx, val in enumerate(row):
                if val == "#":
                    wx = building.x + int(cx * cw_pix)
                    wy = building.y + int(ry * ch_pix)
                    sx, sy = camera.apply((wx, wy))
                    overlay = pygame.Surface((max(1, cell_w), max(1, cell_h)), pygame.SRCALPHA)
                    overlay.fill((255, 0, 0, 100))
                    screen.blit(overlay, (sx, sy))

    def _render_picker(self, screen, editor_view=None):
        options = [("#", "Solid"), (".", "Walk")]
        w = len(options) * (THUMB + PAD) + PAD
        label_font = pygame.font.SysFont("Arial", 14)
        char_font = pygame.font.SysFont("Arial", THUMB)
        # Altura para la hilera de opciones + botón 'Save CU'
        top_h = THUMB + PAD + label_font.get_height() + PAD
        btn_h = label_font.get_height() + PAD
        h = top_h + PAD + btn_h
        mouse_pos = pygame.mouse.get_pos()
        surf = pygame.Surface((w, h), pygame.SRCALPHA)
        surf.fill((20, 20, 20, 235))

        # Anclaje por defecto: alineado a la DERECHA y a la misma altura que el botón 'buildings_colliders'
        if self.model.picker_pos is None:
            try:
                px, py = 0, 0
                # Posicionar a la DERECHA del toolbar de Buildings
                tb_view = getattr(self, 'toolbar_view', None)
                if tb_view is not None and hasattr(tb_view, 'widget'):
                    try:
                        tb_widget = tb_view.widget
                        tb_pos = tb_widget.panel.pos or (tb_widget.x, tb_widget.y)
                        tb_w, _ = tb_widget.panel.surface.get_size()
                        px = int(tb_pos[0] + tb_w + 8)
                        # Y: alineado con el icono de colisiones si existe; si no, top del toolbar
                        coll_rect = tb_widget.icon_rects.get('buildings_colliders')
                        py = int(coll_rect.top) if coll_rect is not None else int(tb_pos[1])
                    except Exception:
                        pass
                # Fallback: bajo el título del editor
                if px == 0 and py == 0:
                    title_rect = getattr(editor_view, '_last_title_rect', None) if editor_view is not None else None
                    if title_rect is None and editor_view is not None and hasattr(editor_view, 'title_view'):
                        title_widget = getattr(editor_view.title_view, 'widget', None)
                        if title_widget is not None and hasattr(title_widget, 'rect'):
                            title_rect = title_widget.rect
                    if title_rect is not None:
                        px = int(title_rect.left)
                        py = int(title_rect.bottom + 8)
            except Exception:
                px, py = 0, 0
            self.model.picker_pos = (px, py)
        else:
            px, py = self.model.picker_pos or (0, 0)

        self.model.picker_panel_size = (w, h)
        self.model.picker_rects.clear()
        for i, (ch, label) in enumerate(options):
            x = PAD + i * (THUMB + PAD)
            y = PAD
            color = (255, 0, 0) if ch == "#" else (200, 200, 200)
            text_surf = char_font.render(ch, True, color)
            surf.blit(text_surf, (x + (THUMB - text_surf.get_width()) // 2,
                                  y + (THUMB - text_surf.get_height()) // 2))
            abs_rect = pygame.Rect(px + x, py + y, THUMB, THUMB)
            self.model.picker_rects[ch] = abs_rect
            # hover/selection
            if abs_rect.collidepoint(mouse_pos):
                pygame.draw.rect(surf, CLR_HOVER, (x, y, THUMB, THUMB), 3)
            elif self.model.choice == ch:
                pygame.draw.rect(surf, CLR_SELECTION, (x, y, THUMB, THUMB), 3)
            lbl_surf = label_font.render(label, True, (255, 255, 255))
            surf.blit(lbl_surf, (x + (THUMB - lbl_surf.get_width()) // 2,
                                 y + THUMB + PAD))
        # Botón 'Save CU' (guarda overrides por instancia; usa split primero y fallback a legacy)
        btn_w = w - 2 * PAD
        btn_x = PAD
        btn_y = top_h + PAD
        # Área interactiva absoluta
        abs_btn_rect = pygame.Rect(px + btn_x, py + btn_y, btn_w, btn_h)
        self.model.picker_rects['save_cu'] = abs_btn_rect
        # Fondo y borde del botón
        pygame.draw.rect(surf, (60, 60, 60), (btn_x, btn_y, btn_w, btn_h), border_radius=4)
        # Hover/selección del botón
        if abs_btn_rect.collidepoint(mouse_pos):
            pygame.draw.rect(surf, CLR_HOVER, (btn_x, btn_y, btn_w, btn_h), 3, border_radius=4)
        else:
            pygame.draw.rect(surf, (120, 120, 120), (btn_x, btn_y, btn_w, btn_h), 2, border_radius=4)
        # Texto del botón
        btn_text = label_font.render("Save CU overrides", True, (255, 255, 255))
        surf.blit(btn_text, (btn_x + (btn_w - btn_text.get_width()) // 2,
                             btn_y + (btn_h - btn_text.get_height()) // 2))
        screen.blit(surf, (px, py))
        # Registrar zona de bloqueo para evitar hover sobre edificios
        try:
            register_blocker(pygame.Rect(px, py, w, h))
        except Exception:
            pass
        # Borde parpadeante en amarillo cuando el panel está activo
        try:
            if getattr(self.model, 'active', False):
                ticks = pygame.time.get_ticks()
                flash_on = ((ticks // 350) % 2) == 0
                if flash_on:
                    border_rect = pygame.Rect(px - 2, py - 2, w + 4, h + 4)
                    pygame.draw.rect(screen, (255, 255, 0), border_rect, 4)
        except Exception:
            pass

    def render(self, screen, camera, buildings, editor_view=None):
        # Respect UI blockers: do not draw any hover/active overlays while mouse is over UI
        blocked = False
        try:
            mx, my = pygame.mouse.get_pos()
            blocked = is_blocked(mx, my)
        except Exception:
            blocked = False

        # Determine active building (persistent selection) for overlay rendering
        try:
            sb = getattr(self.editor_state, 'active_building', None)
        except Exception:
            sb = None
        drawn_for = None

        # Show colliders overlay and cyan outline only for the active building
        # when colliders mode is active, avoiding hover-based overlays. Active visuals
        # persist even if the mouse is over a UI blocker (hover is the only thing cleared).
        try:
            colliders_mode = bool(getattr(self.editor_state, 'colliders_mode', False))
        except Exception:
            colliders_mode = False
        if colliders_mode:
            b = sb
            if b and getattr(b, 'collision_map', None) and b is not drawn_for:
                self._render_building_collision_overlay(screen, camera, b)
            if b and getattr(b, 'collision_map', None):
                x, y = camera.apply((b.x, b.y))
                w, h = camera.scale(b.image.get_size())
                pygame.draw.rect(screen, (0, 255, 255), (x, y, w, h), 4)

        # Picker UI should still render and register as a blocker
        if self.model.picker_open:
            self._render_picker(screen, editor_view)
