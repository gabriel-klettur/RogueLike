import pygame
from roguelike_ui.widgets.menu_renderer import MenuRenderer
from roguelike_ui.widgets.menu_configurator import MenuConfigurator
from roguelike_ui.widgets.sounds_configurator import SoundsConfigurator

class OptionsConfigurator:
    """
    Submenú de Opciones que ofrece elección entre:
    - Inputs (reutiliza MenuConfigurator)
    - Sounds (usa SoundsConfigurator)

    Presenta un menú sencillo con estética del menú principal.
    """
    def __init__(self, screen, font, input_configurator: MenuConfigurator, audio_config, on_audio_change=None, underlay_provider=None, base_font_size: int | None = None):
        self.screen = screen
        self.font = font
        self.menu_configurator = input_configurator
        self.audio_config = audio_config
        self.on_audio_change = on_audio_change
        # Función opcional que dibuja el fondo y devuelve una Y mínima para el panel
        # Firma esperada: underlay_provider(screen) -> panel_top_min | None
        self.underlay_provider = underlay_provider
        # Renderer con tipografía estandarizada
        if isinstance(base_font_size, int) and base_font_size > 6:
            self.renderer = MenuRenderer(font_size=base_font_size)
        else:
            try:
                font_size = int(font.get_height()) if font else 18
            except Exception:
                font_size = 18
            self.renderer = MenuRenderer(font_size=font_size)
        self.base_font_size = self.renderer.font_size
        self.selected = 0
        self.scroll_offset = 0
        self.options = ["Inputs", "Sounds", "Volver"]
        # Último layout dibujado para hit-testing
        self._last_layout = None

    def configure(self):
        running = True
        clock = pygame.time.Clock()
        while running:
            for event in pygame.event.get():
                if event.type == pygame.QUIT:
                    running = False
                    break
                if event.type == pygame.KEYDOWN:
                    if event.key in (pygame.K_ESCAPE,):
                        running = False
                        break
                    elif event.key in (pygame.K_UP, pygame.K_w, pygame.K_a):
                        self.selected = (self.selected - 1) % len(self.options)
                    elif event.key in (pygame.K_DOWN, pygame.K_s, pygame.K_d):
                        self.selected = (self.selected + 1) % len(self.options)
                    elif event.key in (pygame.K_RETURN, pygame.K_SPACE):
                        sel = self.options[self.selected]
                        if sel == "Inputs":
                            # Crear una instancia fresca para asegurar que los cambios de layout/underlay se apliquen
                            MenuConfigurator(
                                self.menu_configurator.config,
                                self.screen,
                                self.font,
                                underlay_provider=self.underlay_provider,
                                base_font_size=self.base_font_size,
                            ).configure()
                        elif sel == "Sounds":
                            SoundsConfigurator(
                                screen=self.screen,
                                audio_config=self.audio_config,
                                on_change=self.on_audio_change,
                                font=self.font,
                                underlay_provider=self.underlay_provider,
                                base_font_size=self.base_font_size,
                            ).configure()
                        elif sel == "Volver":
                            running = False
                            break
                elif event.type == pygame.MOUSEWHEEL:
                    # Scroll simple si hay overflow (poco probable con 3 items)
                    self.scroll_offset = max(0, self.scroll_offset - event.y)
                elif event.type == pygame.MOUSEMOTION:
                    # Hover: actualizar selección según posición del ratón
                    lay = self._last_layout or {}
                    item_rects = lay.get('item_rects', [])
                    for i, r in enumerate(item_rects):
                        if r and r.collidepoint(event.pos):
                            self.selected = i
                            break
                elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                    # Click sobre opción -> activar
                    lay = self._last_layout or {}
                    item_rects = lay.get('item_rects', [])
                    for i, r in enumerate(item_rects):
                        if r and r.collidepoint(event.pos):
                            self.selected = i
                            sel = self.options[self.selected]
                            if sel == "Inputs":
                                # Crear una instancia fresca para asegurar que los cambios de layout/underlay se apliquen
                                MenuConfigurator(
                                    self.menu_configurator.config,
                                    self.screen,
                                    self.font,
                                    underlay_provider=self.underlay_provider,
                                    base_font_size=self.base_font_size,
                                ).configure()
                            elif sel == "Sounds":
                                SoundsConfigurator(
                                    screen=self.screen,
                                    audio_config=self.audio_config,
                                    on_change=self.on_audio_change,
                                    font=self.font,
                                    underlay_provider=self.underlay_provider,
                                    base_font_size=self.base_font_size,
                                ).configure()
                            elif sel == "Volver":
                                running = False
                            break
            # Dibujar
            self._draw()
            pygame.display.flip()
            clock.tick(60)

    def _draw(self):
        # Bajo-fondo (persistir background/logo del menú de inicio si aplica)
        panel_top_min = None
        if callable(self.underlay_provider):
            try:
                panel_top_min = self.underlay_provider(self.screen)
            except Exception:
                panel_top_min = None
        # Overlay general (oscurece ligeramente el fondo)
        overlay_rect = self.renderer._draw_overlay(self.screen)
        w, h = self.renderer._measure_menu(self.options)
        sw, sh = self.screen.get_size()
        w = min(w, int(sw * 0.6))
        h = min(h, int(sh * 0.5))
        panel_rect = self.renderer._center_rect(self.screen, (w, h))
        # Empujar panel hacia abajo si hay un logo encima
        if isinstance(panel_top_min, int) and panel_rect.top < panel_top_min:
            panel_rect.top = panel_top_min
        self.renderer._draw_shadow(self.screen, panel_rect)
        panel = self.renderer._draw_panel((w, h))
        # Items
        inner_h = h - self.renderer.padding_y * 2
        block_h = self.renderer.line_height + self.renderer.item_gap
        max_visible = max(1, (inner_h + self.renderer.item_gap) // block_h)
        total = len(self.options)
        if total <= max_visible:
            start = 0
            end = total
        else:
            max_offset = max(0, total - max_visible)
            self.scroll_offset = max(0, min(self.scroll_offset, max_offset))
            start = self.scroll_offset
            end = start + max_visible
        y = self.renderer.padding_y
        # Preparar layout para hit-testing
        item_rects = []
        for i in range(start, end):
            option = self.options[i]
            is_sel = (i == self.selected)
            if is_sel:
                pill_rect = pygame.Rect(0, 0, w - self.renderer.padding_x * 2, self.renderer.line_height)
                pill_rect.topleft = (self.renderer.padding_x, y)
                pygame.draw.rect(panel, self.renderer.highlight_color, pill_rect, border_radius=self.renderer.radius // 2)
                accent_rect = pygame.Rect(self.renderer.padding_x - 6, y, 4, self.renderer.line_height)
                pygame.draw.rect(panel, self.renderer.accent_color, accent_rect, border_radius=2)
            color = self.renderer.accent_color if is_sel else self.renderer.text_color
            text = self.renderer.font.render(option, True, color)
            tx = self.renderer.padding_x + 12
            ty = y + (self.renderer.line_height - text.get_height()) // 2
            panel.blit(text, (tx, ty))
            # Guardar rect de item (área clickable) en coords de pantalla
            item_rects.append(pygame.Rect(panel_rect.x + self.renderer.padding_x,
                                          panel_rect.y + y,
                                          w - self.renderer.padding_x * 2,
                                          self.renderer.line_height))
            y += block_h
        surface_to_blit = panel._surf if hasattr(panel, '_surf') else panel
        self.screen.blit(surface_to_blit, panel_rect.topleft)
        # Guardar layout
        self._last_layout = {
            'panel_rect': panel_rect,
            'item_rects': item_rects,
            'start': start,
            'end': end,
            'block_h': block_h,
        }
        return overlay_rect
